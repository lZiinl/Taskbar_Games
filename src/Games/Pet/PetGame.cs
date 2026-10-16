// 다람쥐의 산책·돌봄·먹이 추적·저장을 연결하는 UI 계층.
// 작은 창 자체가 움직이며, x는 화면 좌표의 창 왼쪽이고 캐릭터 중심은 x + 38이다.
// 배치된 도토리 목록은 창 수명 동안만 유지하고 포만감·기분·섭취 횟수만 파일에 저장한다.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Pet
{
    public sealed class PetGame : Form
    {
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly ToolStripDropDown menu = new ToolStripDropDown();
        readonly ToolStripButton status = new ToolStripButton();
        readonly ToolTip hint = new ToolTip();
        readonly bool persist;
        readonly string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskbar_Games", "squirrel.txt");
        PetSession session = new PetSession();
        string action = "idle";
        // remaining: 현재 행동의 남은 초, lastTime: 마지막 틱 시각, saveAge: 자동 저장 누적 초.
        // x: 창의 화면 X좌표, petCooldown: 다음 쓰다듬기를 허용할 Stopwatch 시각.
        double remaining = 120, lastTime, saveAge, x, petCooldown;
        // 먹이의 화면 중심 X와 선택적인 표시 창을 묶는다. 모델 테스트에서는 Marker가 null일 수 있다.
        sealed class DroppedAcorn { public double X; public AcornMarker Marker; }
        readonly List<DroppedAcorn> food = new List<DroppedAcorn>();
        bool placingFood;
        // 사용자가 시작한 쓰다듬기/낮잠을 먹이 추적보다 우선시키는 상태다.
        // 단순히 action만 바꾸면 다음 틱에 먹이 추적이 덮어쓰므로 별도 플래그가 필요하다.
        bool attendingCare;
        const int MaxAcorns = 5;
        int direction = 1;
        bool menuOpen;
        string saveError = "";

        // 실사용 생성자는 저장 파일을 읽는다. 테스트는 아래 생성자에 false를 전달해 사용자 파일을 보호한다.
        public PetGame() : this(true) { }
        // 고정 크기 투명 창, 아이콘 메뉴, 입력과 타이머를 구성한다.
        // 기본 모니터의 작업 영역은 매 이동 때 다시 읽어 작업표시줄/해상도 변화에 대응한다.
        internal PetGame(bool loadSettings)
        {
            persist = loadSettings;
            Text = "Taskbar_Games - Squirrel";
            Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            // Native window creation otherwise expands this tiny form, shortening its
            // travel range while food still uses the intended 76-pixel footprint.
            MinimumSize = MaximumSize = new Size(76, 56);
            ClientSize = new Size(76, 56);
            BackColor = TransparencyKey = Color.Magenta;
            TopMost = true;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            if (persist) LoadState();
            x = Screen.PrimaryScreen.WorkingArea.Left + 120;
            PositionPet();
            ArcadeTheme.Style(menu);
            // 문자 메뉴의 자동 너비 확장을 피하기 위해 이미지 전용 ToolStripButton 5개를 고정 폭으로 배치한다.
            menu.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            menu.AutoSize = false;
            menu.Size = new Size(130, 34);
            menu.ShowItemToolTips = true;
            status.Image = PetMenuIcon("info");
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripButton("도토리 놓기", PetMenuIcon("food"), delegate { menu.Close(); BeginInvoke(new Action(PlaceFood)); }));
            menu.Items.Add(new ToolStripButton("쓰다듬기", PetMenuIcon("love"), delegate { menu.Close(); Stroke(); }));
            menu.Items.Add(new ToolStripButton("낮잠 자기 / 깨우기", PetMenuIcon("sleep"), delegate { menu.Close(); StartAction(action == "sleep" ? "idle" : "sleep", action == "sleep" ? 120 : 300); }));
            menu.Items.Add(new ToolStripButton("게임 선택으로 돌아가기 (Esc)", PetMenuIcon("back"), delegate { menu.Close(); Close(); }));
            foreach (ToolStripItem item in menu.Items)
            {
                item.DisplayStyle = ToolStripItemDisplayStyle.Image;
                item.ImageScaling = ToolStripItemImageScaling.None;
                item.AutoSize = false; item.Size = new Size(24, 24);
                item.Overflow = ToolStripItemOverflow.Never;
                item.Margin = Padding.Empty; item.Padding = Padding.Empty;
                item.ToolTipText = item.Text; item.AccessibleName = item.Text;
            }
            menu.Opening += delegate { menuOpen = true; UpdateStatus(); };
            menu.Closed += delegate { menuOpen = false; lastTime = clock.Elapsed.TotalSeconds; };
            hint.SetToolTip(this, "다람쥐 · 클릭해서 쓰다듬기 · 우클릭으로 먹이와 낮잠");
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) Stroke();
                else if (e.Button == MouseButtons.Right) menu.Show(this, e.Location);
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            timer.Interval = 40;
            timer.Tick += Tick;
            timer.Start();
            FormClosing += delegate { SaveState(); };
        }

        // 상태 아이콘의 툴팁/접근성 설명과 5개 제한에 따른 먹이 버튼 활성화를 동기화한다.
        void UpdateStatus()
        {
            status.Text = String.Format("포만감 {0:0} · 기분 {1:0} · 도토리 {2}개{3}", session.Fullness, session.Mood, session.Meals, saveError);
            status.ToolTipText = status.AccessibleName = status.Text;
            menu.Items[1].Enabled = food.Count < MaxAcorns;
            menu.Items[1].ToolTipText = food.Count >= MaxAcorns ? "도토리는 최대 5개까지 놓을 수 있어요" : "도토리 놓기 (" + food.Count + "/5)";
        }
        // 18×18 투명 비트맵에 직접 픽셀 아이콘을 그린다. 반환 이미지의 소유권은 메뉴에 둔다.
        // 게임 Dispose에서 메뉴 항목별 Image를 직접 반환한다.
        static Bitmap PetMenuIcon(string kind)
        {
            Bitmap bitmap = new Bitmap(18, 18);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                if (kind == "food") AcornMarker.DrawAcorn(g, 3, 4);
                else if (kind == "love") DrawHeart(g, 2, 3, 2);
                else if (kind == "sleep") { PixelArt.Text(g, "Z", ArcadeTheme.Cyan, 2, 8); PixelArt.Text(g, "Z", ArcadeTheme.Cyan, 10, 2); }
                else if (kind == "info")
                {
                    using (Pen p = new Pen(ArcadeTheme.Neon)) g.DrawRectangle(p, 2, 2, 13, 13);
                    PixelArt.Text(g, "I", ArcadeTheme.Neon, 6, 5);
                }
                else
                {
                    Box(g, ArcadeTheme.Pink, 4, 8, 11, 2);
                    for (int i = 0; i < 5; i++) { Box(g, ArcadeTheme.Pink, 3 + i, 8 - i, 2, 2); Box(g, ArcadeTheme.Pink, 3 + i, 8 + i, 2, 2); }
                }
            }
            return bitmap;
        }
        // 섭취 애니메이션 중에는 돌봄을 차단하지만 먹이를 향해 걷는 중에는 즉시 전환한다.
        // 먹이가 있는 상태에서 깨우면 idle 대기를 건너뛰고 다음 틱부터 다시 추적한다.
        void StartAction(string next, double seconds)
        {
            // Finish eating before accepting another activity or meal.
            if (action == "eat") return;
            attendingCare = true;
            action = next; remaining = next == "idle" && food.Count > 0 ? 0 : seconds; Invalidate();
        }
        // 1.5초 쿨다운과 먹는 중 여부를 확인한 뒤 기분 보상 및 4초 하트 동작을 적용한다.
        void Stroke()
        {
            if (clock.Elapsed.TotalSeconds < petCooldown || action == "eat") return;
            session.Pet(); petCooldown = clock.Elapsed.TotalSeconds + 1.5;
            StartAction("love", 4); UpdateStatus();
        }
        // 메뉴 클릭 후 UI 큐에서 실행되는 모달 배치 흐름이다. 선택 중에는 타이머를 멈춘다.
        // 최대 5개 제한을 입구와 실제 추가 함수에서 모두 검사하며, 취소 시 기존 먹이는 유지한다.
        // finally에서 시각 기준을 재설정해 배치하는 동안의 시간이 한 번에 진행되지 않게 한다.
        void PlaceFood()
        {
            if (IsDisposed || placingFood || food.Count >= MaxAcorns) return;
            placingFood = true;
            timer.Stop();
            try
            {
                using (AcornPlacement picker = new AcornPlacement())
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        if (!SetFoodTarget(picker.SelectedX)) return;
                        DroppedAcorn added = food[food.Count - 1];
                        added.Marker = new AcornMarker();
                        PositionFood();
                        added.Marker.Show(this);
                    }
            }
            finally
            {
                placingFood = false;
                lastTime = clock.Elapsed.TotalSeconds;
                if (!IsDisposed) timer.Start();
            }
        }
        // 유효한 화면 X를 76px 창의 중심이 도달 가능한 구간으로 제한해 목록에 추가한다.
        // 실제 창 생성 없이도 호출 가능한 테스트 경계이다. 성공 여부를 반환하며 기존 먹이는 삭제하지 않는다.
        internal bool SetFoodTarget(double screenX)
        {
            if (food.Count >= MaxAcorns || Double.IsNaN(screenX) || Double.IsInfinity(screenX)) return false;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            food.Add(new DroppedAcorn { X = Math.Max(area.Left + 38, Math.Min(area.Right - 38, screenX)) });
            if (action != "eat" && !attendingCare) { action = "walk"; remaining = 30; }
            UpdateStatus();
            return true;
        }
        // 작업 영역이 바뀌면 모든 먹이 좌표를 재보정한다. 표시 창이 있는 먹이만 위치를 갱신한다.
        void PositionFood()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            foreach (DroppedAcorn acorn in food)
            {
                acorn.X = Math.Max(area.Left + 38, Math.Min(area.Right - 38, acorn.X));
                if (acorn.Marker != null) acorn.Marker.Location = new Point((int)acorn.X - 8, area.Bottom - 16);
            }
        }
        // 창 전체가 작업 영역 안에 남도록 왼쪽 X를 제한한 다음 아래쪽을 작업표시줄 위에 붙인다.
        // 창 폭 76px와 먹이 중심 여백 38px가 일치해야 양 끝 먹이에 도달할 수 있다.
        void PositionPet()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            x = Math.Max(area.Left, Math.Min(Math.Max(area.Left, area.Right - Width), x));
            Location = new Point((int)x, area.Bottom - Height);
        }
        // 타이머 지연이 큰 경우 한 번에 최대 0.25초만 반영한다. 메뉴가 열렸으면 상태/행동 모두 멈춘다.
        // 상태 감소 → 행동 이동 → 60초 자동 저장 → 다시 그리기 순서이며 낮잠은 250ms로 갱신한다.
        void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.25, Math.Max(0, now - lastTime)); lastTime = now;
            if (menuOpen) return;
            session.Advance(dt); saveAge += dt;
            AdvanceBehavior(dt);
            if (saveAge >= 60) { SaveState(); saveAge = 0; }
            timer.Interval = action == "sleep" ? 250 : 40;
            Invalidate();
        }
        // 행동 우선순위: 사용자 돌봄 유지 → 섭취 동작 유지 → 가까운 먹이 추적 → 자율 산책/휴식.
        // 시간은 초 단위다. 먹이를 향해 걸을 때는 매 단계 가장 가까운 먹이를 다시 고른다.
        internal void AdvanceBehavior(double dt)
        {
            if (dt <= 0 || Double.IsNaN(dt) || Double.IsInfinity(dt)) return;
            remaining -= dt;
            PositionFood();
            if (attendingCare && remaining > 0) { PositionPet(); return; }
            attendingCare = false;
            if (action == "eat" && remaining > 0) { PositionPet(); return; }
            if (food.Count > 0)
            {
                // 동일 거리라면 먼저 놓인 먹이를 유지한다. 비교 기준은 창 왼쪽이 아닌 다람쥐 중심이다.
                DroppedAcorn nearest = food[0];
                foreach (DroppedAcorn candidate in food)
                    if (Math.Abs(candidate.X - x - 38) < Math.Abs(nearest.X - x - 38)) nearest = candidate;
                double destination = nearest.X - 38;
                double distance = destination - x;
                if (Math.Abs(distance) > 0.1) direction = distance > 0 ? 1 : -1;
                // 한 번에 목표까지의 남은 거리 이상 움직이지 않아 도토리를 지나치거나 왕복 떨림이 생기지 않는다.
                double travel = Math.Min(Math.Abs(distance), 36 * dt);
                x += Math.Sign(distance) * travel;
                action = "walk";
                if (Math.Abs(destination - x) < 0.1)
                {
                    // 도착한 먹이만 목록/표시 창에서 제거한다. 보상은 이 지점에서 한 번만 주고 6초 섭취 동작을 시작한다.
                    food.Remove(nearest);
                    UpdateStatus();
                    if (nearest.Marker != null) nearest.Marker.Dispose();
                    action = "eat"; remaining = 6;
                    session.Feed(); SaveState();
                }
                PositionPet(); PositionFood();
                return;
            }
            if (action == "walk")
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                x += direction * 26 * dt;
                if (x <= area.Left || x >= area.Right - Width) direction = -direction;
            }
            PositionPet();
            if (remaining <= 0)
            {
                if (action == "walk" || action == "love" || action == "eat" || action == "sleep") { action = "idle"; remaining = 60 + random.Next(121); }
                else if (action == "idle" && random.Next(3) == 0) { action = "sleep"; remaining = 180 + random.Next(121); }
                else { action = "walk"; remaining = 120 + random.Next(121); direction = random.Next(2) == 0 ? -1 : 1; }
            }
        }

        // Whole-pixel geometry keeps the sprite crisp, including its mirrored pose.
        // 32px 너비의 원본 좌표계에서 꼬리·몸통·얼굴·팔다리를 그린다.
        // 왼쪽을 볼 때는 평행 이동 후 X축을 반전한다. Save/Restore로 호출자의 변환을 보존한다.
        // 시간에 따른 눈 깜빡임/꼬리 흔들림은 외형 효과이며 상태 수치에는 영향을 주지 않는다.
        internal static void DrawSquirrel(Graphics g, int left, int top, bool facingRight, string pose, double time, int scale)
        {
            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.TranslateTransform(left + (facingRight ? 0 : 32 * scale), top);
            g.ScaleTransform(facingRight ? scale : -scale, scale);
            Color outline = Color.FromArgb(110, 68, 56), fur = Color.FromArgb(215, 153, 106), light = Color.FromArgb(255, 201, 147), cream = Color.FromArgb(255, 240, 210);
            Color blush = Color.FromArgb(255, 153, 165), eye = Color.FromArgb(58, 38, 41);
            bool asleep = pose == "sleep";
            int bob = pose == "walk" ? (int)(time * 8) % 2 : 0;
            g.TranslateTransform(0, bob);
            // Broad curled tail, rounded cheeks and short feet make a compact silhouette.
            int sway = !asleep && Math.Sin(time * 1.5) > 0 ? 1 : 0;
            RoundPixel(g, outline, 0, 2 + sway, 15, 22, 4);
            RoundPixel(g, fur, 1, 3 + sway, 13, 19, 4);
            RoundPixel(g, light, 2, 4 + sway, 10, 14, 3);
            RoundPixel(g, cream, 4, 6 + sway, 6, 9, 2);
            Box(g, fur, 6, 10 + sway, 4, 6);
            RoundPixel(g, outline, 10, 16, 17, 11, 3);
            RoundPixel(g, fur, 11, 17, 15, 9, 3);
            if (asleep)
            {
                RoundPixel(g, outline, 18, 16, 14, 11, 3);
                RoundPixel(g, light, 19, 17, 12, 9, 3);
                Box(g, outline, 21, 15, 3, 3);
                Box(g, blush, 22, 16, 1, 2);
                Box(g, eye, 25, 21, 4, 1);
                Box(g, eye, 24, 20, 1, 1);
                Box(g, blush, 23, 23, 3, 1);
                RoundPixel(g, light, 5, 19, 17, 8, 3);
                RoundPixel(g, cream, 8, 21, 11, 4, 1);
            }
            else
            {
                RoundPixel(g, outline, 13, 3, 7, 7, 2);
                RoundPixel(g, light, 14, 4, 5, 5, 1);
                Box(g, blush, 15, 5, 3, 2);
                RoundPixel(g, outline, 25, 3, 6, 7, 2);
                RoundPixel(g, light, 26, 4, 4, 5, 1);
                Box(g, blush, 27, 5, 2, 2);
                RoundPixel(g, outline, 11, 7, 21, 16, 4);
                RoundPixel(g, light, 12, 8, 19, 14, 4);
                Box(g, fur, 19, 8, 2, 3);
                Box(g, cream, 22, 8, 2, 3);
                RoundPixel(g, cream, 14, 16, 17, 6, 2);
                bool happy = pose == "love";
                bool blink = time % 4.8 > 4.6;
                if (happy || blink)
                {
                    Box(g, eye, 17, 14, 4, 1); Box(g, eye, 26, 14, 4, 1);
                    if (happy) { Box(g, eye, 18, 13, 2, 1); Box(g, eye, 27, 13, 2, 1); }
                }
                else
                {
                    RoundPixel(g, eye, 16, 12, 5, 5, 1); RoundPixel(g, eye, 26, 12, 5, 5, 1);
                    Box(g, Color.White, 17, 12, 2, 2); Box(g, Color.White, 27, 12, 2, 2);
                    Box(g, blush, 19, 16, 1, 1); Box(g, blush, 29, 16, 1, 1);
                }
                Box(g, blush, 15, 17, 4, 2); Box(g, blush, 28, 17, 3, 2);
                Box(g, eye, 23, 17, 2, 1); Box(g, eye, 24, 18, 1, 1);
                Box(g, eye, 22, 19, 2, 1); Box(g, eye, 25, 19, 2, 1);
                Box(g, Color.White, 24, 20, 1, 1);
                RoundPixel(g, cream, 17, 22, 7, 4, 1);
                int step = pose == "walk" ? (int)(time * 8) % 2 * 2 : 0;
                RoundPixel(g, outline, 12 - step, 26, 6, 2, 1); RoundPixel(g, outline, 23 + step, 26, 6, 2, 1);
                if (pose == "eat")
                {
                    int nibble = (int)(time * 6) % 2;
                    RoundPixel(g, outline, 22, 22 + nibble, 7, 2, 1);
                    RoundPixel(g, fur, 23, 24 + nibble, 5, 3, 1);
                    Box(g, cream, 20, 23 + nibble, 3, 2); Box(g, cream, 28, 23 + nibble, 2, 2);
                }
                else { Box(g, light, 15, 22, 3, 2); Box(g, light, 25, 22, 3, 2); }
            }
            g.Restore(state);
        }
        // 위아래 행의 양 끝을 계단식으로 깎아 픽셀 느낌의 둥근 사각형을 만든다. 안티앨리어싱을 쓰지 않는다.
        static void RoundPixel(Graphics g, Color color, int x, int y, int width, int height, int corner)
        {
            for (int row = 0; row < height; row++)
            {
                int inset = Math.Max(0, corner - Math.Min(row, height - 1 - row));
                Box(g, color, x + inset, y + row, width - inset * 2, 1);
            }
        }
        // 정수 좌표 사각형을 채우는 기본 도형. Brush를 즉시 해제해 GDI 핸들 누적을 막는다.
        static void Box(Graphics g, Color color, int x, int y, int width, int height)
        { using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height); }

        // 캐릭터와 해당 행동의 짧은 라벨/하트만 그린다. 도토리는 별도 AcornMarker가 담당한다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            double time = clock.Elapsed.TotalSeconds;
            DrawSquirrel(e.Graphics, 22, 27, direction > 0, action, time, 1);
            if (action == "love")
            {
                int rise = (int)((4 - remaining) * 2.5);
                DrawHeart(e.Graphics, 32, 19 - rise, 1);
                DrawHeart(e.Graphics, 49, 26 - rise, 1);
            }
            string label = action == "sleep" ? "Z Z Z" : action == "eat" ? "YUM" : session.Fullness < 25 && action != "love" ? "ACORN" : "";
            if (label.Length > 0)
            {
                Box(e.Graphics, ArcadeTheme.Black, 17, 5, PixelArt.Width(label) + 10, 17);
                PixelArt.Text(e.Graphics, label, ArcadeTheme.Neon, 22, 10);
            }
        }
        // 문자 마스크의 X 위치만 채워 하트를 만든다. 게임 반응과 메뉴 아이콘이 같은 그림을 공유한다.
        static void DrawHeart(Graphics g, int x, int y, int scale)
        {
            string[] rows = { ".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." };
            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < rows[row].Length; col++)
                    if (rows[row][col] == 'X') Box(g, Color.FromArgb(244, 139, 157), x + col * scale, y + row * scale, scale, scale);
        }
        // 세 줄(포만감, 기분, 섭취 횟수)을 읽는다. 소수점은 시스템 언어와 무관하게 InvariantCulture로 해석한다.
        // 파싱 실패는 기본 상태를 유지하고, IO/권한 실패는 메뉴 상태에 표시할 오류 문자열로 남긴다.
        void LoadState()
        {
            try
            {
                if (!File.Exists(savePath)) return;
                string[] values = File.ReadAllLines(savePath);
                double fullness, mood; int meals;
                if (values.Length == 3 && Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out fullness) &&
                    Double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out mood) && Int32.TryParse(values[2], out meals))
                    session = new PetSession(fullness, mood, meals);
            }
            catch (IOException) { saveError = " · 저장 읽기 실패"; }
            catch (UnauthorizedAccessException) { saveError = " · 저장 읽기 실패"; }
        }
        // 테스트 인스턴스는 쓰기를 건너뛴다. 임시 파일에 전체 내용을 먼저 쓴 뒤 Replace/Move로 반영한다.
        // 중간 쓰기 실패로 기존 정상 파일을 부분 데이터로 덮는 위험을 줄이며 오류는 UI에서 확인할 수 있다.
        void SaveState()
        {
            if (!persist) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                string temporary = savePath + ".tmp";
                File.WriteAllLines(temporary, new string[] { session.Fullness.ToString("R", CultureInfo.InvariantCulture), session.Mood.ToString("R", CultureInfo.InvariantCulture), session.Meals.ToString(CultureInfo.InvariantCulture) });
                if (File.Exists(savePath)) File.Replace(temporary, savePath, null); else File.Move(temporary, savePath);
                saveError = "";
            }
            catch (IOException) { saveError = " · 저장 실패"; }
            catch (UnauthorizedAccessException) { saveError = " · 저장 실패"; }
        }
        // 타이머·아이콘 비트맵·메뉴·툴팁과 남아 있는 도토리 창을 모두 반환한다.
        // 다람쥐 게임을 종료하면 바닥의 도토리는 별도로 저장하지 않는다.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                foreach (ToolStripItem item in menu.Items) if (item.Image != null) item.Image.Dispose();
                menu.Dispose(); hint.Dispose();
                foreach (DroppedAcorn acorn in food) if (acorn.Marker != null) acorn.Marker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

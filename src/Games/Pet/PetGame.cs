// 다람쥐 UI: 입력, 작은 창과 메뉴, 도토리 표시, 공통 저장을 연결한다.
// 행동과 먹이 우선순위는 화면에 의존하지 않는 PetBehavior가 담당한다.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Launcher;

namespace TaskbarGames.Games.Pet
{
    public sealed class PetGame : Form
    {
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ToolStripDropDown menu = new ToolStripDropDown();
        readonly ContextMenuStrip animalMenu = new ContextMenuStrip();
        PetAnimal animal;
        readonly ToolStripButton status = new ToolStripButton();
        readonly ArcadeHints hint = new ArcadeHints();
        readonly GameStore store;
        readonly PetBehavior behavior;
        readonly Dictionary<Acorn, AcornMarker> markers = new Dictionary<Acorn, AcornMarker>();
        double lastTime, saveAge;
        bool placingFood, jumpHeld, focused, menuOpen;
        public PetGame() : this(true) { }
        internal PetGame(bool loadSettings)
        {
            store = loadSettings ? GameEnvironment.Store : new GameStore();
            animal = PetAppearance.Parse(store.Data.PetAnimal);
            behavior = new PetBehavior(new PetSession(store.Data.PetFullness, store.Data.PetMood, store.Data.PetMeals),
                new Random(), GameEnvironment.Display.WorkingArea.Left + 120);
            behavior.Ate += delegate(Acorn acorn) {
                AcornMarker marker;
                if (markers.TryGetValue(acorn, out marker)) { markers.Remove(acorn); marker.Dispose(); }
                SaveState(); UpdateStatus();
            };
            Text = "Taskbar_Games - " + PetAppearance.Name(animal); Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            MinimumSize = MaximumSize = new Size(76, 56); ClientSize = new Size(76, 56);
            BackColor = TransparencyKey = Color.Magenta; TopMost = true; DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual; PositionPet();
            ArcadeTheme.Style(menu); menu.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
            menu.AutoSize = false; menu.Size = new Size(154, 34);
            status.Image = PetMenuIcon("info"); menu.Items.Add(status);
            menu.Items.Add(new ToolStripButton(PetFood.Name(animal) + " 놓기", PetFood.Icon(animal), delegate { menu.Close(); BeginInvoke(new Action(PlaceFood)); }));
            menu.Items.Add(new ToolStripButton("쓰다듬기", PetMenuIcon("love"), delegate { menu.Close(); Stroke(); }));
            menu.Items.Add(new ToolStripButton("낮잠 자기 / 깨우기", PetMenuIcon("sleep"), delegate { menu.Close(); behavior.ToggleSleep(); Invalidate(); }));
            menu.Items.Add(new ToolStripButton("게임 선택으로 돌아가기 (Esc)", PetMenuIcon("back"), delegate { menu.Close(); Close(); }));
            menu.Items.Add(new ToolStripButton("동물 변경", PetMenuIcon("animal"), delegate { menu.Close(); BeginInvoke(new Action(ShowAnimalMenu)); }));
            foreach (ToolStripItem item in menu.Items)
            {
                item.DisplayStyle = ToolStripItemDisplayStyle.Image; item.ImageScaling = ToolStripItemImageScaling.None;
                item.AutoSize = false; item.Size = new Size(24, 24); item.Overflow = ToolStripItemOverflow.Never;
                item.Margin = Padding.Empty; item.Padding = Padding.Empty;
                item.ToolTipText = item.AccessibleName = item.Text;
            }
            hint.AttachMenu(this, menu);
            ArcadeTheme.Style(animalMenu); animalMenu.ImageScalingSize = new Size(36, 30);
            animalMenu.ShowImageMargin = true; animalMenu.ShowCheckMargin = true;
            foreach (PetAnimal candidate in Enum.GetValues(typeof(PetAnimal)))
            {
                PetAnimal choice = candidate;
                ToolStripMenuItem item = new ToolStripMenuItem(PetAppearance.Name(choice), PetAppearance.Icon(choice), delegate { SelectAnimal(choice); });
                item.Tag = choice; item.ToolTipText = PetAppearance.Name(choice) + "로 변경 · 포만감과 기분은 이어집니다";
                animalMenu.Items.Add(item);
            }
            hint.AttachMenu(this, animalMenu);
            animalMenu.Opening += delegate { menuOpen = true; };
            animalMenu.Closed += delegate { menuOpen = false; lastTime = clock.Elapsed.TotalSeconds; };
            menu.Opening += delegate { menuOpen = true; UpdateStatus(); };
            menu.Closed += delegate { menuOpen = false; lastTime = clock.Elapsed.TotalSeconds; };
            UpdatePetHint();
            UpdateStatus();
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) Stroke();
                else if (e.Button == MouseButtons.Right) menu.Show(this, e.Location);
            };
            Activated += delegate { focused = true; jumpHeld = KeyboardState.SpaceDown; };
            Deactivate += delegate { focused = false; jumpHeld = true; behavior.CancelJump(); PositionPet(); Invalidate(); };
            KeyDown += OnPetKeyDown;
            KeyUp += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) { jumpHeld = false; e.Handled = e.SuppressKeyPress = true; }
            };
            timer.Interval = 40; timer.Tick += Tick; timer.Start();
            FormClosing += delegate { SaveState(); };
        }
        void UpdatePetHint()
        { hint.SetToolTip(this, PetAppearance.Name(animal) + " · 클릭해서 쓰다듬기 · SPACE 점프 · 우클릭으로 돌봄과 동물 변경"); }
        void ShowAnimalMenu()
        {
            if (IsDisposed) return;
            foreach (ToolStripMenuItem item in animalMenu.Items) item.Checked = (PetAnimal)item.Tag == animal;
            animalMenu.Show(this, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }
        void SelectAnimal(PetAnimal choice)
        {
            animal = choice; store.Data.PetAnimal = choice.ToString().ToLowerInvariant(); SaveState();
            Image oldFoodIcon = menu.Items[1].Image;
            menu.Items[1].Image = PetFood.Icon(animal);
            if (oldFoodIcon != null) oldFoodIcon.Dispose();
            foreach (AcornMarker marker in markers.Values) marker.Animal = animal;
            UpdateStatus();
            Text = "Taskbar_Games - " + PetAppearance.Name(animal); UpdatePetHint(); Invalidate();
        }
        void UpdateStatus()
        {
            status.Text = String.Format("포만감 {0:0} · 기분 {1:0} · 식사 {2}회{3}", behavior.Stats.Fullness, behavior.Stats.Mood,
                behavior.Stats.Meals, store.Error.Length == 0 ? "" : " · " + store.Error);
            status.ToolTipText = status.AccessibleName = status.Text;
            menu.Items[1].Enabled = behavior.Food.Count < PetBehavior.MaxAcorns;
            menu.Items[1].Text = PetFood.Name(animal) + " 놓기";
            menu.Items[1].AccessibleName = menu.Items[1].Text;
            menu.Items[1].ToolTipText = behavior.Food.Count >= PetBehavior.MaxAcorns ? "먹이는 최대 5개까지 놓을 수 있어요" : menu.Items[1].Text + " (" + behavior.Food.Count + "/5)";
        }
        void OnPetKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true;
            if (!focused || jumpHeld) return;
            jumpHeld = true;
            if (placingFood || menuOpen || !behavior.Jump()) return;
            lastTime = clock.Elapsed.TotalSeconds; timer.Interval = 16; Invalidate();
        }
        static Bitmap PetMenuIcon(string kind)
        {
            Bitmap bitmap = new Bitmap(18, 18);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                if (kind == "food") AcornMarker.DrawAcorn(g, 3, 4);
                else if (kind == "love") DrawHeart(g, 2, 3, 2);
                else if (kind == "sleep") { PixelArt.Text(g, "Z", ArcadeTheme.Cyan, 2, 8); PixelArt.Text(g, "Z", ArcadeTheme.Cyan, 10, 2); }
                else if (kind == "animal")
                {
                    RoundPixel(g, ArcadeTheme.Cyan, 5, 9, 9, 7, 2);
                    Box(g, ArcadeTheme.Cyan, 2, 5, 3, 4); Box(g, ArcadeTheme.Cyan, 6, 2, 3, 4);
                    Box(g, ArcadeTheme.Cyan, 10, 2, 3, 4); Box(g, ArcadeTheme.Cyan, 14, 5, 3, 4);
                }
                else if (kind == "info")
                {
                    using (Pen pen = new Pen(ArcadeTheme.Neon)) g.DrawRectangle(pen, 2, 2, 13, 13);
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
        void Stroke() { if (behavior.Pet()) { UpdateStatus(); Invalidate(); } }
        void PlaceFood()
        {
            if (IsDisposed || placingFood || behavior.Food.Count >= PetBehavior.MaxAcorns) return;
            placingFood = true; timer.Stop();
            try
            {
                using (AcornPlacement picker = new AcornPlacement(animal))
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        Acorn acorn = AddFood(picker.SelectedX);
                        if (acorn == null) return;
                        AcornMarker marker = new AcornMarker(animal); markers.Add(acorn, marker);
                        PositionFood(); marker.Show(this);
                    }
            }
            finally { placingFood = false; lastTime = clock.Elapsed.TotalSeconds; if (!IsDisposed) timer.Start(); }
        }
        Acorn AddFood(double screenX)
        {
            Rectangle area = GameEnvironment.Display.WorkingArea;
            Acorn acorn = behavior.AddFood(screenX, area.Left, area.Right); UpdateStatus(); return acorn;
        }
        void PositionFood()
        {
            Rectangle area = GameEnvironment.Display.WorkingArea;
            foreach (KeyValuePair<Acorn, AcornMarker> item in markers)
                item.Value.Location = new Point((int)item.Key.X - 8, area.Bottom - 16);
        }
        void PositionPet()
        {
            Rectangle area = GameEnvironment.Display.WorkingArea;
            behavior.SetBounds(area.Left, area.Right);
            Location = new Point((int)behavior.X, area.Bottom - Height - (int)Math.Round(behavior.JumpHeight));
            PositionFood();
        }
        void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.25, Math.Max(0, now - lastTime)); lastTime = now;
            if (menuOpen) { PositionPet(); return; }
            behavior.Stats.Advance(dt); saveAge += dt;
            Rectangle area = GameEnvironment.Display.WorkingArea;
            behavior.Advance(dt, area.Left, area.Right); PositionPet();
            if (saveAge >= 60) { SaveState(); saveAge = 0; }
            timer.Interval = behavior.JumpAge >= 0 ? 16 : behavior.Action == PetAction.Sleep ? 250 : 40;
            Invalidate();
        }
        void SaveState()
        {
            store.Data.PetFullness = behavior.Stats.Fullness; store.Data.PetMood = behavior.Stats.Mood;
            store.Data.PetMeals = behavior.Stats.Meals; store.Save();
        }

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
            PetAppearance.Draw(e.Graphics, animal, 22, 27, behavior.Direction > 0, behavior.Pose, time, 1);
            if (behavior.Action == PetAction.Love)
            {
                int rise = (int)((4 - behavior.Remaining) * 2.5);
                DrawHeart(e.Graphics, 32, 19 - rise, 1);
                DrawHeart(e.Graphics, 49, 26 - rise, 1);
            }
            string label = behavior.Action == PetAction.Sleep ? "Z Z Z" : behavior.Action == PetAction.Eat ? "YUM"
                : behavior.Action == PetAction.Cola ? "COLA" : behavior.Action == PetAction.Popcorn ? "POP"
                : behavior.Action == PetAction.Read ? "READ" : behavior.Action == PetAction.WatchTV ? "TV"
                : behavior.Stats.Fullness < 25 && behavior.Action != PetAction.Love ? PetFood.Label(animal) : "";
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
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                foreach (ToolStripItem item in menu.Items) if (item.Image != null) item.Image.Dispose();
                menu.Dispose(); hint.Dispose();
                foreach (ToolStripItem item in animalMenu.Items) if (item.Image != null) item.Image.Dispose();
                animalMenu.Dispose();
                foreach (AcornMarker marker in markers.Values) marker.Dispose();
                markers.Clear();
            }
            base.Dispose(disposing);
        }
    }
}

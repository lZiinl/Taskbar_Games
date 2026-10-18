// 야구의 입력·선수 그림·공 궤적·결과 UI를 담당한다. 규칙은 BaseballSession이 소유한다.
// 선수와 필드는 0.65배 논리 좌표로 그리고, 점수/결과 글자는 실제 화면 픽셀로 그려 획을 유지한다.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Launcher;

namespace TaskbarGames.Games.Baseball
{
    public sealed class BaseballGame : Form
    {
        readonly BaseballSession session;
        readonly GameStore store;
        ProgressTracker progress;
        bool resultShown;
        readonly ToolStripDropDown teamMenu = new ToolStripDropDown();
        readonly ArcadeHints teamHints = new ArcadeHints();
        bool choosingTeam, editingBatter;
        TeamUniform batterUniform, pitcherUniform;
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Color ink = ArcadeTheme.Black;
        readonly Color mint = ArcadeTheme.Neon;
        readonly Color gold = Color.FromArgb(255, 233, 77);
        // 배트와 공이 만나는 논리 X좌표. 투구의 목표점과 타구 거리 매핑의 시작점이 같다.
        const int ContactX = 108;
        const float GameScale = 0.65f;
        float WorldWidth { get { return ClientSize.Width / GameScale; } }
        int ground, pitcherX;
        Rectangle closeButton;
        double lastTime;
        // 한 번 누른 키가 자동 반복으로 여러 스윙이 되지 않게 하는 입력 잠금이다.
        bool spaceHeld;
        // 투수 기본 자세의 문자 스프라이트. 배색은 PixelArt.Sprite가 해석한다.
        static readonly string[] Pitcher = {
            ".......DDDDD........", "......DBBBBBD.......", "....DDDBBLLBBD......",
            "....DBBBBBBBBD......", "......DSSSSTD.......", ".....DSSD SSTD......",
            "......DSSSSD........", ".......DTTDD........", ".....DDBBLLBDD......",
            "....DBBBLLLBBBD.....", "....DBBBWWLBBBD.....", "....DCBBWWLBBCD.....",
            ".....DCBBBBBCD......", ".....DCBBBBBCD......", ".....DDDDDDDDD......",
            ".....DWWWWWWWD......", "....DWWWWDWWWWD.....", "....DWWWD.DWWWD.....",
            "....DWWD...DWWD.....", "...DWWD.....DWWD....", "...DWWD......DWWD...",
            "..DWWWD......DWWD...", ".DDDDDD......DDDDD..", ".DDDDDD......DDDDDD."
        };
        // 준비 중 특정 시간 구간에 표시하는 다리/몸통 자세. 팔은 별도 관절 그리기로 덧그린다.
        static readonly string[] PitcherWindup = {
            ".......DDDDD........", "......DBBBBBD.......", "....DDDBBLLBBD......",
            "....DBBBBBBBBD......", "......DSSSSTD.......", ".....DSSD SSTD......",
            "......DSSSSD........", ".......DTTDD........", ".....DDBBLLBDD......",
            "....DBBBLLLBBBD.....", "....DBBBWWLBBBD.....", "....DCBBWWLBBCD.....",
            ".....DCBBBBBCD......", ".....DCBBBBBCD......", ".....DDDDDDDDD......",
            "...DDDWWWWWWWD......", "..DWWWWWWDWWWD......", "..DWWDDDDDWWWD......",
            "...DWWD..DWWWD......", "....DDD..DWWWD......", ".........DWWWD......",
            ".........DWWWD......", ".........DDDDDD.....", ".........DDDDDD....."
        };

        // 투명 전체 작업 영역 창을 만들고 활성화/비활성화에 모델의 Active를 연결한다.
        // 키를 누른 채 복귀한 경우 먼저 떼도록 하며 종료 버튼 판정은 화면 좌표를 논리 좌표로 바꿔 검사한다.
        public BaseballGame() : this(GameMode.Practice) { }
        public BaseballGame(GameMode mode)
        {
            store = GameEnvironment.Store;
            batterUniform = TeamUniform.Find(store.Data.BatterTeam, true);
            pitcherUniform = TeamUniform.Find(store.Data.PitcherTeam, false);
            session = new BaseballSession(new Random(), mode);
            progress = new ProgressTracker(store, GameKind.Baseball, mode);
            session.AttemptCompleted += SaveRecords;
            FormClosing += delegate { SaveRecords(); };
            Text = "Taskbar_Games - Baseball";
            Icon = ArcadeTheme.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            ResetLayout();
            Activated += delegate {
                spaceHeld = KeyboardState.SpaceDown;
                if (!choosingTeam) session.SetActive(true);
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimer();
                Invalidate();
            };
            Deactivate += delegate {
                spaceHeld = false;
                if (!choosingTeam) session.SetActive(false);
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimer();
                Invalidate();
            };
            KeyDown += OnGameKeyDown;
            KeyUp += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) { spaceHeld = false; e.Handled = e.SuppressKeyPress = true; }
            };
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Right)
                {
                    Point world = new Point((int)(e.X / GameScale), (int)(e.Y / GameScale));
                    if (new Rectangle(42, ground - 56, 66, 56).Contains(world)) OpenTeamMenu(true, e.Location);
                    else if (new Rectangle(pitcherX - 25, ground - 54, 50, 54).Contains(world)) OpenTeamMenu(false, e.Location);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                if (closeButton.Contains((int)(e.X / GameScale), (int)(e.Y / GameScale))) { Close(); return; }
                Activate();
                Focus();
            };
            ArcadeTheme.Style(teamMenu);
            teamMenu.AutoSize = false; teamMenu.Size = new Size(106, 122);
            teamMenu.LayoutStyle = ToolStripLayoutStyle.Table;
            TableLayoutSettings grid = (TableLayoutSettings)teamMenu.LayoutSettings;
            grid.ColumnCount = 3; grid.RowCount = 4; grid.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            for (int column = 0; column < 3; column++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            for (int row = 0; row < 4; row++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            foreach (TeamUniform team in TeamUniform.All)
            {
                TeamUniform choice = team;
                ToolStripButton item = new TeamLogoButton();
                item.Image = team.Icon(); item.Tag = team; item.AccessibleName = team.Name;
                item.ToolTipText = team.Name;
                item.Click += delegate { SelectTeam(choice); teamMenu.Close(); };
                teamMenu.Items.Add(item);
            }
            teamHints.AttachMenu(this, teamMenu);
            teamMenu.Closed += delegate {
                choosingTeam = false; lastTime = clock.Elapsed.TotalSeconds; spaceHeld = KeyboardState.SpaceDown;
                session.SetActive(Form.ActiveForm == this); UpdateTimer(); Invalidate();
            };
            timer.Interval = 250;
            timer.Tick += Tick;
            timer.Start();
        }

        void OpenTeamMenu(bool batter, Point location)
        {
            AdvanceToNow(); editingBatter = batter; choosingTeam = true;
            teamMenu.AccessibleName = batter ? "타자 구단 선택" : "투수 구단 선택";
            foreach (ToolStripButton item in teamMenu.Items)
                item.Checked = ((TeamUniform)item.Tag).Id == (batter ? batterUniform.Id : pitcherUniform.Id);
            teamMenu.Show(this, location);
        }
        void SelectTeam(TeamUniform team)
        {
            if (editingBatter) { batterUniform = team; store.Data.BatterTeam = team.Id; }
            else { pitcherUniform = team; store.Data.PitcherTeam = team.Id; }
            store.Save(); Invalidate();
        }
        sealed class TeamLogoButton : ToolStripButton
        {
            public TeamLogoButton()
            {
                AutoSize = false; Size = new Size(32, 28); Margin = Padding.Empty; Padding = Padding.Empty;
                DisplayStyle = ToolStripItemDisplayStyle.Image; ImageScaling = ToolStripItemImageScaling.None;
                Overflow = ToolStripItemOverflow.Never;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Checked || Selected)
                    using (Pen border = new Pen(Checked ? ArcadeTheme.Neon : Color.White))
                        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }
        void SaveRecords() { progress.Record(session.Stats); }
        void ShowSummary()
        {
            if (resultShown || choosingTeam || !session.Active || !session.IsComplete || session.Elapsed < BaseballSession.ResultSeconds) return;
            resultShown = true; timer.Stop();
            using (ChallengeResultForm result = new ChallengeResultForm(GameKind.Baseball, session.Stats, progress))
                if (result.ShowDialog(this) != DialogResult.Retry) { Close(); return; }
            session.Restart(); progress = new ProgressTracker(store, GameKind.Baseball, session.Mode);
            resultShown = false; lastTime = clock.Elapsed.TotalSeconds;
            Activate(); UpdateTimer(); timer.Start(); Invalidate();
        }

        // 작업 영역을 다시 읽고 논리 지면·투수 위치·초기 종료 버튼 위치를 설정한다.
        // 화면 변경 중 진행되던 투구는 초기화하지만 모델의 누적 기록은 보존한다.
        void ResetLayout()
        {
            Bounds = GameEnvironment.Display.WorkingArea;
            ground = (int)((ClientSize.Height - 2) / GameScale);
            pitcherX = Math.Min((int)WorldWidth - 44, Math.Max(390, Math.Min(580, (int)(WorldWidth * 0.36))));
            closeButton = new Rectangle(338, ground - 15, 15, 14);
            session.ResetRound();
            spaceHeld = false;
            lastTime = clock.Elapsed.TotalSeconds;
        }

        // Esc는 선택창 복귀, Space는 최초 눌림만 처리한다.
        // 입력 직전까지의 경과 시간을 먼저 반영해야 타이머 프레임 사이에 들어온 입력도 실제 시각에 가깝게 판정된다.
        void OnGameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true;
            if (spaceHeld || !session.Active || choosingTeam) return;
            spaceHeld = true;
            // Synchronize to the actual key press, independent of timer jitter.
            bool changed = AdvanceToNow();
            changed = session.Swing() || changed;
            if (changed) Invalidate();
            UpdateTimer();
        }

        // Stopwatch의 현재 값과 이전 값의 차이만 모델에 전달한다. 시스템 날짜/시각 보정에 영향을 받지 않는다.
        bool AdvanceToNow()
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - lastTime;
            lastTime = now;
            if (choosingTeam) return false;
            return session.Advance(elapsed);
        }

        // 작업 영역 변경과 모델 진행 결과를 합쳐 실제 변화가 있을 때만 화면을 무효화한다.
        void Tick(object sender, EventArgs e)
        {
            bool changed = false;
            if (Bounds != GameEnvironment.Display.WorkingArea) { ResetLayout(); changed = true; }
            changed = AdvanceToNow() || changed;
            UpdateTimer();
            if (changed) Invalidate();
            ShowSummary();
        }

        // 비활성 상태는 250ms, 움직이는 동안은 16ms로 갱신한다.
        // 정적인 결과 화면은 다음 전환 시점까지만 기다리되 작업 영역 확인을 위해 최대 250ms로 제한한다.
        void UpdateTimer()
        {
            timer.Interval = !session.Active || (session.IsComplete && session.Elapsed >= BaseballSession.ResultSeconds) ? 250 : session.Phase == BaseballPhase.Result && session.SwingAge < 0
                ? Math.Max(1, Math.Min(250, (int)Math.Ceiling((BaseballSession.ResultSeconds - session.Elapsed) * 1000))) : 16;
        }

        // Pitch에서는 투수→타격점 진행률과 작은 높이 변화를 사용한다.
        // Flight에서는 모델의 실제 펜스 반영 거리/높이를 화면 논리 좌표로 변환한다.
        PointF BallPosition(double seconds)
        {
            if (session.Phase == BaseballPhase.Pitch)
            {
                double ratio = seconds / session.PitchSeconds;
                return new PointF((float)(pitcherX - 14 + (ContactX - pitcherX + 14) * ratio),
                    (float)(ground - 30 - Math.Sin(Math.Min(1, ratio) * Math.PI) * 5));
            }
            double t = Math.Min(1, Math.Max(0, seconds / session.FlightSeconds));
            return new PointF(FieldX(session.FlightDistance(t)), (float)(ground - session.FlightHeight(t)));
        }

        // 0~160m를 타격점부터 화면 오른쪽 여백 28까지 선형 매핑한다. 창 폭이 달라도 점수 거리는 변하지 않는다.
        float FieldX(double meters) { return ContactX + (WorldWidth - ContactX - 28) * (float)meters / 160f; }

        // 마지막 결과의 실제 착지 거리로 라벨과 공의 최종 표시 위치를 구한다.
        float LandingX()
        {
            return FieldX(session.LastHit.Distance);
        }

        // 현재 Graphics 변환을 따르는 사각형 도우미다. 화면 픽셀로 쓰려면 호출 전에 변환을 초기화해야 한다.
        void Box(Graphics g, int x, int y, int width, int height, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height);
        }

        // 필드와 선수는 축소 변환으로, HUD와 결과 라벨은 별도 Save/ResetTransform/Restore 구간에서 그린다.
        // 점수판 너비는 문자열 길이에 맞춰 늘고 종료 버튼 클릭 영역도 실제 그려진 위치와 함께 갱신한다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            GraphicsState saved = g.Save();
            g.ScaleTransform(GameScale, GameScale);
            g.SmoothingMode = SmoothingMode.None;
            DrawFence(g);
            BatterAnimation.Draw(g, ground, session.SwingAge, batterUniform);
            bool winding = session.Phase == BaseballPhase.Ready && session.Elapsed > 0.4 && session.Elapsed < 0.72;
            pitcherUniform.DrawSprite(g, winding ? PitcherWindup : Pitcher, pitcherX - 20, ground - 48);
            Box(g, ContactX - 5, ground - 2, 11, 2, Color.WhiteSmoke);
            Box(g, pitcherX - 22, ground - 2, 44, 2, Color.FromArgb(171, 141, 106));
            DrawPitchingArm(g);

            string score = session.Phase == BaseballPhase.Result ? "SCORE " + session.Score + "  BEST " + session.BestDistance + "M"
                : !session.Active ? "CLICK TO PLAY" : session.Phase == BaseballPhase.Ready ? "READY..."
                : session.Phase == BaseballPhase.Pitch ? "SPACE: SWING" : "BASEBALL";
            // Keep text on device pixels even when the players and field are scaled down.
            GraphicsState hudState = g.Save();
            g.ResetTransform();
            int hudX = (int)Math.Round(130 * GameScale), hudBottom = (int)Math.Round(ground * GameScale);
            int hudWidth = Math.Max(156, PixelArt.Width(score) + 28);
            Box(g, hudX, hudBottom - 18, hudWidth, 18, ink);
            using (Pen border = new Pen(ArcadeTheme.Neon)) g.DrawRectangle(border, hudX, hudBottom - 18, hudWidth - 1, 17);
            PixelArt.Text(g, score, session.Active ? mint : ArcadeTheme.Cyan, hudX + 5, hudBottom - 12);
            Box(g, hudX + hudWidth - 19, hudBottom - 14, 1, 10, ArcadeTheme.Neon);
            PixelArt.Text(g, "X", ArcadeTheme.Pink, hudX + hudWidth - 12, hudBottom - 12);
            closeButton = new Rectangle((int)((hudX + hudWidth - 18) / GameScale), (int)((hudBottom - 18) / GameScale),
                (int)Math.Ceiling(18 / GameScale), (int)Math.Ceiling(18 / GameScale));
            // 남은 공 수는 점수판 오른쪽에 붙이고 구속은 투수 옆에 표시한다.
            string remaining = session.Mode == GameMode.Challenge ? Math.Max(0, 10 - session.Attempts) + " LEFT" : "PRACT";
            int countX = hudX + hudWidth + 5;
            Box(g, countX, hudBottom - 11, PixelArt.Width(remaining) + 4, 10, ink);
            PixelArt.Text(g, remaining, Color.Silver, countX + 2, hudBottom - 10);
            if (session.Phase == BaseballPhase.Pitch)
            {
                string speed = session.PitchKph.ToString("0") + " KM/H";
                int speedX = Math.Min(ClientSize.Width - PixelArt.Width(speed) - 6, (int)((pitcherX + 28) * GameScale));
                Box(g, speedX, hudBottom - 27, PixelArt.Width(speed) + 4, 10, ink);
                PixelArt.Text(g, speed, Color.Silver, speedX + 2, hudBottom - 26);
            }
            GameUi.DrawBadge(g, store, GameKind.Baseball, progress, hudX, hudBottom - 56);
            g.Restore(hudState);

            if (session.Phase == BaseballPhase.Pitch || session.Phase == BaseballPhase.Flight)
            {
                for (int i = 3; i > 0; i--)
                {
                    PointF trail = BallPosition(Math.Max(0, session.Elapsed - i * 0.025));
                    Box(g, (int)trail.X - 1, (int)trail.Y - 1, 2, 2, Color.FromArgb(150, 163, 183));
                }
                PointF ball = BallPosition(session.Elapsed);
                Box(g, (int)ball.X - 3, (int)ball.Y - 3, 7, 7, ink);
                Box(g, (int)ball.X - 2, (int)ball.Y - 2, 5, 5, Color.WhiteSmoke);
                Box(g, (int)ball.X, (int)ball.Y - 1, 1, 3, Color.FromArgb(232, 101, 111));
            }
            if (session.Phase == BaseballPhase.Result)
            {
                string label = session.LastHit.Hit ? (session.LastHit.HomeRun ? "HOME RUN" : session.LastHit.Grade) + " " + session.LastHit.Distance + "M" : session.LastHit.Grade;
                string points = session.LastHit.Hit ? "+" + session.LastHit.Points + " PTS" : "0 PTS";
                GraphicsState resultState = g.Save();
                g.ResetTransform();
                int width = Math.Max(PixelArt.Width(label), PixelArt.Width(points)) + 8;
                // 결과 배치는 화면 픽셀 기준으로 계산한다. 오른쪽 공간 부족 시 왼쪽으로 옮기고 HUD와 겹치면 위로 올린다.
                int landing = (int)Math.Round(LandingX() * GameScale);
                int x = session.LastHit.Hit ? landing + 6 : hudX;
                if (x + width > ClientSize.Width - 6) x = landing - width - 6;
                x = Math.Max(6, Math.Min(ClientSize.Width - width - 6, x));
                int y = hudBottom - 23;
                // Short hits can land over the HUD; keep their result immediately above it.
                if (x < hudX + hudWidth && x + width > hudX) y = hudBottom - 43;
                Box(g, x, y, width, 22, ink);
                PixelArt.Text(g, label, session.LastHit.Hit ? gold : Color.FromArgb(255, 135, 141), x + 4, y + 3);
                PixelArt.Text(g, points, mint, x + 4, y + 12);
                g.Restore(resultState);
                if (session.LastHit.Hit)
                    Box(g, (int)LandingX() - 2, ground - 5, 5, 5, gold);
            }
            g.Restore(saved);
        }

        // 130m 위치의 펜스와 거리/홈런 표시를 그린다.
        // 펜스는 축소하지만 글자는 원래 1픽셀 획을 유지하도록 화면 좌표로 따로 출력한다.
        void DrawFence(Graphics g)
        {
            int x = (int)FieldX(BaseballSession.FenceDistance);
            int fenceHeight = (int)Math.Round(BaseballSession.FenceHeight);
            int top = ground - fenceHeight;
            Color mesh = Color.FromArgb(104, 142, 155);
            for (int yy = top + 4; yy < ground; yy += 4) Box(g, x, yy, 18, 1, mesh);
            for (int xx = x + 4; xx < x + 18; xx += 4) Box(g, xx, top, 1, fenceHeight, mesh);
            Box(g, x, top, 2, fenceHeight, ink);
            Box(g, x + 16, top, 2, fenceHeight, ink);
            Box(g, x - 1, top - 2, 20, 3, mint);
            GraphicsState labelState = g.Save();
            g.ResetTransform();
            int labelX = (int)Math.Round((x + 9) * GameScale);
            int labelY = (int)Math.Round(top * GameScale);
            Box(g, labelX - 15, labelY - 14, 30, 11, ink);
            PixelArt.Text(g, "130M", Color.WhiteSmoke, labelX - 11, labelY - 12);
            if (session.Phase == BaseballPhase.Flight && session.HomeRunVisible)
            {
                Box(g, labelX - 28, labelY - 28, 56, 12, ink);
                PixelArt.Text(g, "HOME RUN", mint, labelX - 23, labelY - 25);
            }
            g.Restore(labelState);
        }

        // 준비 단계의 공 모으기·팔 접기·릴리스 시각에 맞춰 팔 관절과 공 위치를 보간한다.
        void DrawPitchingArm(Graphics g)
        {
            Point gatheredElbow = new Point(7, -24), gatheredHand = new Point(-6, -30);
            Point cockedElbow = new Point(12, -34), cockedHand = new Point(8, -44);
            Point releasedElbow = new Point(-4, -37), releasedHand = new Point(-14, -30);
            Point elbow = gatheredElbow, hand = gatheredHand;
            if (session.Phase == BaseballPhase.Ready)
            {
                double age = session.Elapsed;
                if (age < 0.67)
                {
                    elbow = Blend(gatheredElbow, cockedElbow, (age - 0.48) / 0.19);
                    hand = Blend(gatheredHand, cockedHand, (age - 0.48) / 0.19);
                }
                else
                {
                    elbow = Blend(cockedElbow, releasedElbow, (age - 0.67) / 0.18);
                    hand = Blend(cockedHand, releasedHand, (age - 0.67) / 0.18);
                }
            }
            else if (session.Phase == BaseballPhase.Pitch)
            {
                elbow = Blend(releasedElbow, gatheredElbow, session.Elapsed / 0.24);
                hand = Blend(releasedHand, gatheredHand, session.Elapsed / 0.24);
            }
            // Glove arm stays folded at the chest; the throwing arm has its own elbow.
            DrawLimb(g, new Point(pitcherX - 7, ground - 32), new Point(pitcherX - 12, ground - 25), new Point(pitcherX - 7, ground - 29));
            Point shoulder = new Point(pitcherX + 3, ground - 32);
            elbow.Offset(pitcherX, ground);
            hand.Offset(pitcherX, ground);
            DrawLimb(g, shoulder, elbow, hand);
            if (session.Phase == BaseballPhase.Ready && session.Elapsed > 0.50)
                Box(g, hand.X - 2, hand.Y - 2, 4, 4, Color.WhiteSmoke);
            Box(g, pitcherX - 12, ground - 33, 9, 8, ink);
            Box(g, pitcherX - 11, ground - 32, 7, 6, Color.FromArgb(171, 118, 68));
            Box(g, pitcherX - 9, ground - 31, 1, 4, Color.FromArgb(225, 176, 104));
        }

        static Point Blend(Point a, Point b, double amount)
        {
            double t = Math.Max(0, Math.Min(1, amount));
            t = t * t * (3 - 2 * t);
            return new Point((int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t));
        }

        void DrawLimb(Graphics g, Point shoulder, Point elbow, Point hand)
        {
            using (Pen outline = new Pen(ink, 6))
            using (Pen skin = new Pen(Color.FromArgb(246, 194, 144), 3))
            {
                outline.LineJoin = skin.LineJoin = LineJoin.Bevel;
                outline.StartCap = outline.EndCap = skin.StartCap = skin.EndCap = LineCap.Round;
                g.DrawLines(outline, new Point[] { shoulder, elbow, hand });
                g.DrawLines(skin, new Point[] { shoulder, elbow, hand });
            }
        }

        // 폼 종료 시 타이머를 해제하여 닫힌 게임의 업데이트가 남지 않게 한다.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose(); teamHints.Dispose();
                foreach (ToolStripItem item in teamMenu.Items) if (item.Image != null) item.Image.Dispose();
                teamMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

// 포트리스 UI: 입력과 렌더링을 FortressSession 및 공통 저장에 연결한다.
// 게임 논리 좌표는 화면 픽셀을 GameScale로 나눈 값이며, 픽셀 글꼴은 화면 정수 좌표로 따로 그린다.

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Launcher;

namespace TaskbarGames.Games.Fortress
{
    public sealed class FortressGame : Form
    {
        // Rendering, mouse input and physics share compact logical coordinates.
        const float GameScale = 0.60f;
        const float TankOriginX = 55; // Center of the tank tracks is position 0.
        Color tankBase = Color.FromArgb(93, 184, 106);
        Color tankLight = Color.FromArgb(184, 246, 153);
        Color tankShadow = Color.FromArgb(48, 105, 79);
        // 색상 선택 중 입력/진행을 막는 플래그. waitForSpaceRelease는 포커스 복귀 시 키 떼기를 요구한다.
        bool choosingColor;
        bool waitForSpaceRelease;
        float WorldWidth { get { return ClientSize.Width / GameScale; } }
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly FortressSession session;
        readonly GameStore store;
        ProgressTracker progress;
        bool resultShown;
        readonly Color ink = ArcadeTheme.Black;
        readonly Color green = ArcadeTheme.Neon;
        // 탱크 문자 스프라이트. DrawPixelSprite에서 색상 코드별로 기본색·명암·윤곽을 해석한다.
        static readonly string[] TankPixels = {
            "....................",
            "....................",
            "....................",
            ".................DDD",
            ".......DDDD...DDDLLD",
            "......DLLLLDDDLLDDD.",
            "......DGGGGGDDDD....",
            "...DDDDGGGGGDDDDD...",
            "..DLLLLLLLLLLLLLGD..",
            "..DGGGGGGGGGGGGGSD..",
            ".DDSSSSSSSSSSSSSSDD.",
            "DWWDWWDWWDWWDWWDWWDD",
            ".DDDDDDDDDDDDDDDDDD."
        };
        // 과녁 스프라이트. 명중 결과 표시 동안 숨기고 파편으로 대체한다.
        static readonly string[] TargetPixels = {
            "....DDDD....",
            "..DDRRRRDD..",
            ".DRRCCCCRRD.",
            ".DRCCRRCCRD.",
            "DRCCRBBRCCRD",
            "DRCRBBBBRCRD",
            "DRCRBBBBRCRD",
            "DRCCRBBRCCRD",
            ".DRCCRRCCRD.",
            ".DRRCCCCRRD.",
            "..DDRRRRDD..",
            "DDDDDDDDDDDD"
        };
        // HUD 논리 영역과 포구/포탄/과녁 좌표. 지면 역시 논리 Y값이다.
        Rectangle panel;
        PointF muzzle;
        float ground;
        // 게임 상태는 session이 소유하고 창은 갱신 시각과 장식 효과만 관리한다.
        double lastTime;
        bool focused;
        // 명중 파편 하나의 위치·속도·크기·수명·색상. 게임 판정과 무관한 장식 효과다.
        sealed class Debris
        {
            public float X, Y, VX, VY, Size;
            public double Age, Lifetime;
            public Color Color;
        }
        readonly List<Debris> debris = new List<Debris>();
        // Cosmetic randomness must not change the sequence of target positions.
        readonly Random effectRandom = new Random();

        // 실행 시에는 사용자 색상 설정을 읽는 생성자로 연결한다.
        public FortressGame()
            : this(true)
        {
        }

        // Tests can exercise a real form without reading or migrating user settings.
        // 테스트에서는 false로 레지스트리/레거시 파일 접근을 피한다.
        // 투명 창·입력·포커스 전환·타이머를 구성하며 포커스를 잃으면 충전 상태를 버린다.
        internal FortressGame(bool loadSettings) : this(loadSettings ? GameEnvironment.Store : new GameStore()) { }
        public FortressGame(GameStore storage) : this(storage, GameMode.Practice) { }
        public FortressGame(GameMode mode) : this(GameEnvironment.Store, mode) { }
        public FortressGame(GameStore storage, GameMode mode)
        {
            Text = "Taskbar_Games - Fortress";
            Icon = ArcadeTheme.AppIcon;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            store = storage;
            session = new FortressSession(new Random(), mode);
            progress = new ProgressTracker(store, GameKind.Fortress, mode);
            session.ShotFinished += OnShotFinished;
            SetTankColor(Color.FromArgb((store.Data.TankColor >> 16) & 255, (store.Data.TankColor >> 8) & 255, store.Data.TankColor & 255));
            FormClosing += delegate { SaveRecords(); };
            ResetLayout();
            Activated += delegate {
                focused = true;
                waitForSpaceRelease = KeyboardState.SpaceDown;
                Invalidate();
            };
            Deactivate += delegate {
                focused = false;
                session.CancelCharge();
                waitForSpaceRelease = true;
                UpdateTimerInterval(clock.Elapsed.TotalSeconds);
                Invalidate();
            };
            MouseDown += OnMouse;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            timer.Interval = 250;
            timer.Tick += Tick;
            timer.Start();
        }

        void ResetLayout()
        {
            Bounds = GameEnvironment.Display.WorkingArea;
            ground = (ClientSize.Height - 2) / GameScale;
            muzzle = new PointF(87, ground - 29);
            panel = new Rectangle(126, (int)ground - 24, 200, 24);
            UpdatePanelLayout();
            debris.Clear();
            session.ResetLayout(WorldWidth, ground, muzzle, MinimumTargetX());
        }

        // 표시할 숫자 자릿수에 맞춰 점수판 너비를 산정한다. 글자 폭과 닫기 영역 여백을 합친다.
        static int ScorePanelWidth(long pointScore, int shotCount)
        {
            return (int)Math.Ceiling(TextWidth(String.Format("{0} PTS / {1} SHOTS", pointScore, shotCount), 1)) + 36;
        }

        // 현재 명중/발사 횟수로 점수판 폭을 갱신한다. 과녁 배치 보정은 별도 함수에서 처리한다.
        void UpdatePanelLayout()
        {
            panel.Width = ScorePanelWidth(session.Score, session.Shots);
        }

        // 다음 발사·명중으로 숫자 자릿수가 늘어날 때의 HUD 폭까지 미리 확보한다.
        // 조준 후 점수판 확장 때문에 과녁이 갑자기 움직이거나 가려지지 않게 한다.
        float MinimumTargetX()
        {
            // Reserve the next shot's digits before aiming. The sprite is 24 device
            // pixels wide; leave another 8 pixels between it and the score panel.
            int nextPanelRight = panel.X + ScorePanelWidth(session.Score > Int64.MaxValue - 2000 ? Int64.MaxValue : session.Score + 2000,
                session.Shots == Int32.MaxValue ? session.Shots : session.Shots + 1);
            return Math.Max(panel.Right, nextPanelRight) + (12 + 8) / GameScale;
        }

        // 움직이는 충전/포탄/파편은 16ms, 정적인 상태는 최대 250ms로 간격을 늘린다.
        // 결과 문구의 만료가 더 가까우면 그 시각에 맞춰 깨어나게 한다.
        void UpdateTimerInterval(double now)
        {
            bool animating = (session.Charging && session.Charge < 1) || session.Flying || debris.Count > 0;
            // A slow idle poll still catches taskbar/work-area changes. Static
            // session.Result labels only need a wakeup when their display time expires.
            timer.Interval = animating ? 16 : session.IsComplete ? 250 : session.Result.Length > 0
                ? Math.Max(1, Math.Min(250, (int)Math.Ceiling(session.ResultRemaining * 1000)))
                : 250;
        }

        // 화면 좌표를 논리 좌표로 바꿔 탱크 우클릭 영역과 종료 영역을 검사한다.
        // 일반 왼쪽 클릭은 조작 포커스만 주며 직접 발사하지 않는다.
        void OnMouse(object sender, MouseEventArgs e)
        {
            PointF worldMouse = new PointF(e.X / GameScale, e.Y / GameScale);
            if (e.Button == MouseButtons.Right &&
                new RectangleF(TankOriginX - 34, ground - 44, 68, 44).Contains(worldMouse))
            {
                ChooseTankColor();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (new RectangleF(panel.Right - 24, panel.Top, 24, panel.Height).Contains(worldMouse)) { Close(); return; }
            Activate();
            Focus();
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (choosingColor) return;
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true;
            if (waitForSpaceRelease || !focused) return;
            if (session.BeginCharge(MinimumTargetX()))
            {
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimerInterval(lastTime); Invalidate();
            }
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true; waitForSpaceRelease = false;
            if (!session.ReleaseCharge()) return;
            SaveRecords(); UpdatePanelLayout(); lastTime = clock.Elapsed.TotalSeconds;
            UpdateTimerInterval(lastTime); Invalidate();
        }

        void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = Math.Max(0, now - lastTime); lastTime = now;
            bool changed = false;
            if (Bounds != GameEnvironment.Display.WorkingArea) { ResetLayout(); changed = true; }
            changed = session.Advance(elapsed, MinimumTargetX()) || changed;
            if (debris.Count > 0) { UpdateDebris(Math.Min(0.05, elapsed)); changed = true; }
            UpdateTimerInterval(now);
            if (changed) Invalidate();
            ShowSummary();
        }

        void OnShotFinished(bool hit)
        {
            UpdatePanelLayout();
            if (hit) BurstTarget();
            SaveRecords();
        }
        void SaveRecords() { progress.Record(session.Stats); }
        void ShowSummary()
        {
            if (resultShown || !focused || !session.IsComplete || session.ResultRemaining > 0) return;
            resultShown = true; timer.Stop();
            using (ChallengeResultForm result = new ChallengeResultForm(GameKind.Fortress, session.Stats, progress))
            {
                if (result.ShowDialog(this) != DialogResult.Retry) { Close(); return; }
            }
            session.Restart(); progress = new ProgressTracker(store, GameKind.Fortress, session.Mode);
            resultShown = false; ResetLayout(); lastTime = clock.Elapsed.TotalSeconds;
            Activate(); timer.Start(); Invalidate();
        }

        // 과녁 주변에 28개 파편을 만든다. 별도 effectRandom을 써 장식 난수가 다음 과녁 위치를 바꾸지 않게 한다.
        void BurstTarget()
        {
            debris.Clear();
            Color[] colors = { Color.FromArgb(238, 91, 103), Color.FromArgb(255, 236, 192),
                Color.FromArgb(166, 43, 66), Color.FromArgb(255, 218, 123) };
            for (int i = 0; i < 28; i++)
                debris.Add(new Debris {
                    X = session.TargetX + (float)(effectRandom.NextDouble() * 30 - 15),
                    Y = ground - 19 + (float)(effectRandom.NextDouble() * 24 - 12),
                    VX = (float)(effectRandom.NextDouble() * 200 - 100),
                    VY = -(float)(80 + effectRandom.NextDouble() * 160),
                    Size = (i % 3 == 0 ? 3 : 2) / GameScale,
                    Lifetime = 0.45 + effectRandom.NextDouble() * 0.4,
                    Color = colors[i % colors.Length]
                });
        }

        // 파편에 중력을 적용하고 수명 종료 또는 지면 도착 시 제거한다.
        // 뒤에서부터 순회해 삭제로 인한 인덱스 이동이 다음 항목을 건너뛰지 않게 한다.
        void UpdateDebris(double dt)
        {
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                Debris p = debris[i];
                p.Age += dt;
                p.X += p.VX * (float)dt;
                p.Y += p.VY * (float)dt;
                p.VY += 420 * (float)dt;
                if (p.Age >= p.Lifetime || p.Y >= ground) debris.RemoveAt(i);
            }
        }

        // 점수판·충전 게이지·입력 안내·탱크·과녁·파편·포탄·결과 순으로 표시한다.
        // 포탄 잔상은 과거 탄도 시각을 다시 계산하여 현재 포탄과 같은 궤적을 따른다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.ScaleTransform(GameScale, GameScale);
            g.SmoothingMode = SmoothingMode.None;
            string score = String.Format("{0} PTS / {1}{2}", session.Score, session.Shots, session.Mode == GameMode.Challenge ? "/10" : " SHOTS");
            PixelRect(g, ink, panel.X, panel.Y, panel.Width, panel.Height);
            DrawPixelText(g, score, 1, focused ? green : Color.LightGray, panel.X + 6, panel.Y + 6);
            PixelRect(g, Color.FromArgb(53, 67, 86), panel.Right - 24, panel.Y + 3, 2, panel.Height - 6);
            DrawPixelText(g, "X", 1, Color.LightGray, panel.Right - 16, panel.Y + 6);

            // A narrow power column sits beside the tank, filling from the ground up.
            Rectangle meter = new Rectangle(100, (int)ground - 44, 13, 42);
            PixelRect(g, ink, meter.X - 3, meter.Y - 3, meter.Width + 6, meter.Height + 6);
            int lit = (int)Math.Ceiling(session.Charge * 8);
            for (int i = 0; i < 8; i++)
            {
                Color segment = i < lit ? (i >= 6 ? Color.FromArgb(255, 195, 98) : green) : Color.FromArgb(53, 67, 86);
                PixelRect(g, segment, meter.X, meter.Bottom - (i + 1) * 5, meter.Width, 3);
            }

            string hint = !focused ? "CLICK TANK" : session.Charging ? ((int)(session.Charge * 100)) + "%" : session.Flying || session.Result.Length > 0 ? "" : "HOLD SPACE";
            if (hint.Length > 0)
            {
                PixelRect(g, ink, panel.X, ground - 47, TextWidth(hint, 1) + 12, 21);
                DrawPixelText(g, hint, 1, Color.FromArgb(207, 217, 232), panel.X + 6, ground - 42);
            }

            DrawPixelText(g, session.Mode == GameMode.Challenge ? "CHALLENGE" : "PRACTICE", 1, ArcadeTheme.Cyan, panel.X, ground - 70);
            if (!session.Flying && session.Result.Length == 0)
                DrawPixelText(g, session.TargetPoints + " PTS", 1, ArcadeTheme.Cyan, session.TargetX - 25, ground - 65);
            DrawPixelSprite(g, TankPixels, TankOriginX, ground);
            if (!(session.WasHit && session.Result.Length > 0)) DrawPixelSprite(g, TargetPixels, session.TargetX, ground);
            foreach (Debris p in debris)
            {
                float size = p.Age > p.Lifetime * 0.65 ? 1 / GameScale : p.Size;
                PixelRect(g, p.Color, p.X - size / 2, p.Y - size / 2, size, size);
            }
            if (session.Flying)
            {
                for (int i = 5; i >= 0; i--)
                {
                    double t = Math.Max(0, session.FlightTime - i * 0.025);
                    PointF p = Ballistics.Position(muzzle, session.Speed, t, session.Gravity);
                    float radius = i == 0 ? 5 : 2;
                    PixelRect(g, i == 0 ? ink : Color.FromArgb(154, 171, 190), p.X - radius, p.Y - radius, radius * 2, radius * 2);
                    if (i == 0) PixelRect(g, Color.FromArgb(255, 218, 123), p.X - 3, p.Y - 3, 6, 6);
                }
            }
            if (session.Result.Length > 0)
            {
                string resultText = session.WasHit ? "HIT +" + session.LastPoints : "MISS";
                SizeF size = new SizeF(TextWidth(resultText, 1), 7 / GameScale);
                string comboText = session.WasHit && session.Combo >= 2 ? session.Combo + " COMBO" : "";
                float labelWidth = Math.Max(size.Width, TextWidth(comboText, 1));
                float x = session.TargetX + 31;
                if (x + labelWidth + 6 > WorldWidth)
                    x = session.TargetX - 31 - labelWidth;
                float y = ground - 19 - size.Height / 2;
                PixelRect(g, ink, x - 6, y - 4, size.Width + 12, size.Height + 8);
                DrawPixelText(g, resultText, 1, session.WasHit ? green : Color.FromArgb(255, 135, 141), x, y);
                if (comboText.Length > 0)
                {
                    PixelRect(g, ink, x - 6, y - 25, labelWidth + 12, 19);
                    DrawPixelText(g, comboText, 1, Color.FromArgb(255, 218, 123), x, y - 21);
                }
            }
            g.ResetTransform();
            GameUi.DrawBadge(g, store, GameKind.Fortress, progress, (int)(panel.Right * GameScale) + 6, ClientSize.Height - 17);
        }

        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string> {
            {'0', "0E11131519110E"}, {'1', "040C040404040E"},
            {'2', "0E11010204081F"}, {'3', "1E01010E01011E"},
            {'4', "02060A121F0202"}, {'5', "1F10101E01011E"},
            {'6', "0E10101E11110E"}, {'7', "1F010204080808"},
            {'8', "0E11110E11110E"}, {'9', "0E11110F01010E"},
            {'A', "0E11111F111111"}, {'B', "1E11111E11111E"}, {'C', "0E11101010110E"},
            {'D', "1E11111111111E"}, {'E', "1F10101E10101F"}, {'G', "0E11101711110E"},
            {'H', "1111111F111111"}, {'I', "0E04040404040E"},
            {'K', "11121418141211"}, {'L', "1010101010101F"},
            {'M', "111B1515111111"}, {'N', "11191915131311"},
            {'O', "0E11111111110E"}, {'P', "1E11111E101010"},
            {'S', "0F10100E01011E"}, {'T', "1F040404040404"}, {'R', "1E11111E141211"}, {'+', "0004041F040400"},
            {'X', "11110A040A1111"}, {'/', "01010204081010"},
            {'%', "191A0204080B13"}
        };

        // 실제 글자 픽셀 폭을 논리 좌표로 되돌려 HUD/결과 배치 계산과 맞춘다.
        static float TextWidth(string text, int pixel)
        {
            return Math.Max(0, text.Length * 6 - 1) * pixel / GameScale;
        }

        // 축소 변환을 잠시 해제하고 각 경계를 실제 정수 픽셀로 반올림한다.
        // 작은 도형의 흐림과 투명색 가장자리 번짐을 피하고 최소 1픽셀은 보장한다.
        static void PixelRect(Graphics g, Color color, float x, float y, float width, float height)
        {
            GraphicsState state = g.Save();
            g.ResetTransform();
            using (SolidBrush brush = new SolidBrush(color))
                g.FillRectangle(brush, (int)Math.Round(x * GameScale), (int)Math.Round(y * GameScale),
                    Math.Max(1, (int)Math.Round(width * GameScale)), Math.Max(1, (int)Math.Round(height * GameScale)));
            g.Restore(state);
        }

        // 글자의 시작점만 논리→화면 좌표로 변환하고 획은 정수 픽셀 크기로 찍는다.
        // HUD·HIT/MISS는 pixel=1을 써 다른 게임 안내와 같은 글자 높이를 유지한다.
        static void DrawPixelText(Graphics g, string text, int pixel, Color color, float x, float y)
        {
            GraphicsState state = g.Save();
            g.ResetTransform();
            int left = (int)Math.Round(x * GameScale), top = (int)Math.Round(y * GameScale);
            using (SolidBrush brush = new SolidBrush(color))
                foreach (char letter in text)
                {
                    string glyph;
                    if (Glyphs.TryGetValue(letter, out glyph))
                        for (int row = 0; row < 7; row++)
                        {
                            int bits = Convert.ToInt32(glyph.Substring(row * 2, 2), 16);
                            for (int col = 0; col < 5; col++)
                                if ((bits & (1 << (4 - col))) != 0)
                                    g.FillRectangle(brush, left + col * pixel, top + row * pixel, pixel, pixel);
                        }
                    left += 6 * pixel;
                }
            g.Restore(state);
        }

        // 중앙 X와 발바닥 Y를 기준으로 그림을 배치한다. 실제 도트는 2×2 화면 픽셀이다.
        // 색상 변경은 탱크 기본색/명암에만 반영하고 과녁의 색 코드는 고정한다.
        void DrawPixelSprite(Graphics g, string[] rows, float centerX, float bottom)
        {
            // Draw on whole device pixels to avoid blurred edges and transparency-key halos.
            GraphicsState state = g.Save();
            g.ResetTransform();
            g.SmoothingMode = SmoothingMode.None;
            const int pixel = 2;
            int left = (int)Math.Round(centerX * GameScale) - rows[0].Length * pixel / 2;
            int top = (int)Math.Round(bottom * GameScale) - rows.Length * pixel;
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    Color color;
                    switch (rows[y][x])
                    {
                        case 'D': color = Color.FromArgb(25, 32, 45); break;
                        case 'L': color = tankLight; break;
                        case 'G': color = tankBase; break;
                        case 'S': color = tankShadow; break;
                        case 'W': color = Color.FromArgb(137, 156, 171); break;
                        case 'R': color = Color.FromArgb(238, 91, 103); break;
                        case 'C': color = Color.FromArgb(255, 236, 192); break;
                        case 'B': color = Color.FromArgb(166, 43, 66); break;
                        default: continue;
                    }
                    using (SolidBrush brush = new SolidBrush(color))
                        g.FillRectangle(brush, left + x * pixel, top + y * pixel, pixel, pixel);
                }
            g.Restore(state);
        }

        // 폼의 투명색인 순수 마젠타가 선택되어 탱크가 사라지지 않도록 G를 1로 보정한다.
        // 알파값도 255로 정규화한다.
        static Color OpaqueTankColor(Color color)
        {
            // Pure magenta is the window's transparency key, so keep it visible.
            return color.R == 255 && color.G == 0 && color.B == 255
                ? Color.FromArgb(255, 1, 255) : Color.FromArgb(color.R, color.G, color.B);
        }

        // 선택한 기본색에서 밝은 색과 어두운 색을 파생해 입체감을 유지한다.
        void SetTankColor(Color color)
        {
            tankBase = OpaqueTankColor(color);
            tankLight = OpaqueTankColor(Color.FromArgb(
                tankBase.R + (255 - tankBase.R) * 2 / 5,
                tankBase.G + (255 - tankBase.G) * 2 / 5,
                tankBase.B + (255 - tankBase.B) * 2 / 5));
            tankShadow = Color.FromArgb(tankBase.R * 3 / 5, tankBase.G * 3 / 5, tankBase.B * 3 / 5);
        }



        void SaveTankColor()
        {
            store.Data.TankColor = tankBase.ToArgb() & 0xFFFFFF;
            if (!store.Save()) ShowColorSaveError();
        }

        // 충전을 취소하고 타이머를 멈춘 뒤 12색 모달 팔레트를 연다.
        // finally에서 기준 시각을 재설정해 대화상자에서 보낸 시간을 모델에 전달하지 않는다.
        void ChooseTankColor()
        {
            if (choosingColor) return;
            choosingColor = true;
            session.CancelCharge();
            timer.Stop();
            try
            {
                using (TankPalette picker = new TankPalette(tankBase))
                {
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        SetTankColor(picker.SelectedColor);
                        SaveTankColor();
                    }
                }
            }
            finally
            {
                double now = clock.Elapsed.TotalSeconds;
                lastTime = now;
                choosingColor = false;
                UpdateTimerInterval(now);
                timer.Start();
                Activate();
                Invalidate();
            }
        }

        // 색상은 이미 화면에 적용되었지만 다음 실행까지 유지되지 않을 수 있음을 구분해 알린다.
        void ShowColorSaveError()
        {
            MessageBox.Show(this, "색상은 적용했지만 Windows 사용자 설정에 저장하지 못했습니다. 다음 실행에는 유지되지 않을 수 있습니다.",
                "Fortress", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // 폼이 닫힌 뒤에도 틱이 남지 않도록 타이머를 반환한다.
        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

    }
}

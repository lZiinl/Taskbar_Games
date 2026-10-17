// 공룡 러너의 WinForms 입력·위치·흑백 렌더링 계층.
// 선택한 모니터 작업 영역의 왼쪽 절반을 쓰며 실제 규칙은 RunnerSession에 위임한다.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Runner
{
    public sealed class RunnerGame : Form
    {
        // 모델과 UI 타이머는 창 수명에 종속된다. Stopwatch는 시스템 시각 변경과 무관한 경과 시간을 제공한다.
        readonly RunnerSession session;
        readonly GameStore store;
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;
        // 운영체제 키 자동 반복을 한 번의 누름으로 취급하기 위한 잠금이다.
        // 다른 앱에서 스페이스를 누른 채 돌아온 경우도 키를 뗄 때까지 잠근다.
        bool held;
        // 문자 기반 공룡 원본. B는 몸체, D는 눈, 점은 투명 영역이다.
        // 선택창에서도 같은 원본을 사용하지만 게임 화면은 자체 흑백 색상으로 그린다.
        internal static readonly string[] Dino = {
            "..........BBBBBBBB..", ".........BBBBBBBBBB.", ".........BBDBBBBBBB.",
            ".........BBBBBBBBBB.", ".........BBBB.......", ".........BBBBBBBB...",
            "B.......BBBB........", "BB.....BBBBB........", "BBB...BBBBBBBB......",
            "BBBB.BBBBBBB.B......", ".BBBBBBBBBBB........", "..BBBBBBBBBB........",
            "...BBBBBBBB.........", "....BBBBBB..........", ".....BBBBB..........",
            ".....BB.BB..........", ".....B...B..........", ".....BB..BB........."
        };
        // 투명 배경 창과 입력 이벤트를 구성한다. 전체 화면을 덮는 배경색은 그리지 않는다.
        // Space는 시작/점프/재시작이며, 짧게 떼면 모델에 점프 높이 조정을 요청한다.
        public RunnerGame()
        {
            store = GameEnvironment.Store;
            session = new RunnerSession(new Random(), store.Data.RunnerBest);
            FormClosing += delegate { SaveRecords(); };
            Text = "Taskbar_Games - Dino Runner"; Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            BackColor = TransparencyKey = Color.Magenta; TopMost = true;
            DoubleBuffered = true; KeyPreview = true; StartPosition = FormStartPosition.Manual;
            PositionGame();
            // 복귀 시 last를 현재 시각으로 맞춰 비활성 상태에서 지난 시간이 한꺼번에 반영되지 않게 한다.
            Activated += delegate { session.Active = true; held = KeyboardState.SpaceDown; last = clock.Elapsed.TotalSeconds; Invalidate(); };
            Deactivate += delegate { session.Active = false; held = true; session.ReleaseJump(); Invalidate(); };
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (e.X >= Width - 20 && e.Y >= Height - 18) { Close(); return; }
                Activate(); Focus();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { Close(); return; }
                if (e.KeyCode != Keys.Space) return;
                e.Handled = e.SuppressKeyPress = true;
                if (!held) { held = true; session.Jump(); last = clock.Elapsed.TotalSeconds; timer.Interval = 16; Invalidate(); }
            };
            KeyUp += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) { held = false; session.ReleaseJump(); e.Handled = e.SuppressKeyPress = true; }
            };
            timer.Interval = 250;
            // 작업 영역 변경은 코스 초기화로 처리한다. 이동 중만 16ms, 정적인 대기/비활성은 250ms로 확인한다.
            // 멈춘 화면은 매 틱 다시 그리지 않고 입력·상태 변화 시에만 무효화한다.
            timer.Tick += delegate {
                Rectangle area = GameEnvironment.Display.WorkingArea;
                if (Left != area.Left || Bottom != area.Bottom || Width != area.Width / 2) { PositionGame(); session.ResetForLayout(); SaveRecords(); Invalidate(); }
                double now = clock.Elapsed.TotalSeconds;
                bool moving = session.Active && session.Phase == RunnerPhase.Running;
                session.Advance(now - last, Width); last = now;
                if (moving && session.Phase == RunnerPhase.GameOver) SaveRecords();
                timer.Interval = moving ? 16 : 250;
                if (moving) Invalidate();
            };
            timer.Start();
        }
        // 충돌·종료·화면 변경 시 현재 달리는 중의 기록도 포함한다.
        void SaveRecords()
        {
            store.Data.RunnerBest = Math.Max(store.Data.RunnerBest, Math.Max(session.Best, session.Score));
            store.Save();
        }
        void PositionGame()
        {
            Rectangle area = GameEnvironment.Display.WorkingArea;
            MinimumSize = MaximumSize = new Size(area.Width / 2, 100);
            Bounds = new Rectangle(area.Left, area.Bottom - 100, area.Width / 2, 100);
        }
        // 바닥선 → 공룡 → 장애물 → 안내·점수 순서로 그린다.
        // 모델의 공중 높이를 화면의 아래쪽 기준으로 변환하고 지상 달리기에는 발 프레임을 바꾼다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; int ground = Height - 2;
            using (Pen p = new Pen(Color.Gray)) g.DrawLine(p, 0, ground, Width, ground);
            int bob = session.Height <= 0 && session.Phase == RunnerPhase.Running ? (int)(session.Distance / 12) % 2 : 0;
            // A 26-pixel dinosaur with a readable silhouette at taskbar scale.
            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.TranslateTransform(40, ground - (int)session.Height - 27 + bob);
            g.ScaleTransform(1.5f, 1.5f);
            // 공유 원본을 수정하지 않도록 달리기 프레임을 바꿀 때만 얕은 복사한 배열을 사용한다.
            string[] pose = Dino;
            if (session.Height <= 0 && session.Phase == RunnerPhase.Running)
            {
                pose = (string[])Dino.Clone();
                pose[16] = bob == 0 ? ".....BB..B.........." : ".....B..BB..........";
                pose[17] = bob == 0 ? ".........BB........." : ".....BB.............";
            }
            for (int row = 0; row < pose.Length; row++)
                for (int col = 0; col < pose[row].Length; col++)
                    if (pose[row][col] != '.') Fill(g, pose[row][col] == 'D' ? Color.White : Color.DimGray, col, row, 1, 1);
            g.Restore(state);
            foreach (RunnerObstacle cactus in session.Obstacles)
            {
                int x = (int)cactus.X, y = ground - cactus.Height;
                Fill(g, Color.Black, x, y, cactus.Width, cactus.Height);
                Fill(g, Color.Silver, x + cactus.Width / 2 - 2, y, 4, cactus.Height);
                Fill(g, Color.Silver, x + 1, y + 5, 3, 8);
                Fill(g, Color.Silver, x + 2, y + 11, cactus.Width - 3, 3);
                Fill(g, Color.Silver, x + cactus.Width - 4, y + 3, 3, 10);
            }
            string hint = !session.Active ? "CLICK TO PLAY" : session.Phase == RunnerPhase.Ready ? "SPACE: JUMP" : session.Phase == RunnerPhase.GameOver ? "GAME OVER / SPACE: RETRY" : "";
            if (hint.Length > 0) Label(g, hint, 82, ground - 40, session.Phase == RunnerPhase.GameOver ? Color.White : Color.Silver);
            Label(g, "HI " + session.Best.ToString("D5") + "  " + session.Score.ToString("D5"), Math.Max(82, Width - 160), ground - 17, Color.Silver);
            Label(g, "X", Width - 17, ground - 17, Color.White);
        }
        // 단색 사각형 도우미. 호출마다 만든 Brush를 즉시 해제한다.
        static void Fill(Graphics g, Color color, int x, int y, int width, int height)
        { using (SolidBrush b = new SolidBrush(color)) g.FillRectangle(b, x, y, width, height); }
        // 7픽셀 높이의 공통 글꼴에 사방 4픽셀 여백을 둔 검정 라벨을 그린다.
        static void Label(Graphics g, string text, int x, int y, Color color)
        { Fill(g, Color.Black, x - 4, y - 4, PixelArt.Width(text) + 8, 15); PixelArt.Text(g, text, color, x, y); }
        // 닫힌 창에 타이머 이벤트가 계속 전달되지 않도록 UI 타이머를 반환한다.
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Runner
{
    public sealed class RunnerGame : Form
    {
        readonly RunnerSession session = new RunnerSession();
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;
        bool held;
        internal static readonly string[] Dino = {
            "..........BBBBBBBB..", ".........BBBBBBBBBB.", ".........BBDBBBBBBB.",
            ".........BBBBBBBBBB.", ".........BBBB.......", ".........BBBBBBBB...",
            "B.......BBBB........", "BB.....BBBBB........", "BBB...BBBBBBBB......",
            "BBBB.BBBBBBB.B......", ".BBBBBBBBBBB........", "..BBBBBBBBBB........",
            "...BBBBBBBB.........", "....BBBBBB..........", ".....BBBBB..........",
            ".....BB.BB..........", ".....B...B..........", ".....BB..BB........."
        };
        public RunnerGame()
        {
            Text = "Taskbar_Games - Dino Runner"; Icon = ArcadeTheme.AppIcon;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            BackColor = TransparencyKey = Color.Magenta; TopMost = true;
            DoubleBuffered = true; KeyPreview = true; StartPosition = FormStartPosition.Manual;
            PositionGame();
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
            timer.Tick += delegate {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                if (Left != area.Left || Bottom != area.Bottom || Width != area.Width / 2) { PositionGame(); session.ResetForLayout(); Invalidate(); }
                double now = clock.Elapsed.TotalSeconds;
                bool moving = session.Active && session.Phase == RunnerPhase.Running;
                session.Advance(now - last, Width); last = now;
                timer.Interval = moving ? 16 : 250;
                if (moving) Invalidate();
            };
            timer.Start();
        }
        void PositionGame()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = MaximumSize = new Size(area.Width / 2, 100);
            Bounds = new Rectangle(area.Left, area.Bottom - 100, area.Width / 2, 100);
        }
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
        static void Fill(Graphics g, Color color, int x, int y, int width, int height)
        { using (SolidBrush b = new SolidBrush(color)) g.FillRectangle(b, x, y, width, height); }
        static void Label(Graphics g, string text, int x, int y, Color color)
        { Fill(g, Color.Black, x - 4, y - 4, PixelArt.Width(text) + 8, 15); PixelArt.Text(g, text, color, x, y); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Baseball
{
    public sealed class BaseballGame : Form
    {
        readonly BaseballSession session = new BaseballSession();
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Color ink = ArcadeTheme.Black;
        readonly Color mint = ArcadeTheme.Neon;
        readonly Color gold = Color.FromArgb(255, 233, 77);
        const int ContactX = 108;
        const float GameScale = 0.65f;
        float WorldWidth { get { return ClientSize.Width / GameScale; } }
        int ground, pitcherX;
        Rectangle closeButton;
        double lastTime;
        bool spaceHeld;
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

        public BaseballGame()
        {
            Text = "Taskbar_Games - Baseball";
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
                session.SetActive(true);
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimer();
                Invalidate();
            };
            Deactivate += delegate {
                spaceHeld = false;
                session.SetActive(false);
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimer();
                Invalidate();
            };
            KeyDown += OnGameKeyDown;
            KeyUp += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Space) { spaceHeld = false; e.Handled = e.SuppressKeyPress = true; }
            };
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (closeButton.Contains((int)(e.X / GameScale), (int)(e.Y / GameScale))) { Close(); return; }
                Activate();
                Focus();
            };
            timer.Interval = 250;
            timer.Tick += Tick;
            timer.Start();
        }

        void ResetLayout()
        {
            Bounds = Screen.PrimaryScreen.WorkingArea;
            ground = (int)((ClientSize.Height - 2) / GameScale);
            pitcherX = Math.Min((int)WorldWidth - 44, Math.Max(390, Math.Min(580, (int)(WorldWidth * 0.36))));
            closeButton = new Rectangle(338, ground - 15, 15, 14);
            session.ResetRound();
            spaceHeld = false;
            lastTime = clock.Elapsed.TotalSeconds;
        }

        void OnGameKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true;
            if (spaceHeld || !session.Active) return;
            spaceHeld = true;
            // Synchronize to the actual key press, independent of timer jitter.
            bool changed = AdvanceToNow();
            changed = session.Swing() || changed;
            if (changed) Invalidate();
            UpdateTimer();
        }

        bool AdvanceToNow()
        {
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - lastTime;
            lastTime = now;
            return session.Advance(elapsed);
        }

        void Tick(object sender, EventArgs e)
        {
            bool changed = false;
            if (Bounds != Screen.PrimaryScreen.WorkingArea) { ResetLayout(); changed = true; }
            changed = AdvanceToNow() || changed;
            UpdateTimer();
            if (changed) Invalidate();
        }

        void UpdateTimer()
        {
            timer.Interval = !session.Active ? 250 : session.Phase == BaseballPhase.Result && session.SwingAge < 0
                ? Math.Max(1, Math.Min(250, (int)Math.Ceiling((BaseballSession.ResultSeconds - session.Elapsed) * 1000))) : 16;
        }

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

        float FieldX(double meters) { return ContactX + (WorldWidth - ContactX - 28) * (float)meters / 160f; }

        float LandingX()
        {
            return FieldX(session.LastHit.Distance);
        }

        void Box(Graphics g, int x, int y, int width, int height, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            GraphicsState saved = g.Save();
            g.ScaleTransform(GameScale, GameScale);
            g.SmoothingMode = SmoothingMode.None;
            DrawFence(g);
            BatterAnimation.Draw(g, ground, session.SwingAge);
            bool winding = session.Phase == BaseballPhase.Ready && session.Elapsed > 0.4 && session.Elapsed < 0.72;
            PixelArt.Sprite(g, winding ? PitcherWindup : Pitcher, pitcherX - 20, ground - 48, Color.FromArgb(227, 101, 110));
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

        void DrawFence(Graphics g)
        {
            int x = (int)FieldX(BaseballSession.FenceDistance);
            int top = ground - BaseballSession.FenceHeight;
            Color mesh = Color.FromArgb(104, 142, 155);
            for (int yy = top + 4; yy < ground; yy += 4) Box(g, x, yy, 18, 1, mesh);
            for (int xx = x + 4; xx < x + 18; xx += 4) Box(g, xx, top, 1, BaseballSession.FenceHeight, mesh);
            Box(g, x, top, 2, BaseballSession.FenceHeight, ink);
            Box(g, x + 16, top, 2, BaseballSession.FenceHeight, ink);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}

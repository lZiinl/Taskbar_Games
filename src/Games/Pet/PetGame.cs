using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Pet
{
    public sealed class PetGame : Form
    {
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolStripMenuItem status = new ToolStripMenuItem();
        readonly ToolTip hint = new ToolTip();
        readonly bool persist;
        readonly string savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Taskbar_Games", "squirrel.txt");
        PetSession session = new PetSession();
        string action = "idle";
        double remaining = 120, lastTime, saveAge, x, petCooldown;
        double? foodX;
        AcornMarker foodMarker;
        bool placingFood;
        int direction = 1;
        bool menuOpen;
        string saveError = "";

        public PetGame() : this(true) { }
        internal PetGame(bool loadSettings)
        {
            persist = loadSettings;
            Text = "Taskbar_Games - Squirrel";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(76, 56);
            BackColor = TransparencyKey = Color.Magenta;
            TopMost = true;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            if (persist) LoadState();
            x = Screen.PrimaryScreen.WorkingArea.Left + 120;
            PositionPet();
            ArcadeTheme.Style(menu);
            status.Enabled = false;
            menu.Items.Add(status);
            menu.Items.Add("도토리 놓기...", null, delegate { BeginInvoke(new Action(PlaceFood)); });
            menu.Items.Add("쓰다듬기", null, delegate { Stroke(); });
            menu.Items.Add("낮잠 자기 / 깨우기", null, delegate { StartAction(action == "sleep" ? "idle" : "sleep", action == "sleep" ? 120 : 300); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("게임 선택으로 돌아가기 (Esc)", null, delegate { Close(); });
            menu.Opening += delegate { menuOpen = true; UpdateStatus(); };
            menu.Closed += delegate { menuOpen = false; lastTime = clock.Elapsed.TotalSeconds; };
            ContextMenuStrip = menu;
            hint.SetToolTip(this, "다람쥐 · 클릭해서 쓰다듬기 · 우클릭으로 먹이와 낮잠");
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Stroke(); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            timer.Interval = 40;
            timer.Tick += Tick;
            timer.Start();
            FormClosing += delegate { SaveState(); };
        }

        void UpdateStatus()
        {
            status.Text = String.Format("포만감 {0:0} · 기분 {1:0} · 도토리 {2}개{3}", session.Fullness, session.Mood, session.Meals, saveError);
        }
        void StartAction(string next, double seconds)
        {
            // Finish eating before accepting another activity or meal.
            if (foodX.HasValue || action == "eat") return;
            action = next; remaining = seconds; Invalidate();
        }
        void Stroke()
        {
            if (clock.Elapsed.TotalSeconds < petCooldown || foodX.HasValue || action == "eat") return;
            session.Pet(); petCooldown = clock.Elapsed.TotalSeconds + 1.5;
            StartAction("love", 4); UpdateStatus();
        }
        void PlaceFood()
        {
            if (IsDisposed || placingFood || action == "eat") return;
            placingFood = true;
            timer.Stop();
            try
            {
                using (AcornPlacement picker = new AcornPlacement())
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        SetFoodTarget(picker.SelectedX);
                        if (foodMarker != null) foodMarker.Dispose();
                        foodMarker = new AcornMarker();
                        PositionFood();
                        foodMarker.Show(this);
                    }
            }
            finally
            {
                placingFood = false;
                lastTime = clock.Elapsed.TotalSeconds;
                if (!IsDisposed) timer.Start();
            }
        }
        internal void SetFoodTarget(double screenX)
        {
            if (Double.IsNaN(screenX) || Double.IsInfinity(screenX)) return;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            foodX = Math.Max(area.Left + 38, Math.Min(area.Right - 38, screenX));
            action = "walk"; remaining = 30;
        }
        void PositionFood()
        {
            if (foodMarker == null || !foodX.HasValue) return;
            foodMarker.Location = new Point((int)foodX.Value - 6, Screen.PrimaryScreen.WorkingArea.Bottom - 12);
        }
        void PositionPet()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            x = Math.Max(area.Left, Math.Min(Math.Max(area.Left, area.Right - Width), x));
            Location = new Point((int)x, area.Bottom - Height);
        }
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
        internal void AdvanceBehavior(double dt)
        {
            if (dt <= 0 || Double.IsNaN(dt) || Double.IsInfinity(dt)) return;
            remaining -= dt;
            if (foodX.HasValue)
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                foodX = Math.Max(area.Left + 38, Math.Min(area.Right - 38, foodX.Value));
                double destination = foodX.Value - 38;
                double distance = destination - x;
                if (Math.Abs(distance) > 0.1) direction = distance > 0 ? 1 : -1;
                double travel = Math.Min(Math.Abs(distance), 36 * dt);
                x += Math.Sign(distance) * travel;
                action = "walk";
                if (Math.Abs(destination - x) < 0.1)
                {
                    foodX = null;
                    if (foodMarker != null) { foodMarker.Dispose(); foodMarker = null; }
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
        static void RoundPixel(Graphics g, Color color, int x, int y, int width, int height, int corner)
        {
            for (int row = 0; row < height; row++)
            {
                int inset = Math.Max(0, corner - Math.Min(row, height - 1 - row));
                Box(g, color, x + inset, y + row, width - inset * 2, 1);
            }
        }
        static void Box(Graphics g, Color color, int x, int y, int width, int height)
        { using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, width, height); }

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
        static void DrawHeart(Graphics g, int x, int y, int scale)
        {
            string[] rows = { ".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." };
            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < rows[row].Length; col++)
                    if (rows[row][col] == 'X') Box(g, Color.FromArgb(244, 139, 157), x + col * scale, y + row * scale, scale, scale);
        }
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
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); menu.Dispose(); hint.Dispose(); if (foodMarker != null) foodMarker.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

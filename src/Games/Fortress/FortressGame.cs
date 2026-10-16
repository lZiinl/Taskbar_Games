using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Fortress
{
    public sealed class FortressGame : Form
    {
        // Rendering, mouse input and physics share compact logical coordinates.
        const float GameScale = 0.60f;
        const float TankOriginX = 55; // Center of the tank tracks is position 0.
        const double FullChargeSeconds = 2.0;
        const string SettingsKey = @"Software\TaskbarFortress";
        readonly string legacyColorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tank-color.txt");
        Color tankBase = Color.FromArgb(93, 184, 106);
        Color tankLight = Color.FromArgb(184, 246, 153);
        Color tankShadow = Color.FromArgb(48, 105, 79);
        bool choosingColor;
        bool waitForSpaceRelease;
        float WorldWidth { get { return ClientSize.Width / GameScale; } }
        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random random = new Random();
        readonly Color ink = ArcadeTheme.Black;
        readonly Color green = ArcadeTheme.Neon;
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
        Rectangle panel;
        PointF muzzle, projectile;
        float ground, targetX;
        double lastTime, flightTime, speed, charge, resultUntil, shotGravity;
        bool charging, flying, focused, wasHit;
        string result = "";
        int hits, shots, combo;
        sealed class Debris
        {
            public float X, Y, VX, VY, Size;
            public double Age, Lifetime;
            public Color Color;
        }
        readonly List<Debris> debris = new List<Debris>();
        // Cosmetic randomness must not change the sequence of target positions.
        readonly Random effectRandom = new Random();

        public FortressGame()
            : this(true)
        {
        }

        // Tests can exercise a real form without reading or migrating user settings.
        internal FortressGame(bool loadSettings)
        {
            Text = "Taskbar_Games - Fortress";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            if (loadSettings) LoadTankColor();
            ResetLayout();
            Activated += delegate {
                focused = true;
                waitForSpaceRelease = KeyboardState.SpaceDown;
                Invalidate();
            };
            Deactivate += delegate {
                focused = false;
                charging = false;
                charge = 0;
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
            Bounds = Screen.PrimaryScreen.WorkingArea;
            ground = (ClientSize.Height - 2) / GameScale;
            muzzle = new PointF(87, ground - 29);
            panel = new Rectangle(126, (int)ground - 24, 200, 24);
            UpdatePanelLayout();
            charging = flying = false;
            charge = 0;
            result = "";
            debris.Clear();
            NewTarget();
        }

        void NewTarget()
        {
            float span = WorldWidth - TankOriginX;
            float left = Math.Max(TankOriginX + span * 0.10f, MinimumTargetX());
            float right = Math.Max(left, TankOriginX + span * 0.80f);
            targetX = left + (float)random.NextDouble() * (right - left);
        }

        static int ScorePanelWidth(int hitCount, int shotCount)
        {
            return (int)Math.Ceiling(TextWidth(String.Format("{0} HIT / {1} SHOTS", hitCount, shotCount), 1)) + 36;
        }

        void UpdatePanelLayout()
        {
            panel.Width = ScorePanelWidth(hits, shots);
        }

        float MinimumTargetX()
        {
            // Reserve the next shot's digits before aiming. The sprite is 24 device
            // pixels wide; leave another 8 pixels between it and the score panel.
            int nextPanelRight = panel.X + ScorePanelWidth(hits + 1, shots + 1);
            return Math.Max(panel.Right, nextPanelRight) + (12 + 8) / GameScale;
        }

        void UpdateTimerInterval(double now)
        {
            bool animating = (charging && charge < 1) || flying || debris.Count > 0;
            // A slow idle poll still catches taskbar/work-area changes. Static
            // result labels only need a wakeup when their display time expires.
            timer.Interval = animating ? 16 : result.Length > 0
                ? Math.Max(1, Math.Min(250, (int)Math.Ceiling((resultUntil - now) * 1000)))
                : 250;
        }

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
            if (waitForSpaceRelease) return;
            if (!charging && !flying && result.Length == 0)
            {
                targetX = Math.Max(targetX, MinimumTargetX());
                charging = true;
                charge = 0;
                lastTime = clock.Elapsed.TotalSeconds;
                UpdateTimerInterval(lastTime);
                Invalidate();
            }
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space) return;
            e.Handled = e.SuppressKeyPress = true;
            waitForSpaceRelease = false;
            if (!charging) return;
            charging = false;
            double maxRange = WorldWidth - muzzle.X;
            double minRange = Math.Min(70, maxRange);
            double range = minRange + charge * (maxRange - minRange);
            speed = Ballistics.SpeedForRange(range, ground - muzzle.Y);
            shotGravity = Ballistics.GravityForRange(range, ground - muzzle.Y);
            flightTime = 0;
            projectile = muzzle;
            flying = true;
            shots++;
            UpdatePanelLayout();
            lastTime = clock.Elapsed.TotalSeconds;
            UpdateTimerInterval(lastTime);
            Invalidate();
        }

        void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, now - lastTime);
            lastTime = now;
            bool changed = false;
            if (Bounds != Screen.PrimaryScreen.WorkingArea) { ResetLayout(); changed = true; }
            if (charging && charge < 1)
            {
                charge = Math.Min(1, charge + dt / FullChargeSeconds);
                changed = true;
            }
            if (flying)
            {
                changed = true;
                // Small physics steps prevent a fast shell from skipping the target.
                int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
                for (int i = 0; i < steps && flying; i++)
                {
                    flightTime += dt / steps;
                    projectile = Ballistics.Position(muzzle, speed, flightTime, shotGravity);
                    double dx = projectile.X - targetX, dy = projectile.Y - (ground - 19);
                    if (dx * dx + dy * dy <= 23 * 23) Finish(true, now);
                    else if (projectile.Y >= ground || projectile.X > WorldWidth + 10) Finish(false, now);
                }
            }
            if (debris.Count > 0) { UpdateDebris(dt); changed = true; }
            if (result.Length > 0 && now >= resultUntil)
            {
                if (wasHit) NewTarget();
                result = "";
                charge = 0;
                changed = true;
            }
            UpdateTimerInterval(now);
            if (changed) Invalidate();
        }

        void Finish(bool hit, double now)
        {
            flying = false;
            wasHit = hit;
            if (hit)
            {
                hits++;
                UpdatePanelLayout();
                combo++;
                BurstTarget();
            }
            else combo = 0;
            result = hit ? "HIT" : "MISS";
            resultUntil = now + 1.2;
        }

        void BurstTarget()
        {
            debris.Clear();
            Color[] colors = { Color.FromArgb(238, 91, 103), Color.FromArgb(255, 236, 192),
                Color.FromArgb(166, 43, 66), Color.FromArgb(255, 218, 123) };
            for (int i = 0; i < 28; i++)
                debris.Add(new Debris {
                    X = targetX + (float)(effectRandom.NextDouble() * 30 - 15),
                    Y = ground - 19 + (float)(effectRandom.NextDouble() * 24 - 12),
                    VX = (float)(effectRandom.NextDouble() * 200 - 100),
                    VY = -(float)(80 + effectRandom.NextDouble() * 160),
                    Size = (i % 3 == 0 ? 3 : 2) / GameScale,
                    Lifetime = 0.45 + effectRandom.NextDouble() * 0.4,
                    Color = colors[i % colors.Length]
                });
        }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.ScaleTransform(GameScale, GameScale);
            g.SmoothingMode = SmoothingMode.None;
            string score = String.Format("{0} HIT / {1} SHOTS", hits, shots);
            PixelRect(g, ink, panel.X, panel.Y, panel.Width, panel.Height);
            DrawPixelText(g, score, 1, focused ? green : Color.LightGray, panel.X + 6, panel.Y + 6);
            PixelRect(g, Color.FromArgb(53, 67, 86), panel.Right - 24, panel.Y + 3, 2, panel.Height - 6);
            DrawPixelText(g, "X", 1, Color.LightGray, panel.Right - 16, panel.Y + 6);

            // A narrow power column sits beside the tank, filling from the ground up.
            Rectangle meter = new Rectangle(100, (int)ground - 44, 13, 42);
            PixelRect(g, ink, meter.X - 3, meter.Y - 3, meter.Width + 6, meter.Height + 6);
            int lit = (int)Math.Ceiling(charge * 8);
            for (int i = 0; i < 8; i++)
            {
                Color segment = i < lit ? (i >= 6 ? Color.FromArgb(255, 195, 98) : green) : Color.FromArgb(53, 67, 86);
                PixelRect(g, segment, meter.X, meter.Bottom - (i + 1) * 5, meter.Width, 3);
            }

            string hint = !focused ? "CLICK TANK" : charging ? ((int)(charge * 100)) + "%" : flying || result.Length > 0 ? "" : "HOLD SPACE";
            if (hint.Length > 0)
            {
                PixelRect(g, ink, panel.X, ground - 47, TextWidth(hint, 1) + 12, 21);
                DrawPixelText(g, hint, 1, Color.FromArgb(207, 217, 232), panel.X + 6, ground - 42);
            }

            DrawPixelSprite(g, TankPixels, TankOriginX, ground);
            if (!(wasHit && result.Length > 0)) DrawPixelSprite(g, TargetPixels, targetX, ground);
            foreach (Debris p in debris)
            {
                float size = p.Age > p.Lifetime * 0.65 ? 1 / GameScale : p.Size;
                PixelRect(g, p.Color, p.X - size / 2, p.Y - size / 2, size, size);
            }
            if (flying)
            {
                for (int i = 5; i >= 0; i--)
                {
                    double t = Math.Max(0, flightTime - i * 0.025);
                    PointF p = Ballistics.Position(muzzle, speed, t, shotGravity);
                    float radius = i == 0 ? 5 : 2;
                    PixelRect(g, i == 0 ? ink : Color.FromArgb(154, 171, 190), p.X - radius, p.Y - radius, radius * 2, radius * 2);
                    if (i == 0) PixelRect(g, Color.FromArgb(255, 218, 123), p.X - 3, p.Y - 3, 6, 6);
                }
            }
            if (result.Length > 0)
            {
                SizeF size = new SizeF(TextWidth(result, 2), 14 / GameScale);
                string comboText = wasHit && combo >= 2 ? combo + " COMBO" : "";
                float labelWidth = Math.Max(size.Width, TextWidth(comboText, 1));
                float x = targetX + 31;
                if (x + labelWidth + 6 > WorldWidth)
                    x = targetX - 31 - labelWidth;
                float y = ground - 19 - size.Height / 2;
                PixelRect(g, ink, x - 6, y - 4, size.Width + 12, size.Height + 8);
                DrawPixelText(g, result, 2, wasHit ? green : Color.FromArgb(255, 135, 141), x, y);
                if (comboText.Length > 0)
                {
                    PixelRect(g, ink, x - 6, y - 25, labelWidth + 12, 19);
                    DrawPixelText(g, comboText, 1, Color.FromArgb(255, 218, 123), x, y - 21);
                }
            }
        }

        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string> {
            {'0', "0E11131519110E"}, {'1', "040C040404040E"},
            {'2', "0E11010204081F"}, {'3', "1E01010E01011E"},
            {'4', "02060A121F0202"}, {'5', "1F10101E01011E"},
            {'6', "0E10101E11110E"}, {'7', "1F010204080808"},
            {'8', "0E11110E11110E"}, {'9', "0E11110F01010E"},
            {'A', "0E11111F111111"}, {'B', "1E11111E11111E"}, {'C', "0E11101010110E"},
            {'D', "1E11111111111E"}, {'E', "1F10101E10101F"},
            {'H', "1111111F111111"}, {'I', "0E04040404040E"},
            {'K', "11121418141211"}, {'L', "1010101010101F"},
            {'M', "111B1515111111"}, {'N', "11191915131311"},
            {'O', "0E11111111110E"}, {'P', "1E11111E101010"},
            {'S', "0F10100E01011E"}, {'T', "1F040404040404"},
            {'X', "11110A040A1111"}, {'/', "01010204081010"},
            {'%', "191A0204080B13"}
        };

        static float TextWidth(string text, int pixel)
        {
            return Math.Max(0, text.Length * 6 - 1) * pixel / GameScale;
        }

        static void PixelRect(Graphics g, Color color, float x, float y, float width, float height)
        {
            GraphicsState state = g.Save();
            g.ResetTransform();
            using (SolidBrush brush = new SolidBrush(color))
                g.FillRectangle(brush, (int)Math.Round(x * GameScale), (int)Math.Round(y * GameScale),
                    Math.Max(1, (int)Math.Round(width * GameScale)), Math.Max(1, (int)Math.Round(height * GameScale)));
            g.Restore(state);
        }

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

        static Color OpaqueTankColor(Color color)
        {
            // Pure magenta is the window's transparency key, so keep it visible.
            return color.R == 255 && color.G == 0 && color.B == 255
                ? Color.FromArgb(255, 1, 255) : Color.FromArgb(color.R, color.G, color.B);
        }

        void SetTankColor(Color color)
        {
            tankBase = OpaqueTankColor(color);
            tankLight = OpaqueTankColor(Color.FromArgb(
                tankBase.R + (255 - tankBase.R) * 2 / 5,
                tankBase.G + (255 - tankBase.G) * 2 / 5,
                tankBase.B + (255 - tankBase.B) * 2 / 5));
            tankShadow = Color.FromArgb(tankBase.R * 3 / 5, tankBase.G * 3 / 5, tankBase.B * 3 / 5);
        }

        void LoadTankColor()
        {
            bool loaded = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    object value = key == null ? null : key.GetValue("TankColor");
                    if (value is int && (int)value >= 0 && (int)value <= 0xFFFFFF)
                    {
                        int rgb = (int)value;
                        SetTankColor(Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
                        loaded = true;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }

            // Only remove the old file after a valid color is safely persisted.
            try
            {
                int rgb;
                if (File.Exists(legacyColorPath) && int.TryParse(File.ReadAllText(legacyColorPath).Trim(),
                    System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb)
                    && rgb >= 0 && rgb <= 0xFFFFFF)
                {
                    if (!loaded)
                    {
                        SetTankColor(Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
                        SaveTankColor();
                    }
                    File.Delete(legacyColorPath);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        void SaveTankColor()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                if (key == null) throw new IOException("Cannot open user settings.");
                key.SetValue("TankColor", tankBase.ToArgb() & 0xFFFFFF, RegistryValueKind.DWord);
                key.Flush();
            }
        }

        void ChooseTankColor()
        {
            if (choosingColor) return;
            choosingColor = true;
            charging = false;
            charge = 0;
            timer.Stop();
            double pausedAt = clock.Elapsed.TotalSeconds;
            try
            {
                using (TankPalette picker = new TankPalette(tankBase))
                {
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        SetTankColor(picker.SelectedColor);
                        try
                        {
                            SaveTankColor();
                        }
                        catch (IOException) { ShowColorSaveError(); }
                        catch (UnauthorizedAccessException) { ShowColorSaveError(); }
                        catch (System.Security.SecurityException) { ShowColorSaveError(); }
                    }
                }
            }
            finally
            {
                double now = clock.Elapsed.TotalSeconds;
                resultUntil += now - pausedAt;
                lastTime = now;
                choosingColor = false;
                UpdateTimerInterval(now);
                timer.Start();
                Activate();
                Invalidate();
            }
        }

        void ShowColorSaveError()
        {
            MessageBox.Show(this, "색상은 적용했지만 Windows 사용자 설정에 저장하지 못했습니다. 다음 실행에는 유지되지 않을 수 있습니다.",
                "Fortress", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Fortress;

public static class FortressRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type GameType = typeof(FortressGame);

    sealed class LeftmostRandom : Random
    {
        public override double NextDouble() { return 0; }
    }

    static object Get(FortressGame game, string name)
    {
        return GameType.GetField(name, Private).GetValue(game);
    }

    static void Set(FortressGame game, string name, object value)
    {
        GameType.GetField(name, Private).SetValue(game, value);
    }

    static void Call(FortressGame game, string name, params object[] args)
    {
        GameType.GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(game, args);
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void AssertClear(FortressGame game)
    {
        Rectangle panel = (Rectangle)Get(game, "panel");
        double targetLeft = Math.Round((float)Get(game, "targetX") * 0.6) - 12;
        double panelRight = Math.Round(panel.X * 0.6) + Math.Round(panel.Width * 0.6);
        Assert(targetLeft - panelRight >= 7, "Target overlaps score panel or loses its 8px margin.");
        Assert((float)Get(game, "targetX") * 0.6 + 12 <= game.ClientSize.Width, "Target leaves screen.");
    }

    static void Render(FortressGame game, string path)
    {
        using (Bitmap bitmap = new Bitmap(game.ClientSize.Width, game.ClientSize.Height))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 238, 243));
            Call(game, "OnPaint", new PaintEventArgs(graphics, game.ClientRectangle));
            using (Bitmap crop = bitmap.Clone(new Rectangle(0, bitmap.Height - 80, 600, 80), bitmap.PixelFormat))
                crop.Save(path, ImageFormat.Png);
        }
    }

    public static void Run(string artifacts)
    {
        Directory.CreateDirectory(artifacts);
        using (FortressGame game = (FortressGame)Activator.CreateInstance(GameType, Private, null, new object[] { false }, null))
        {
            Timer timer = (Timer)Get(game, "timer");
            timer.Stop();
            Set(game, "random", new LeftmostRandom());
            Set(game, "focused", true);
            foreach (int width in new int[] { 640, 800, 1024, 1366, 1920, 3840 })
            foreach (int count in new int[] { 0, 9, 99, 999, 9999 })
            {
                game.ClientSize = new Size(width, 768);
                Set(game, "ground", (768 - 2) / 0.6f);
                Set(game, "panel", new Rectangle(126, (int)((768 - 2) / 0.6f) - 24, 200, 24));
                Set(game, "hits", count);
                Set(game, "shots", count);
                Call(game, "UpdatePanelLayout");
                Call(game, "NewTarget");
                AssertClear(game);
                if (width == 1366 && count == 0) Render(game, Path.Combine(artifacts, "fortress-1366.png"));

                // Digit growth is reserved before aiming, not during flight.
                Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
                float aimedAt = (float)Get(game, "targetX");
                Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
                AssertClear(game);
                Assert((float)Get(game, "targetX") == aimedAt, "Target moved after firing.");
                Set(game, "flying", false);
            }
            Console.WriteLine("PASS: target clearance at six screen widths and five score digit boundaries.");

            Set(game, "hits", 0);
            Set(game, "shots", 0);
            Call(game, "ResetLayout");
            IntPtr handle = game.Handle;
            int invalidations = 0;
            game.Invalidated += delegate { invalidations++; };
            for (int i = 0; i < 120; i++) Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0, "Idle ticks repaint the screen.");
            Assert(timer.Interval == 250, "Idle timer is still running at animation speed.");

            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(timer.Interval == 16, "Charging does not wake animation timer.");
            invalidations = 0;
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 0.02);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && (double)Get(game, "charge") > 0, "Charging no longer animates.");

            Set(game, "charge", 1.0);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Full charge keeps repainting.");
            Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
            Assert((bool)Get(game, "flying") && timer.Interval == 16, "Full charge release does not start flight.");
            invalidations = 0;
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 0.02);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && (double)Get(game, "flightTime") > 0, "Flight no longer animates.");

            double now = ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds;
            Call(game, "Finish", false, now);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0, "Static MISS label keeps repainting.");
            Set(game, "resultUntil", -1.0);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert((string)Get(game, "result") == "" && invalidations > 0, "Result expiry does not repaint.");

            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            typeof(Form).GetMethod("OnDeactivate", Private).Invoke(game, new object[] { EventArgs.Empty });
            Assert(!(bool)Get(game, "charging") && (double)Get(game, "charge") == 0 && timer.Interval == 250,
                "Focus loss does not cancel charge and return to idle.");

            now = ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds;
            Call(game, "Finish", true, now);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && timer.Interval == 16, "Impact particles no longer animate.");
            Call(game, "UpdateDebris", 2.0);
            Set(game, "resultUntil", -1.0);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(timer.Interval == 250, "Completed effects do not return to idle.");

            // The low-frequency poll must still detect display/work-area changes.
            game.Bounds = new Rectangle(game.Left, game.Top, game.Width - 1, game.Height);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(game.Bounds == Screen.PrimaryScreen.WorkingArea && invalidations > 0,
                "Idle work-area changes are not handled.");
            Console.WriteLine("PASS: 120 idle ticks with zero redraws; charge, full charge, flight, results, focus loss, particles and work-area reset.");
            timer.Stop();
        }
    }
}

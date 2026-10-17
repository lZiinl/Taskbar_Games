using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Shared;

public static class FortressRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(FortressGame game, string name) { return typeof(FortressGame).GetField(name, Private).GetValue(game); }
    static void Set(FortressGame game, string name, object value) { typeof(FortressGame).GetField(name, Private).SetValue(game, value); }
    static void Call(FortressGame game, string name, params object[] args) { typeof(FortressGame).GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(game, args); }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static FortressSession Session(FortressGame game) { return (FortressSession)Get(game, "session"); }
    static void AssertClear(FortressGame game)
    {
        Rectangle panel = (Rectangle)Get(game, "panel");
        double targetLeft = Math.Round(Session(game).TargetX * 0.6) - 12;
        double panelRight = Math.Round(panel.X * 0.6) + Math.Round(panel.Width * 0.6);
        Assert(targetLeft - panelRight >= 7, "Target overlaps score panel.");
        Assert(Session(game).TargetX * 0.6 + 12 <= game.ClientSize.Width, "Target leaves screen.");
    }
    public static void Run(string artifacts)
    {
        DisplaySelection original = GameEnvironment.Display;
        try
        {
            foreach (int width in new int[] { 640, 800, 1024, 1366, 1920, 3840 })
            foreach (int count in new int[] { 0, 9, 99, 999, 9999 })
            {
                GameEnvironment.Display = new DisplaySelection(() => new[] { new DisplayInfo("TEST", new Rectangle(-width, -100, width, 768), true) });
                GameStore store = new GameStore(); store.Data.FortressHits = store.Data.FortressShots = count;
                using (FortressGame game = new FortressGame(store))
                {
                    ((Timer)Get(game, "timer")).Stop();
                    AssertClear(game); Set(game, "focused", true);
                    Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
                    float target = Session(game).TargetX;
                    Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
                    AssertClear(game); Assert(Session(game).TargetX == target, "Target moved after firing.");
                    Assert(store.Data.FortressShots == count + 1, "Shot record was not saved.");
                    if (width == 1366 && count == 0)
                        using (Bitmap bitmap = new Bitmap(game.Width, game.Height))
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.Clear(Color.FromArgb(235, 238, 243)); Call(game, "OnPaint", new PaintEventArgs(g, game.ClientRectangle));
                            using (Bitmap crop = bitmap.Clone(new Rectangle(0, bitmap.Height - 80, 600, 80), bitmap.PixelFormat))
                                crop.Save(Path.Combine(artifacts, "fortress-1366.png"), ImageFormat.Png);
                        }
                }
            }
        }
        finally { GameEnvironment.Display = original; }
        using (FortressGame game = new FortressGame(new GameStore()))
        {
            Timer timer = (Timer)Get(game, "timer"); timer.Stop();
            FortressSession session = Session(game);
            IntPtr handle = game.Handle; int invalidations = 0;
            game.Invalidated += delegate { invalidations++; };
            for (int i = 0; i < 120; i++) Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Idle window repaints.");
            Set(game, "focused", true);
            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(timer.Interval == 16, "Charge does not wake timer.");
            for (int i = 0; i < 40; i++) session.Advance(0.05, 300);
            invalidations = 0; Call(game, "Tick", game, EventArgs.Empty);
            Assert(session.Charge == 1 && invalidations == 0 && timer.Interval == 250, "Full charge keeps repainting.");
            Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
            Assert(session.Flying && timer.Interval == 16, "Release does not start flight.");
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 0.02);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(session.FlightTime > 0, "Flight did not advance.");
            for (int i = 0; i < 2000 && session.Flying; i++) session.Advance(0.01, 300);
            Assert(session.Result == "MISS", "Full-range shot unexpectedly hit target.");
            invalidations = 0; Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Static result repaints.");
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 2);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(session.Result == "" && invalidations > 0, "Result expiry did not repaint.");
            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            typeof(Form).GetMethod("OnDeactivate", Private).Invoke(game, new object[] { EventArgs.Empty });
            Assert(!session.Charging && session.Charge == 0 && timer.Interval == 250, "Focus loss did not cancel charge.");
            Call(game, "BurstTarget");
            Assert(((System.Collections.ICollection)Get(game, "debris")).Count == 28, "Impact particles missing.");
            Call(game, "UpdateDebris", 2.0);
            Assert(((System.Collections.ICollection)Get(game, "debris")).Count == 0, "Particles did not expire.");
            game.Width--; Call(game, "Tick", game, EventArgs.Empty);
            Assert(game.Bounds == GameEnvironment.Display.WorkingArea, "Work-area reset failed.");
        }
        FortressSession model = new FortressSession(new LeftmostRandom(), 3, 5, 2);
        model.ResetLayout(1800, 600, new PointF(87, 571), 400);
        Assert(model.TargetX == 400 && model.Hits == 3 && model.Combo == 0 && model.BestCombo == 2, "Restored records or minimum target wrong.");
        Assert(model.BeginCharge(420) && !model.BeginCharge(420) && model.TargetX == 420, "Charging state boundary failed.");
        model.Advance(Double.NaN, 420); Assert(model.Charge == 0, "Invalid time accepted.");
        model.CancelCharge(); Assert(!model.ReleaseCharge() && model.Shots == 5, "Cancelled charge fired.");
        Console.WriteLine("PASS: Fortress model state, six screen widths, score digit growth, saved shots, idle/charge/flight/result/focus and particles.");
    }
    sealed class LeftmostRandom : Random { public override double NextDouble() { return 0; } }
}

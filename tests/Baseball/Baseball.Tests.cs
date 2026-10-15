using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Baseball;

public static class BaseballRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    static BaseballSession NewPitch(int seed)
    {
        BaseballSession game = new BaseballSession(new Random(seed));
        game.SetActive(true);
        game.Advance(BaseballSession.ReadySeconds);
        Assert(game.Phase == BaseballPhase.Pitch, "Pitch did not start.");
        return game;
    }

    static BaseballSession Hit(double error, double frameSeconds)
    {
        BaseballSession game = NewPitch(42);
        double pressAt = game.PitchSeconds - BaseballSession.SwingContactDelay + error;
        while (game.Elapsed + frameSeconds < pressAt) game.Advance(frameSeconds);
        game.Advance(pressAt - game.Elapsed);
        Assert(game.Swing(), "First swing was rejected.");
        Assert(!game.Swing(), "Multiple swings accepted in one pitch.");
        for (int i = 0; game.Phase == BaseballPhase.Pitch && i < 1000; i++) game.Advance(frameSeconds);
        Assert(game.Phase == BaseballPhase.Flight, "Valid timing did not hit.");
        return game;
    }

    public static void Run(string artifacts)
    {
        BattingResult perfect = BaseballSession.EvaluateTiming(0);
        Assert(perfect.Hit && perfect.Distance == 160 && perfect.Points == 1600 && perfect.Grade == "PERFECT", "Perfect hit scoring failed.");
        Assert(BaseballSession.EvaluateTiming(0.04).Distance > BaseballSession.EvaluateTiming(0.09).Distance, "Timing quality does not change distance.");
        Assert(BaseballSession.EvaluateTiming(-0.04).Distance == BaseballSession.EvaluateTiming(0.04).Distance, "Early/late quality differs.");
        Assert(!BaseballSession.EvaluateTiming(0.121).Hit && !BaseballSession.EvaluateTiming(-0.121).Hit, "Out-of-window swing was a hit.");
        CheckFence();
        double min = Double.MaxValue, max = 0;
        for (int i = 0; i < 100; i++)
        {
            BaseballSession game = NewPitch(i);
            Assert(game.PitchSeconds >= BaseballSession.MinPitchSeconds && game.PitchSeconds <= BaseballSession.MaxPitchSeconds, "Pitch speed outside range.");
            min = Math.Min(min, game.PitchSeconds);
            max = Math.Max(max, game.PitchSeconds);
        }
        Assert(max - min > 0.4, "Random pitch speeds do not vary enough.");
        foreach (double frame in new double[] { 1.0 / 30, 1.0 / 60, 1.0 / 144 })
        {
            BaseballSession game = Hit(0, frame);
            Assert(game.LastHit.Distance == 160, "Frame rate changed perfect hit quality.");
            game.Advance(game.FlightSeconds);
            Assert(game.Score == 1600 && game.Hits == 1 && game.Attempts == 1 && game.BestDistance == 160, "Landing score not counted exactly once.");
            game.Advance(0.3);
            Assert(game.Score == 1600, "Result counted twice.");
            game.Advance(BaseballSession.ResultSeconds);
            Assert(game.Phase == BaseballPhase.Ready, "Next round did not start.");
        }
        BaseballSession early = NewPitch(7);
        early.Swing();
        early.Advance(early.PitchSeconds + 0.31);
        Assert(early.Score == 0 && early.Attempts == 1 && early.LastHit.Grade == "EARLY", "Early miss not recorded.");
        BaseballSession noSwing = NewPitch(9);
        noSwing.Advance(noSwing.PitchSeconds + 0.31);
        Assert(noSwing.Attempts == 1 && noSwing.Hits == 0 && noSwing.Score == 0, "No-swing miss failed.");
        BaseballSession late = NewPitch(12);
        late.Advance(late.PitchSeconds + 0.27);
        late.Swing();
        late.Advance(0.04);
        Assert(late.Phase == BaseballPhase.Result && late.LastHit.Grade == "LATE" && late.Score == 0,
            "A swing after the ball passed did not show late feedback.");
        Assert(late.Advance(0.1) && late.SwingAge > 0, "Late-miss follow-through stopped animating during result.");
        Assert(late.Advance(BaseballSession.SwingDuration) && late.SwingAge < 0, "Swing did not redraw its final recovery pose.");

        BaseballSession cancelled = NewPitch(1);
        cancelled.Advance(cancelled.PitchSeconds - BaseballSession.SwingContactDelay);
        cancelled.Swing();
        cancelled.SetActive(false);
        Assert(!cancelled.Advance(10) && !cancelled.Swing(), "Inactive game accepts input or advances.");
        Assert(cancelled.Phase == BaseballPhase.Ready && cancelled.Attempts == 0 && cancelled.SwingAge < 0, "Focus loss did not cancel pending contact.");
        cancelled.SetActive(true);
        cancelled.Advance(BaseballSession.ReadySeconds);
        cancelled.Advance(cancelled.PitchSeconds + 0.31);
        Assert(cancelled.Score == 0, "Cancelled swing leaked into next pitch.");

        BaseballSession flight = Hit(0.04, 1.0 / 60);
        double before = flight.Elapsed;
        flight.SetActive(false);
        flight.Advance(20);
        Assert(flight.Elapsed == before && flight.Score == 0, "Background flight progressed.");
        flight.SetActive(true);
        flight.Advance(flight.FlightSeconds);
        Assert(flight.Score == flight.LastHit.Points && flight.Score > 0, "Paused flight did not resume.");
        Console.WriteLine("PASS: random pitch bounds; timing-distance scoring at 30/60/144 FPS; misses, one swing per pitch, focus cancellation and flight resume.");

        RenderAndCheckWindow(artifacts);
        RenderSwingPoses(artifacts);
        RenderPitcherPoses(artifacts);
    }

    static double ErrorForDistance(int distance)
    {
        return BaseballSession.HitWindow * (1 - Math.Pow((distance - 12) / 148.0, 2.0 / 3));
    }

    static void CheckFence()
    {
        Assert(!BaseballSession.EvaluateTiming(ErrorForDistance(130)).HomeRun, "Landing at the fence counted as a home run.");
        BaseballSession wall = Hit(ErrorForDistance(134), 1.0 / 60);
        Assert(wall.LastHit.HitFence && !wall.LastHit.HomeRun, "A low ball passed through the fence.");
        double crossing = 130.0 / wall.LastHit.CarryDistance;
        Assert(Math.Abs(wall.FlightDistance(crossing) - 130) < 0.001, "Wall impact was misplaced.");
        Assert(wall.FlightHeight(crossing) <= BaseballSession.FenceHeight + BaseballSession.BallRadius, "Wall collision above fence.");
        Assert(wall.FlightDistance(1) == 128 && wall.FlightHeight(1) == 0, "Wall rebound did not land inside the field.");
        wall.Advance(wall.FlightSeconds);
        Assert(wall.Score == 1280 && wall.LastHit.Distance == 128, "Fence rebound scored its predicted carry instead of landing.");
        BaseballSession homer = Hit(0, 1.0 / 60);
        Assert(!homer.HomeRunVisible && homer.Score == 0, "Home run announced before crossing the fence.");
        homer.Advance(homer.FlightSeconds * 130 / 160 - 0.001);
        Assert(!homer.HomeRunVisible, "Home run announced too early.");
        homer.Advance(0.002);
        Assert(homer.HomeRunVisible && homer.Score == 0, "Fence crossing did not announce home run, or scored before landing.");
        homer.SetActive(false);
        double paused = homer.Elapsed;
        homer.Advance(2);
        Assert(homer.HomeRunVisible && homer.Elapsed == paused, "Home run flight did not pause.");
        homer.SetActive(true);
        homer.Advance(homer.FlightSeconds);
        Assert(homer.HomeRunVisible && homer.Score == 1600, "Home run landing score failed.");
        Console.WriteLine("PASS: 130m fence, low-ball rebound, home run crossing, focus pause and landing-only scoring.");
    }

    static void RenderSwingPoses(string artifacts)
    {
        Type animator = typeof(BaseballGame).Assembly.GetType("TaskbarGames.Games.Baseball.BatterAnimation");
        MethodInfo draw = animator.GetMethod("Draw", BindingFlags.Public | BindingFlags.Static);
        double[] times = { -1, 0.04, 0.09, BaseballSession.SwingContactDelay, 0.22, 0.34, 0.49, 0.72 };
        using (Bitmap strip = new Bitmap(128 * times.Length, 100))
        using (Graphics g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(235, 238, 243));
            for (int i = 0; i < times.Length; i++)
            {
                g.TranslateTransform(i * 128, 0);
                draw.Invoke(null, new object[] { g, 80, times[i] });
                using (Pen baseline = new Pen(Color.FromArgb(187, 197, 210))) g.DrawLine(baseline, 32, 82, 114, 82);
                g.ResetTransform();
            }
            strip.Save(Path.Combine(artifacts, "baseball-swing-poses.png"), ImageFormat.Png);
            for (int i = 1; i < times.Length; i++)
                for (int y = 66; y <= 80; y++)
                    for (int x = 45; x < 94; x++)
                        Assert(strip.GetPixel(x, y) == strip.GetPixel(i * 128 + x, y), "Batter feet or legs moved during swing.");
        }
    }

    static void Call(BaseballGame game, string name, params object[] args)
    {
        typeof(BaseballGame).GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(game, args);
    }

    static void RenderPitcherPoses(string artifacts)
    {
        double[] times = { 0, 0.48, 0.58, 0.67, 0.76, 0.849, 0.85, 0.97, 1.10 };
        using (BaseballGame game = new BaseballGame())
        using (Bitmap frame = new Bitmap(game.Width, game.Height))
        using (Bitmap strip = new Bitmap(times.Length * 90, 90))
        using (Graphics source = Graphics.FromImage(frame))
        using (Graphics target = Graphics.FromImage(strip))
        {
            ((Timer)typeof(BaseballGame).GetField("timer", Private).GetValue(game)).Stop();
            BaseballSession state = (BaseballSession)typeof(BaseballGame).GetField("session", Private).GetValue(game);
            int pitcher = (int)typeof(BaseballGame).GetField("pitcherX", Private).GetValue(game);
            float scale = (float)typeof(BaseballGame).GetField("GameScale", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            state.SetActive(true);
            for (int i = 0; i < times.Length; i++)
            {
                state.ResetRound();
                state.Advance(Math.Min(times[i], BaseballSession.ReadySeconds));
                if (times[i] > BaseballSession.ReadySeconds) state.Advance(times[i] - BaseballSession.ReadySeconds);
                source.Clear(Color.FromArgb(235, 238, 243));
                Call(game, "OnPaint", new PaintEventArgs(source, game.ClientRectangle));
                target.DrawImage(frame, new Rectangle(i * 90, 0, 90, 90),
                    new Rectangle((int)(pitcher * scale) - 45, frame.Height - 90, 90, 90), GraphicsUnit.Pixel);
            }
            strip.Save(Path.Combine(artifacts, "baseball-pitcher-poses.png"), ImageFormat.Png);
        }
    }

    static void Render(BaseballGame game, string path)
    {
        using (Bitmap bitmap = new Bitmap(game.Width, game.Height))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 238, 243));
            Call(game, "OnPaint", new PaintEventArgs(graphics, game.ClientRectangle));
            BaseballSession state = (BaseballSession)typeof(BaseballGame).GetField("session", Private).GetValue(game);
            // Result gold is used only for the landed shot's distance/grade and marker.
            int resultPixels = 0;
            int gold = Color.FromArgb(255, 206, 129).ToArgb();
            for (int y = bitmap.Height - 52; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == gold) resultPixels++;
            if (state.Phase == BaseballPhase.Flight) Assert(resultPixels == 0, "Distance/result appeared before landing.");
            if (state.Phase == BaseballPhase.Result && state.LastHit.Hit) Assert(resultPixels > 0, "Landing result was not rendered.");
            using (Bitmap crop = bitmap.Clone(new Rectangle(0, bitmap.Height - 190, bitmap.Width, 190), bitmap.PixelFormat))
                crop.Save(path, ImageFormat.Png);
        }
    }

    static void RenderAndCheckWindow(string artifacts)
    {
        using (BaseballGame game = new BaseballGame())
        {
            Type type = typeof(BaseballGame);
            Timer timer = (Timer)type.GetField("timer", Private).GetValue(game);
            timer.Stop();
            BaseballSession session = (BaseballSession)type.GetField("session", Private).GetValue(game);
            IntPtr handle = game.Handle;
            session.SetActive(false);
            int invalidations = 0;
            game.Invalidated += delegate { invalidations++; };
            for (int i = 0; i < 120; i++) Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Inactive baseball repaints: invalidations=" + invalidations + ", interval=" + timer.Interval + ", active=" + session.Active + ", bounds=" + game.Bounds + ", work=" + Screen.PrimaryScreen.WorkingArea);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge < 0, "Inactive window accepted a swing.");
            session.SetActive(true);
            session.Advance(0.6);
            Render(game, Path.Combine(artifacts, "baseball-windup.png"));
            session.Advance(BaseballSession.ReadySeconds);
            session.Advance(session.PitchSeconds * 0.6);
            Render(game, Path.Combine(artifacts, "baseball-pitch.png"));
            session.Advance(session.PitchSeconds - BaseballSession.SwingContactDelay - session.Elapsed);
            type.GetField("lastTime", Private).SetValue(game, ((Stopwatch)type.GetField("clock", Private).GetValue(game)).Elapsed.TotalSeconds);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge == 0, "Space does not swing.");
            session.Advance(0.01);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge > 0, "Auto-repeat restarted swing.");
            session.Advance(BaseballSession.SwingContactDelay);
            Assert(session.Phase == BaseballPhase.Flight, "Timed window swing did not hit.");
            Render(game, Path.Combine(artifacts, "baseball-swing.png"));
            session.Advance(0.55);
            Render(game, Path.Combine(artifacts, "baseball-flight.png"));
            session.Advance(session.FlightSeconds * 0.84 - session.Elapsed);
            Render(game, Path.Combine(artifacts, "baseball-home-run.png"));
            session.Advance(session.FlightSeconds);
            Render(game, Path.Combine(artifacts, "baseball-result.png"));
            game.Width = 1366;
            Render(game, Path.Combine(artifacts, "baseball-result-1366.png"));
            typeof(Form).GetMethod("OnDeactivate", Private).Invoke(game, new object[] { EventArgs.Empty });
            Assert(!session.Active && timer.Interval == 250, "Focus event did not pause baseball.");
            bool closed = false;
            game.FormClosed += delegate { closed = true; };
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Escape));
            Assert(closed && game.IsDisposed, "Esc did not close baseball.");
        }
        Console.WriteLine("PASS: baseball window input, key-repeat protection, focus pause, 120 idle ticks, Esc and rendered pitch/flight/result views.");
    }
}

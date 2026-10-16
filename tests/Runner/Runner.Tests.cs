using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Runner;

public static class RunnerRegressionTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string artifacts)
    {
        RunnerSession paused = new RunnerSession(new Random(1));
        Assert(!paused.Jump(), "Inactive runner must ignore jumps.");
        paused.Active = true; paused.Jump();
        Assert(!paused.Jump(), "Jump must not stack before the first frame.");
        paused.Advance(0.1, 600); paused.Active = false;
        double height = paused.Height, distance = paused.Distance;
        paused.Advance(2, 600);
        Assert(paused.Height == height && paused.Distance == distance, "Inactive runner must pause.");
        RunnerSession shortJump = new RunnerSession(new Random(2));
        RunnerSession fullJump = new RunnerSession(new Random(2));
        shortJump.Active = fullJump.Active = true;
        shortJump.Jump(); fullJump.Jump(); shortJump.ReleaseJump();
        shortJump.Advance(0.2, 600); fullJump.Advance(0.2, 600);
        Assert(fullJump.Height > shortJump.Height + 15, "Holding jump must reach higher than tapping.");
        double baseline = -1;
        RunnerSession jumping = new RunnerSession(new Random(7)); jumping.Active = true; jumping.Jump();
        double smallestGap = Double.MaxValue, largestGap = 0;
        for (int i = 0; i < 60 * 120; i++)
        {
            foreach (RunnerObstacle obstacle in jumping.Obstacles)
                if (obstacle.X + obstacle.Width > 46 && obstacle.X > 60 && obstacle.X < 61 + jumping.Speed * 0.26 && jumping.Height == 0)
                { jumping.Jump(); break; }
            jumping.Advance(1.0 / 60, 480);
            for (int j = 1; j < jumping.Obstacles.Count; j++)
            {
                double gap = jumping.Obstacles[j].X - jumping.Obstacles[j-1].X - jumping.Obstacles[j-1].Width;
                Assert(gap >= RunnerSession.MinimumGap - 2, "Random gaps must allow landing and the next jump at top speed.");
                smallestGap = Math.Min(smallestGap, gap); largestGap = Math.Max(largestGap, gap);
            }
        }
        Assert(jumping.Phase == RunnerPhase.Running && jumping.Speed == RunnerSession.MaximumSpeed, "Well-timed jumps must clear obstacles all the way to maximum speed.");
        Assert(largestGap - smallestGap > 100, "Obstacle gaps should visibly vary.");
        foreach (int fps in new int[] { 30, 60, 144 })
        {
            RunnerSession game = new RunnerSession(new Random(3)); game.Active = true; game.Jump();
            for (int i = 0; i < fps * 15 && game.Phase == RunnerPhase.Running; i++) game.Advance(1.0 / fps, 480);
            Assert(game.Phase == RunnerPhase.GameOver && game.Best > 0, "A grounded dinosaur must collide and record score.");
            if (baseline < 0) baseline = game.Distance;
            else Assert(Math.Abs(game.Distance - baseline) < 1, "Collision must not depend on frame rate.");
            int best = game.Best;
            Assert(game.Jump() && game.Score == 0 && game.Best == best && game.Obstacles.Count == 0, "Restart should clear course but keep best.");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (RunnerGame form = new RunnerGame())
        {
            RunnerSession game = (RunnerSession)typeof(RunnerGame).GetField("session", flags).GetValue(form);
            Assert(form.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom && form.Height == 100 && form.Width == Screen.PrimaryScreen.WorkingArea.Width / 2,
                "Runner must align to taskbar and use half the screen width.");
            game.Active = true; game.Jump();
            for (int i = 0; i < 350; i++) game.Advance(0.01, form.Width);
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(225,231,239));
                typeof(RunnerGame).GetMethod("OnPaint", flags).Invoke(form, new object[] { new PaintEventArgs(g, form.ClientRectangle) });
                bitmap.Save(Path.Combine(artifacts, "runner.png"), ImageFormat.Png);
            }
        }
        Console.WriteLine("PASS: runner pause, variable jump, collision at 30/60/144 FPS, restart, best score and rendered view.");
    }
}

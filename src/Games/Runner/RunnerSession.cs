using System;
using System.Collections.Generic;
using System.Drawing;

namespace TaskbarGames.Games.Runner
{
    public enum RunnerPhase { Ready, Running, GameOver }
    public sealed class RunnerObstacle
    {
        public double X;
        public int Width, Height;
    }
    public sealed class RunnerSession
    {
        readonly Random random;
        readonly List<RunnerObstacle> obstacles = new List<RunnerObstacle>();
        double velocity, spawnIn, accumulator, elapsed;
        public const double MaximumSpeed = 280;
        // Enough room for a full jump, landing and another press, even at maximum speed.
        public const double MinimumGap = MaximumSpeed * (2 * 290.0 / 850 + 0.2) + 24;
        public RunnerPhase Phase { get; private set; }
        public bool Active { get; set; }
        public double Height { get; private set; }
        public double Distance { get; private set; }
        public int Best { get; private set; }
        public int Score { get { return (int)(Distance / 10); } }
        public double Speed { get { return Math.Min(MaximumSpeed, 140 + elapsed * 1.5); } }
        public IList<RunnerObstacle> Obstacles { get { return obstacles.AsReadOnly(); } }
        public RunnerSession() : this(new Random()) { }
        public RunnerSession(Random source) { random = source; Phase = RunnerPhase.Ready; }
        public bool Jump()
        {
            if (!Active) return false;
            if (Phase != RunnerPhase.Running)
            {
                Phase = RunnerPhase.Running; Distance = Height = velocity = accumulator = elapsed = 0;
                obstacles.Clear(); spawnIn = 140;
            }
            if (Height > 0 || velocity > 0) return false;
            velocity = 290; return true;
        }
        public void ReleaseJump() { if (velocity > 135) velocity = 135; }
        public void ResetForLayout()
        { Best = Math.Max(Best, Score); Phase = RunnerPhase.Ready; obstacles.Clear(); Height = velocity = accumulator = Distance = elapsed = 0; }
        public void Advance(double seconds, int width)
        {
            if (!Active || Phase != RunnerPhase.Running || seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            accumulator += Math.Min(seconds, 0.25);
            const double dt = 1.0 / 240;
            while (accumulator >= dt && Phase == RunnerPhase.Running)
            {
                accumulator -= dt;
                Height += velocity * dt; velocity -= 850 * dt;
                if (Height <= 0) { Height = 0; velocity = 0; }
                elapsed += dt;
                double travel = Speed * dt;
                Distance += travel;
                spawnIn -= travel;
                if (spawnIn <= 0)
                {
                    int cactusWidth = random.Next(2) == 0 ? 11 : 20;
                    obstacles.Add(new RunnerObstacle { X = width + 12, Width = cactusWidth, Height = random.Next(17, 26) });
                    // Distance gaps do not shrink when speed ramps up later.
                    spawnIn += cactusWidth + MinimumGap + random.NextDouble() * 360;
                }
                for (int i = obstacles.Count - 1; i >= 0; i--)
                {
                    RunnerObstacle obstacle = obstacles[i]; obstacle.X -= travel;
                    if (obstacle.X + obstacle.Width < 0) { obstacles.RemoveAt(i); continue; }
                    RectangleF body = new RectangleF(46, (float)(-Height - 23), 15, 21);
                    RectangleF cactus = new RectangleF((float)obstacle.X + 2, -obstacle.Height + 2, obstacle.Width - 4, obstacle.Height - 2);
                    if (body.IntersectsWith(cactus)) { Phase = RunnerPhase.GameOver; Best = Math.Max(Best, Score); break; }
                }
            }
        }
    }
}

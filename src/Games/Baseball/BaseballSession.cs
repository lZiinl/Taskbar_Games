using System;

namespace TaskbarGames.Games.Baseball
{
    public enum BaseballPhase { Ready, Pitch, Flight, Result }

    public struct BattingResult
    {
        public bool Hit;
        public int Distance;
        public int Points;
        public string Grade;
        public int CarryDistance;
        public bool HomeRun, HitFence;
    }

    // Gameplay uses seconds rather than frame counts or screen-dependent distances.
    public sealed class BaseballSession
    {
        public const double MinPitchSeconds = 1.05;
        public const double MaxPitchSeconds = 1.65;
        public const double SwingContactDelay = 0.08;
        public const double SwingDuration = 0.46;
        public const double HitWindow = 0.12;
        public const double ReadySeconds = 0.85;
        public const double ResultSeconds = 1.4;
        public const int FenceDistance = 130;
        public const int FenceHeight = 18;
        public const int BallRadius = 3;
        readonly Random random;
        bool swung, contactResolved;
        double contactTime;

        public BaseballPhase Phase { get; private set; }
        public bool Active { get; private set; }
        public double Elapsed { get; private set; }
        public double PitchSeconds { get; private set; }
        public double SwingAge { get; private set; }
        public BattingResult LastHit { get; private set; }
        public long Score { get; private set; }
        public int BestDistance { get; private set; }
        public int Attempts { get; private set; }
        public int Hits { get; private set; }
        public double FlightSeconds { get { return 1.25 + LastHit.CarryDistance / 160.0; } }
        public bool HomeRunVisible { get {
            return LastHit.HomeRun && (Phase == BaseballPhase.Result ||
                (Phase == BaseballPhase.Flight && Elapsed / FlightSeconds >= FenceDistance / (double)LastHit.CarryDistance));
        } }

        public static double ArcHeight(int distance, double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double apex = 130 * (0.4 + distance / 160.0 * 0.6);
            return 30 * (1 - t) + 4 * apex * t * (1 - t);
        }

        // The same path drives drawing, fence clearance, and the final score.
        public double FlightDistance(double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double crossing = FenceDistance / (double)LastHit.CarryDistance;
            if (LastHit.HitFence && t >= crossing)
                return FenceDistance - 2 * (t - crossing) / (1 - crossing);
            return LastHit.CarryDistance * t;
        }

        public double FlightHeight(double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double crossing = FenceDistance / (double)LastHit.CarryDistance;
            if (LastHit.HitFence && t >= crossing)
                return ArcHeight(LastHit.CarryDistance, crossing) * (1 - t) / (1 - crossing);
            return ArcHeight(LastHit.CarryDistance, t);
        }

        public BaseballSession() : this(new Random()) { }
        public BaseballSession(Random randomSource)
        {
            if (randomSource == null) throw new ArgumentNullException("randomSource");
            random = randomSource;
            ResetRound();
        }

        public static BattingResult EvaluateTiming(double errorSeconds)
        {
            double error = Math.Abs(errorSeconds);
            if (Double.IsNaN(error) || error > HitWindow)
                return new BattingResult { Grade = errorSeconds < 0 ? "EARLY" : "LATE" };
            double quality = Math.Pow(1 - error / HitWindow, 1.5);
            int distance = (int)Math.Round(12 + 148 * quality);
            bool reachesFence = distance > FenceDistance;
            bool homeRun = reachesFence && ArcHeight(distance, FenceDistance / (double)distance) > FenceHeight + BallRadius;
            bool hitFence = reachesFence && !homeRun;
            int landing = hitFence ? FenceDistance - 2 : distance;
            return new BattingResult {
                Hit = true, CarryDistance = distance, Distance = landing, Points = landing * 10,
                HomeRun = homeRun, HitFence = hitFence,
                Grade = error <= 0.025 ? "PERFECT" : error <= 0.065 ? "GOOD" : "HIT"
            };
        }

        public void SetActive(bool active)
        {
            if (Active == active) return;
            Active = active;
            // Discard an unfinished pitch and queued swing when focus is lost.
            // An already-hit ball and its score animation can resume safely.
            if (!active)
            {
                SwingAge = -1;
                if (Phase == BaseballPhase.Ready || Phase == BaseballPhase.Pitch) ResetRound();
            }
        }

        public void ResetRound()
        {
            Phase = BaseballPhase.Ready;
            Elapsed = 0;
            swung = contactResolved = false;
            SwingAge = -1;
            LastHit = new BattingResult { Grade = "" };
        }

        public bool Swing()
        {
            if (!Active || Phase != BaseballPhase.Pitch || swung) return false;
            swung = true;
            SwingAge = 0;
            contactTime = Elapsed + SwingContactDelay;
            return true;
        }

        public bool Advance(double seconds)
        {
            if (!Active || seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return false;
            bool wasSwinging = SwingAge >= 0;
            if (SwingAge >= 0) SwingAge = SwingAge + seconds > SwingDuration ? -1 : SwingAge + seconds;
            Elapsed += seconds;
            switch (Phase)
            {
                case BaseballPhase.Ready:
                    if (Elapsed >= ReadySeconds)
                    {
                        PitchSeconds = MinPitchSeconds + random.NextDouble() * (MaxPitchSeconds - MinPitchSeconds);
                        Phase = BaseballPhase.Pitch;
                        Elapsed = 0;
                    }
                    return true;
                case BaseballPhase.Pitch:
                    if (swung && !contactResolved && Elapsed >= contactTime)
                    {
                        contactResolved = true;
                        LastHit = EvaluateTiming(contactTime - PitchSeconds);
                        if (LastHit.Hit)
                        {
                            Phase = BaseballPhase.Flight;
                            Elapsed = 0;
                            return true;
                        }
                    }
                    if (Elapsed >= PitchSeconds + 0.3)
                    {
                        if (!contactResolved) LastHit = new BattingResult { Grade = swung ? "LATE" : "MISS" };
                        CompleteAttempt();
                    }
                    return true;
                case BaseballPhase.Flight:
                    if (Elapsed >= FlightSeconds) CompleteAttempt();
                    return true;
                case BaseballPhase.Result:
                    if (Elapsed >= ResultSeconds) { ResetRound(); return true; }
                    return wasSwinging;
            }
            return false;
        }

        void CompleteAttempt()
        {
            Attempts++;
            if (LastHit.Hit)
            {
                Hits++;
                Score += LastHit.Points;
                BestDistance = Math.Max(BestDistance, LastHit.Distance);
            }
            Phase = BaseballPhase.Result;
            Elapsed = 0;
        }
    }
}

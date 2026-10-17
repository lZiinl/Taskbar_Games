using System;
using System.Drawing;

namespace TaskbarGames.Games.Fortress
{
    // 화면, 타이머, 파일 저장과 독립된 충전·탄도·명중·결과 상태 기계.
    public sealed class FortressSession
    {
        readonly Random random;
        double worldWidth;
        float ground;
        PointF muzzle;
        public PointF Projectile { get; private set; }
        public float TargetX { get; private set; }
        public double FlightTime { get; private set; }
        public double Speed { get; private set; }
        public double Gravity { get; private set; }
        public double Charge { get; private set; }
        public double ResultRemaining { get; private set; }
        public bool Charging { get; private set; }
        public bool Flying { get; private set; }
        public bool WasHit { get; private set; }
        public string Result { get; private set; }
        public int Hits { get; private set; }
        public int Shots { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public event Action<bool> ShotFinished;

        public FortressSession() : this(new Random(), 0, 0, 0) { }
        public FortressSession(Random source, int hits, int shots, int bestCombo)
        {
            if (source == null) throw new ArgumentNullException("source");
            random = source; Shots = Math.Max(0, shots); Hits = Math.Max(0, Math.Min(Shots, hits));
            BestCombo = Math.Max(0, Math.Min(Hits, bestCombo)); Result = "";
        }
        public void ResetLayout(double width, float floor, PointF origin, float minimumTarget)
        {
            worldWidth = width; ground = floor; muzzle = origin;
            Charging = Flying = WasHit = false; Charge = FlightTime = ResultRemaining = 0; Result = "";
            NewTarget(minimumTarget);
        }
        public void NewTarget(float minimumTarget)
        {
            float span = (float)worldWidth - 55;
            float left = Math.Max(55 + span * 0.10f, minimumTarget);
            float right = Math.Max(left, 55 + span * 0.80f);
            TargetX = left + (float)random.NextDouble() * (right - left);
        }
        public bool BeginCharge(float minimumTarget)
        {
            if (Charging || Flying || Result.Length > 0) return false;
            TargetX = Math.Max(TargetX, minimumTarget); Charging = true; Charge = 0; return true;
        }
        public void CancelCharge() { Charging = false; Charge = 0; }
        public bool ReleaseCharge()
        {
            if (!Charging) return false;
            Charging = false;
            double maxRange = Math.Max(1, worldWidth - muzzle.X);
            double range = Math.Min(70, maxRange) + Charge * (maxRange - Math.Min(70, maxRange));
            Speed = Ballistics.SpeedForRange(range, ground - muzzle.Y);
            Gravity = Ballistics.GravityForRange(range, ground - muzzle.Y);
            FlightTime = 0; Projectile = muzzle; Flying = true;
            if (Shots < Int32.MaxValue) Shots++;
            return true;
        }
        public bool Advance(double seconds, float minimumTarget)
        {
            if (seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return false;
            bool changed = false;
            // 결과 시간은 실제 경과 시간, 물리는 긴 UI 지연을 제한한 시간으로 진행한다.
            if (Result.Length > 0)
            {
                ResultRemaining -= seconds;
                if (ResultRemaining <= 0)
                {
                    if (WasHit) NewTarget(minimumTarget);
                    Result = ""; Charge = 0; ResultRemaining = 0; changed = true;
                }
            }
            double dt = Math.Min(0.05, seconds);
            if (Charging && Charge < 1) { Charge = Math.Min(1, Charge + dt / 2); changed = true; }
            if (Flying)
            {
                changed = true;
                int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
                for (int i = 0; i < steps && Flying; i++)
                {
                    FlightTime += dt / steps;
                    Projectile = Ballistics.Position(muzzle, Speed, FlightTime, Gravity);
                    double dx = Projectile.X - TargetX, dy = Projectile.Y - (ground - 19);
                    if (dx * dx + dy * dy <= 23 * 23) Finish(true);
                    else if (Projectile.Y >= ground || Projectile.X > worldWidth + 10) Finish(false);
                }
            }
            return changed;
        }
        void Finish(bool hit)
        {
            Flying = false; WasHit = hit;
            if (hit)
            {
                if (Hits < Int32.MaxValue) Hits++;
                if (Combo < Int32.MaxValue) Combo++;
                BestCombo = Math.Max(BestCombo, Combo);
            }
            else Combo = 0;
            Result = hit ? "HIT" : "MISS"; ResultRemaining = 1.2;
            if (ShotFinished != null) ShotFinished(hit);
        }
    }
}

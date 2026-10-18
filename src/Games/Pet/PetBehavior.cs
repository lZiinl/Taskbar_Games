using System;
using System.Collections.Generic;

namespace TaskbarGames.Games.Pet
{
    public enum PetAction { Idle, Walk, Sleep, Love, Eat, Cola, Popcorn, Read, WatchTV }
    public sealed class Acorn
    {
        public double X { get; internal set; }
        internal Acorn(double x) { X = x; }
    }

    // 좌표 범위는 호출자가 제공한다. 화면/입력/도토리 창/저장에 의존하지 않는다.
    public sealed class PetBehavior
    {
        readonly Random random;
        readonly List<Acorn> food = new List<Acorn>();
        readonly System.Collections.ObjectModel.ReadOnlyCollection<Acorn> foodView;
        bool attendingCare;
        double cooldown;
        double nextActivity = 15, walkRemaining;
        public static bool IsLeisure(PetAction action)
        { return action == PetAction.Cola || action == PetAction.Popcorn || action == PetAction.Read || action == PetAction.WatchTV; }
        public const int MaxAcorns = 5;
        public PetSession Stats { get; private set; }
        public PetAction Action { get; private set; }
        public double Remaining { get; private set; }
        public double X { get; private set; }
        public int Direction { get; private set; }
        public double JumpAge { get; private set; }
        public double JumpHeight { get { return JumpAge < 0 ? 0 : Math.Max(0, 220 * JumpAge - 400 * JumpAge * JumpAge); } }
        public IList<Acorn> Food { get { return foodView; } }
        public string Pose { get { return JumpAge >= 0 ? "jump" : Action.ToString().ToLowerInvariant(); } }
        public event Action<Acorn> Ate;

        public PetBehavior(PetSession stats, Random source, double startX)
        {
            if (stats == null || source == null) throw new ArgumentNullException();
            Stats = stats; random = source; X = startX; Direction = 1;
            Action = PetAction.Idle; Remaining = 120; JumpAge = -1; foodView = food.AsReadOnly();
        }
        public void SetBounds(double left, double right)
        {
            X = Clamp(X, left, Math.Max(left, right - 76));
            foreach (Acorn acorn in food) acorn.X = Clamp(acorn.X, left + 38, Math.Max(left + 38, right - 38));
        }
        static double Clamp(double x, double left, double right) { return Math.Max(left, Math.Min(right, x)); }
        public Acorn AddFood(double screenX, double left, double right)
        {
            if (food.Count >= MaxAcorns || Double.IsNaN(screenX) || Double.IsInfinity(screenX)) return null;
            Acorn acorn = new Acorn(Clamp(screenX, left + 38, Math.Max(left + 38, right - 38)));
            food.Add(acorn);
            if (Action != PetAction.Eat && !attendingCare) { Action = PetAction.Walk; Remaining = 30; }
            return acorn;
        }
        public bool Pet()
        {
            if (cooldown > 0 || Action == PetAction.Eat) return false;
            Stats.Pet(); cooldown = 1.5; StartCare(PetAction.Love, 4); return true;
        }
        public void ToggleSleep() { StartCare(Action == PetAction.Sleep ? PetAction.Idle : PetAction.Sleep, Action == PetAction.Sleep ? 120 : 300); }
        void StartCare(PetAction next, double seconds)
        {
            if (Action == PetAction.Eat) return;
            attendingCare = true; Action = next; Remaining = next == PetAction.Idle && food.Count > 0 ? 0 : seconds;
        }
        public bool Jump()
        {
            if (JumpAge >= 0 || Action == PetAction.Eat) return false;
            if (IsLeisure(Action)) ResumeWalk();
            if (Action == PetAction.Sleep) { Action = PetAction.Idle; Remaining = 120; attendingCare = false; }
            JumpAge = 0; return true;
        }
        public void CancelJump() { JumpAge = -1; }
        public void Advance(double dt, double left, double right)
        {
            if (dt <= 0 || Double.IsNaN(dt) || Double.IsInfinity(dt)) return;
            SetBounds(left, right);
            cooldown = Math.Max(0, cooldown - dt);
            if (JumpAge >= 0) { JumpAge += dt; if (JumpAge >= 0.55) JumpAge = -1; }
            Remaining -= dt;
            if (attendingCare && Remaining > 0) return;
            attendingCare = false;
            if (Action == PetAction.Eat && Remaining > 0) return;
            if (food.Count > 0)
            {
                Acorn nearest = food[0];
                foreach (Acorn candidate in food)
                    if (Math.Abs(candidate.X - X - 38) < Math.Abs(nearest.X - X - 38)) nearest = candidate;
                double distance = nearest.X - 38 - X;
                if (Math.Abs(distance) > 0.1) Direction = distance > 0 ? 1 : -1;
                X += Math.Sign(distance) * Math.Min(Math.Abs(distance), 36 * dt); Action = PetAction.Walk;
                if (Math.Abs(nearest.X - 38 - X) < 0.1 && JumpAge < 0)
                {
                    food.Remove(nearest); Action = PetAction.Eat; Remaining = 6; Stats.Feed();
                    if (Ate != null) Ate(nearest);
                }
                return;
            }
            if (IsLeisure(Action))
            {
                if (Remaining > 0) return;
                ResumeWalk();
            }
            if (Action == PetAction.Walk)
            {
                // 산책 중 잠깐 즐기는 간식과 취미는 돌봄 수치나 식사 횟수를 바꾸지 않는다.
                if (JumpAge < 0) nextActivity -= dt;
                if (JumpAge < 0 && nextActivity <= 0 && Remaining > 0)
                {
                    walkRemaining = Remaining;
                    Action = (PetAction)((int)PetAction.Cola + random.Next(4));
                    Remaining = Action == PetAction.Read || Action == PetAction.WatchTV ? 12 : 7;
                    return;
                }
                X += Direction * 26 * dt;
                if (X <= left || X >= right - 76) Direction = -Direction;
                SetBounds(left, right);
            }
            if (Remaining <= 0)
            {
                if (Action != PetAction.Idle) { Action = PetAction.Idle; Remaining = 60 + random.Next(121); }
                else if (random.Next(3) == 0) { Action = PetAction.Sleep; Remaining = 180 + random.Next(121); }
                else { Action = PetAction.Walk; Remaining = 120 + random.Next(121); Direction = random.Next(2) == 0 ? -1 : 1; }
            }
        }
        void ResumeWalk()
        {
            Action = PetAction.Walk; Remaining = Math.Max(10, walkRemaining);
            nextActivity = 12 + random.Next(17);
        }
    }
}

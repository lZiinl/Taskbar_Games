using System;

namespace TaskbarGames.Games.Pet
{
    public sealed class PetSession
    {
        public double Fullness { get; private set; }
        public double Mood { get; private set; }
        public int Meals { get; private set; }
        public PetSession() : this(75, 75, 0) { }
        public PetSession(double fullness, double mood, int meals)
        {
            Fullness = Clamp(fullness); Mood = Clamp(mood); Meals = Math.Max(0, meals);
        }
        static double Clamp(double value) { return Double.IsNaN(value) ? 75 : Math.Max(0, Math.Min(100, value)); }
        public void Advance(double seconds)
        {
            if (seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            Fullness = Clamp(Fullness - seconds / 30);
            Mood = Clamp(Mood - seconds / 45);
        }
        public void Feed() { Fullness = Clamp(Fullness + 22); Mood = Clamp(Mood + 5); if (Meals < int.MaxValue) Meals++; }
        public void Pet() { Mood = Clamp(Mood + 12); }
    }
}

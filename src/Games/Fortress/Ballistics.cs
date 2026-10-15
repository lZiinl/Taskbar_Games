using System;
using System.Drawing;

namespace TaskbarGames.Games.Fortress
{
    public static class Ballistics
    {
        public const double Gravity = 350;
        public const double Angle = 25 * Math.PI / 180;
        public static double GravityForRange(double range, double height)
        {
            // Preserve the landing point while taking exactly 90% of the previous
            // 20-degree trajectory's flight time, including the muzzle height.
            double previousRise = height + range * Math.Tan(Math.PI / 9);
            return Gravity * (height + range * Math.Tan(Angle)) / (previousRise * 0.9 * 0.9);
        }
        public static double SpeedForRange(double range, double height)
        {
            double gravity = GravityForRange(range, height);
            return Math.Sqrt(gravity * range * range / (2 * Math.Cos(Angle) * Math.Cos(Angle) * (height + range * Math.Tan(Angle))));
        }
        public static PointF Position(PointF start, double speed, double time, double gravity)
        {
            return new PointF((float)(start.X + speed * Math.Cos(Angle) * time),
                (float)(start.Y - speed * Math.Sin(Angle) * time + gravity * time * time / 2));
        }
    }

}

using System;
using System.Drawing;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Baseball
{
    // Upper-body swing with a planted, completely stationary lower body.
    // Motion reference: MLB Network's Rhys Hoskins swing analysis (2019-04-10).
    internal static class BatterAnimation
    {
        sealed class Pose
        {
            public double Time;
            public float Shoulder, HandX, HandY, BatAngle, Turn;
            public Pose(double time, float shoulder, float handX, float handY, float angle, float turn)
            {
                Time = time; Shoulder = shoulder; HandX = handX; HandY = handY;
                BatAngle = angle; Turn = turn;
            }
        }

        static readonly Pose[] Poses = {
            new Pose(0,     65, 66, -37, -115, 0),
            new Pose(0.04,  64, 63, -38, -130, 0),
            new Pose(0.09,  65, 65, -34, -155, 0.35f),
            new Pose(BaseballSession.SwingContactDelay, 68, 78, -30, 0, 0.75f),
            new Pose(0.22,  70, 85, -34, -25, 1),
            new Pose(0.34,  70, 76, -43, -125, 1),
            new Pose(0.49,  70, 70, -42, -165, 1),
            new Pose(BaseballSession.SwingDuration, 65, 66, -37, -115, 0)
        };
        static readonly string[] Head = {
            "..DDDDD...", ".DBBBBBD..", "DBBLLBBBDD", "DBBBBBBBBD",
            ".DTSSSSD..", ".DTSSDSSD.", "..DSSSSD..", "...DTTD..."
        };
        static readonly Color Ink = Color.FromArgb(25, 32, 45);
        static readonly Color Blue = Color.FromArgb(92, 159, 235);
        static readonly Color Shadow = Color.FromArgb(54, 109, 177);
        static readonly Color Skin = Color.FromArgb(246, 194, 144);
        static readonly Color Pants = Color.FromArgb(233, 239, 247);

        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        static Pose At(double age)
        {
            if (age < 0 || age >= BaseballSession.SwingDuration) return Poses[0];
            for (int i = 1; i < Poses.Length; i++)
                if (age <= Poses[i].Time)
                {
                    Pose a = Poses[i - 1], b = Poses[i];
                    float t = (float)((age - a.Time) / (b.Time - a.Time));
                    t = t * t * (3 - 2 * t);
                    return new Pose(age, Lerp(a.Shoulder,b.Shoulder,t),
                        Lerp(a.HandX,b.HandX,t), Lerp(a.HandY,b.HandY,t), Lerp(a.BatAngle,b.BatAngle,t),
                        Lerp(a.Turn,b.Turn,t));
                }
            return Poses[0];
        }

        static void Stroke(Graphics g, Color color, int width, params PointF[] points)
        {
            using (SolidBrush brush = new SolidBrush(color))
                for (int i = 1; i < points.Length; i++)
                {
                    PointF a = points[i - 1], b = points[i];
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.X-a.X), Math.Abs(b.Y-a.Y))));
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        int x = (int)Math.Round(Lerp(a.X,b.X,t) / 2) * 2;
                        int y = (int)Math.Round(Lerp(a.Y,b.Y,t) / 2) * 2;
                        g.FillRectangle(brush, x - width / 2, y - width / 2, width, width);
                    }
                }
        }

        static void Limb(Graphics g, Color color, int width, params PointF[] points)
        {
            Stroke(g, Ink, width + 2, points);
            Stroke(g, color, width, points);
        }

        public static void Draw(Graphics g, int ground, double age)
        {
            Pose p = At(age);
            PointF hip = new PointF(65, ground - 19);
            PointF rearKnee = new PointF(56, ground - 11);
            PointF rearAnkle = new PointF(52, ground - 3);
            PointF frontKnee = new PointF(75, ground - 11);
            PointF frontAnkle = new PointF(81, ground - 3);
            Limb(g, Color.FromArgb(187, 202, 218), 6, hip, rearKnee, rearAnkle);
            Limb(g, Pants, 6, hip, frontKnee, frontAnkle);
            Stroke(g, Ink, 4, rearAnkle, new PointF(60, ground - 2));
            Stroke(g, Ink, 4, frontAnkle, new PointF(87, ground - 2));

            PointF[] torso = {
                new PointF(p.Shoulder - 9 + p.Turn * 2, ground - 34),
                new PointF(p.Shoulder + 6, ground - 35),
                new PointF(72, ground - 18), new PointF(58, ground - 18)
            };
            using (SolidBrush fill = new SolidBrush(Blue)) g.FillPolygon(fill, torso);
            using (Pen outline = new Pen(Ink, 2)) g.DrawPolygon(outline, torso);
            Stroke(g, Shadow, 4, torso[0], torso[3]);
            Stroke(g, Ink, 2, new PointF(58, ground - 18), new PointF(72, ground - 18));
            Stroke(g, Color.FromArgb(180, 218, 250), 2,
                new PointF(p.Shoulder + 1 - p.Turn * 4, ground - 32), new PointF(66 - p.Turn * 4, ground - 22));
            // Keep the head steady over the torso instead of turning it with the bat.
            PixelArt.Sprite(g, Head, 57 + (int)Math.Round((p.Shoulder - 65) * 0.3), ground - 49, Blue);

            PointF hands = new PointF(p.HandX, ground + p.HandY);
            float extending = age >= BaseballSession.SwingContactDelay && age < 0.3 ? 1 : 0;
            PointF backShoulder = new PointF(p.Shoulder - 7, ground - 32);
            PointF backElbow = new PointF(Lerp(p.Shoulder - 9, p.HandX - 9, p.Turn), ground - 25 - extending * 3);
            PointF frontShoulder = new PointF(p.Shoulder + 5, ground - 33);
            PointF frontElbow = new PointF(Lerp(p.Shoulder + 10, p.HandX - 3, p.Turn), ground - 26 - extending * 5);
            Limb(g, Skin, 4, backShoulder, backElbow, hands);
            Limb(g, Skin, 4, frontShoulder, frontElbow, hands);

            double radians = p.BatAngle * Math.PI / 180;
            PointF tip = new PointF(hands.X + (float)Math.Cos(radians) * 30, hands.Y + (float)Math.Sin(radians) * 30);
            PointF barrel = new PointF(Lerp(hands.X,tip.X,0.32f), Lerp(hands.Y,tip.Y,0.32f));
            Stroke(g, Ink, 6, hands, tip);
            Stroke(g, Color.FromArgb(165, 112, 68), 2, hands, barrel);
            Stroke(g, Color.FromArgb(229, 181, 111), 4, barrel, tip);
            using (SolidBrush glove = new SolidBrush(Pants)) g.FillRectangle(glove, (int)hands.X-3, (int)hands.Y-2, 5, 4);
        }
    }
}

// 하체는 고정하고 상체·팔·배트만 키프레임 보간하는 픽셀 타자 애니메이션.
// 규칙 모델의 SwingContactDelay/SwingDuration을 공유해 시각적 타격과 판정 시점을 맞춘다.

using System;
using System.Drawing;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Baseball
{
    // Upper-body swing with a planted, completely stationary lower body.
    // Motion reference: MLB Network's Rhys Hoskins swing analysis (2019-04-10).
    internal static class BatterAnimation
    {
        // 키프레임 하나. Time은 초, HandY는 지면 기준 상대 높이, BatAngle은 도 단위다.
        // Turn은 몸통 회전 정도를 나타내는 연출용 보간 값이다.
        sealed class Pose
        {
            public double Time;
            public float Shoulder, HandX, HandY, BatAngle, Turn;
            // 관절·회전 값을 묶어 저장한다. 보간 중에도 같은 구조를 만들어 Draw에 전달한다.
            public Pose(double time, float shoulder, float handX, float handY, float angle, float turn)
            {
                Time = time; Shoulder = shoulder; HandX = handX; HandY = handY;
                BatAngle = angle; Turn = turn;
            }
        }

        // 준비 → 뒤로 당기기 → 가속 → 접촉 → 팔로스루 → 복귀 순서다.
        // 키프레임 시간은 반드시 증가해야 하며, 접촉 프레임은 모델 상수를 사용한다.
        static readonly Pose[] Poses = {
            new Pose(0,     65, 66, -37, -115, 0),
            new Pose(0.02,  64, 63, -38, -130, 0),
            new Pose(0.045, 65, 65, -34, -155, 0.35f),
            new Pose(BaseballSession.SwingContactDelay, 68, 78, -30, 0, 0.75f),
            new Pose(0.13,  70, 85, -34, -25, 1),
            new Pose(0.22,  70, 76, -43, -125, 1),
            new Pose(0.32,  70, 70, -42, -165, 1),
            new Pose(BaseballSession.SwingDuration, 65, 66, -37, -115, 0)
        };
        static readonly string[] Head = {
            "..DDDDD...", ".DBBBBBD..", "DBBLLBBBDD", "DBBBBBBBBD",
            ".DTSSSSD..", ".DTSSDSSD.", "..DSSSSD..", "...DTTD..."
        };
        static readonly Color Ink = Color.FromArgb(25, 32, 45);
        static readonly Color Skin = Color.FromArgb(246, 194, 144);
        static readonly Color Pants = Color.FromArgb(233, 239, 247);

        // 두 스칼라 값을 t 비율로 선형 보간한다. 곡선형 시간 보정은 At에서 먼저 적용한다.
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        // 음수 또는 종료 이후는 기본 자세다. 현재 시각을 둘러싼 두 키프레임을 찾아 보간한다.
        // smoothstep(t²(3-2t))을 적용해 각 구간 시작·끝에서 움직임이 급하게 꺾이는 것을 줄인다.
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

        // 선분을 작은 정사각형 연속으로 찍어 픽셀 선을 만든다.
        // 좌표를 2픽셀 단위로 반올림하여 일반 안티앨리어싱 선 대신 일관된 도트 질감을 유지한다.
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

        // 조금 두꺼운 어두운 선을 먼저 그리고 안쪽 색 선을 겹쳐 팔다리 윤곽을 만든다.
        static void Limb(Graphics g, Color color, int width, params PointF[] points)
        {
            Stroke(g, Ink, width + 2, points);
            Stroke(g, color, width, points);
        }

        // 고관절·무릎·발목은 고정 좌표이며 상체만 현재 Pose에 따라 움직인다.
        // 손에서 배트 각도 방향으로 길이 30의 끝점을 구하고 손잡이/배럴/글러브를 덧그린다.
        public static void Draw(Graphics g, int ground, double age)
        { Draw(g, ground, age, TeamUniform.DefaultBatter); }
        public static void Draw(Graphics g, int ground, double age, TeamUniform uniform)
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
            using (SolidBrush fill = new SolidBrush(uniform.Jersey)) g.FillPolygon(fill, torso);
            if (uniform.Pinstripes)
            {
                System.Drawing.Drawing2D.GraphicsState clip = g.Save();
                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddPolygon(torso); g.SetClip(path);
                    using (Pen stripe = new Pen(uniform.Cap))
                        for (int x = 54; x < 80; x += 4) g.DrawLine(stripe, x, ground - 36, x, ground - 18);
                }
                g.Restore(clip);
            }
            using (Pen outline = new Pen(Ink, 2)) g.DrawPolygon(outline, torso);
            Stroke(g, uniform.Cap, 4, torso[0], torso[3]);
            Stroke(g, Ink, 2, new PointF(58, ground - 18), new PointF(72, ground - 18));
            Stroke(g, uniform.Accent, 2,
                new PointF(p.Shoulder + 1 - p.Turn * 4, ground - 32), new PointF(66 - p.Turn * 4, ground - 22));
            // Keep the head steady over the torso instead of turning it with the bat.
            uniform.DrawSprite(g, Head, 57 + (int)Math.Round((p.Shoulder - 65) * 0.3), ground - 49, true);

            PointF hands = new PointF(p.HandX, ground + p.HandY);
            float extending = age >= BaseballSession.SwingContactDelay && age < 0.2 ? 1 : 0;
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

// 포트리스의 고정 발사각 탄도 계산. 화면 그리기와 충돌 검사에서 같은 함수를 사용한다.
// 좌표 단위는 게임의 논리 픽셀, 시간은 초이며 화면 Y축은 아래쪽이 양수다.

using System;
using System.Drawing;

namespace TaskbarGames.Games.Fortress
{
    public static class Ballistics
    {
        // 기준 중력. 실제 발사에서는 목표 거리와 포구 높이에 따른 GravityForRange 결과를 사용한다.
        public const double Gravity = 350;
        // 25도를 삼각함수 입력인 라디안으로 변환한 고정 발사각이다.
        public const double Angle = 25 * Math.PI / 180;
        // range는 수평 도달 거리, height는 포구와 지면의 높이 차다.
        // 기존 20도 궤적보다 비행 시간을 0.9배로 줄이면서 착지점을 유지하도록 중력을 보정한다.
        // 시간의 제곱에 비례하는 수직 운동 때문에 분모에 0.9²가 들어간다.
        public static double GravityForRange(double range, double height)
        {
            // Preserve the landing point while taking exactly 90% of the previous
            // 20-degree trajectory's flight time, including the muzzle height.
            double previousRise = height + range * Math.Tan(Math.PI / 9);
            return Gravity * (height + range * Math.Tan(Angle)) / (previousRise * 0.9 * 0.9);
        }
        // 지면 착지 조건을 포물선 운동식에 대입해 필요한 초기 속력을 역산한다.
        // 호출자는 양수인 거리와 포구 높이를 제공해야 한다. 임의의 음수 입력을 보정하는 함수는 아니다.
        public static double SpeedForRange(double range, double height)
        {
            double gravity = GravityForRange(range, height);
            return Math.Sqrt(gravity * range * range / (2 * Math.Cos(Angle) * Math.Cos(Angle) * (height + range * Math.Tan(Angle))));
        }
        // x = x0 + v cos(각도)t, y = y0 - v sin(각도)t + gt²/2.
        // Y축이 아래로 증가하므로 발사 직후 위로 향하는 속도 항에는 음수가 붙는다.
        public static PointF Position(PointF start, double speed, double time, double gravity)
        {
            return new PointF((float)(start.X + speed * Math.Cos(Angle) * time),
                (float)(start.Y - speed * Math.Sin(Angle) * time + gravity * time * time / 2));
        }
    }

}

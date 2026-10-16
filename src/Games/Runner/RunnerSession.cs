// 공룡 러너의 화면과 독립된 물리·생성·점수 모델.
// UI 타이머 간격이 달라도 누적 시간을 240Hz 고정 단계로 소비하여 충돌 결과의 차이를 줄인다.

using System;
using System.Collections.Generic;
using System.Drawing;

namespace TaskbarGames.Games.Runner
{
    // Ready는 시작 대기, Running은 이동/점프 진행, GameOver는 충돌 후 재시작 대기다.
    public enum RunnerPhase { Ready, Running, GameOver }
    // 장애물의 왼쪽 X와 크기를 논리 픽셀로 보관한다. 지면 높이는 0으로 가정한다.
    public sealed class RunnerObstacle
    {
        public double X;
        public int Width, Height;
    }
    public sealed class RunnerSession
    {
        readonly Random random;
        readonly List<RunnerObstacle> obstacles = new List<RunnerObstacle>();
        // velocity는 위쪽 양수인 수직 속도(px/s), spawnIn은 다음 생성까지 남은 이동 거리(px)다.
        // accumulator는 아직 물리에 반영하지 않은 초, elapsed는 이번 주행에 반영한 총 시간이다.
        double velocity, spawnIn, accumulator, elapsed;
        // 기본 속도의 4배까지 180초에 걸쳐 선형 가속한다. 일시 정지 시 elapsed가 늘지 않는다.
        public const double InitialSpeed = 140;
        public const double MaximumSpeed = InitialSpeed * 4;
        public const double AccelerationSeconds = 180;
        // Enough room for a full jump, landing and another press, even at maximum speed.
        // 최대 점프의 왕복 시간 2v/g에 재입력 여유 0.2초와 여백 24px를 더한다.
        // 최고 속도로 산정하므로 앞서 생성한 장애물도 이후 가속 때문에 간격이 부족해지지 않는다.
        public const double MinimumGap = MaximumSpeed * (2 * 290.0 / 850 + 0.2) + 24;
        public RunnerPhase Phase { get; private set; }
        public bool Active { get; set; }
        // 지면으로부터 올라간 높이. 화면에 그릴 때는 ground - Height로 Y축 방향을 뒤집는다.
        public double Height { get; private set; }
        public double Distance { get; private set; }
        public int Best { get; private set; }
        // 이동 10픽셀당 1점. Best는 이 객체 안에서 재시작할 때만 유지되며 파일에 저장하지 않는다.
        public int Score { get { return (int)(Distance / 10); } }
        public double Speed { get { return InitialSpeed + (MaximumSpeed - InitialSpeed) * Math.Min(1, elapsed / AccelerationSeconds); } }
        // 목록 추가/삭제는 모델만 수행하도록 읽기 전용 래퍼를 제공한다.
        // 각 장애물 자체는 참조 객체이므로 렌더러는 필드를 읽기만 해야 한다.
        public IList<RunnerObstacle> Obstacles { get { return obstacles.AsReadOnly(); } }
        public RunnerSession() : this(new Random()) { }
        // 고정 시드 Random을 주입하면 테스트에서 동일한 장애물 순서를 재현할 수 있다.
        public RunnerSession(Random source) { random = source; Phase = RunnerPhase.Ready; }
        // 비활성 입력은 무시한다. 대기/충돌 상태라면 점수·장애물·가속 시간을 초기화하고 시작한다.
        // 공중이거나 첫 물리 프레임 전이라도 위로 향하는 속도가 있으면 이중 점프를 막는다.
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
        // 상승 중 키를 빨리 떼면 상승 속도를 줄여 낮은 점프를 만든다. 하강 속도는 바꾸지 않는다.
        public void ReleaseJump() { if (velocity > 135) velocity = 135; }
        // 화면 크기 변경으로 현재 코스를 유지할 수 없을 때 최고 점수만 보존하고 대기 상태로 되돌린다.
        public void ResetForLayout()
        { Best = Math.Max(Best, Score); Phase = RunnerPhase.Ready; obstacles.Clear(); Height = velocity = accumulator = Distance = elapsed = 0; }
        // seconds는 초, width는 현재 플레이 영역의 폭이다. 활성 주행에서만 시뮬레이션한다.
        // 긴 시스템 지연은 한 호출당 0.25초로 제한하므로 순간 이동 대신 일부 시간이 생략된다.
        public void Advance(double seconds, int width)
        {
            if (!Active || Phase != RunnerPhase.Running || seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            accumulator += Math.Min(seconds, 0.25);
            // 타이머 한 번에 여러 물리 단계를 실행할 수 있다. 남은 소수 시간은 다음 호출로 넘긴다.
            // 작은 단계는 고속 장애물이 충돌 영역을 한 번에 건너뛰는 현상을 줄인다.
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
                    // 작은/큰 선인장과 높이를 무작위로 고른다. 생성 지점은 창 오른쪽 12픽셀 바깥이다.
                    int cactusWidth = random.Next(2) == 0 ? 11 : 20;
                    obstacles.Add(new RunnerObstacle { X = width + 12, Width = cactusWidth, Height = random.Next(17, 26) });
                    // Distance gaps do not shrink when speed ramps up later.
                    // 앞 장애물의 너비까지 포함해 다음 장애물의 왼쪽 좌표 간 거리를 정한다.
                    // +=로 임계값을 지나친 이동 거리도 보존하고, 0~360px를 더해 리듬을 다양하게 만든다.
                    spawnIn += cactusWidth + MinimumGap + random.NextDouble() * 360;
                }
                for (int i = obstacles.Count - 1; i >= 0; i--)
                {
                    RunnerObstacle obstacle = obstacles[i]; obstacle.X -= travel;
                    if (obstacle.X + obstacle.Width < 0) { obstacles.RemoveAt(i); continue; }
                    // 스프라이트 전체보다 작은 몸통과 선인장 내부 상자로 충돌을 검사한다.
                    // 그림의 꼬리·가장자리까지 꽉 채운 판정보다 플레이어에게 약간의 여유를 준다.
                    RectangleF body = new RectangleF(46, (float)(-Height - 23), 15, 21);
                    RectangleF cactus = new RectangleF((float)obstacle.X + 2, -obstacle.Height + 2, obstacle.Width - 4, obstacle.Height - 2);
                    if (body.IntersectsWith(cactus)) { Phase = RunnerPhase.GameOver; Best = Math.Max(Best, Score); break; }
                }
            }
        }
    }
}

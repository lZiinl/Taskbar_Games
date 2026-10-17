// 야구 규칙의 상태 기계. Ready → Pitch → Flight(명중 시) → Result → Ready 순으로 진행한다.
// 시간은 초, 타구 거리는 미터이며 렌더링 프레임 수나 화면 해상도에 의존하지 않는다.

using System;

namespace TaskbarGames.Games.Baseball
{
    // 투수 준비, 투구, 타구 비행, 결과 표시의 네 단계다. 헛스윙은 Flight 없이 Result로 넘어간다.
    public enum BaseballPhase { Ready, Pitch, Flight, Result }

    // 한 번의 타격 판정 결과. CarryDistance는 방해물이 없을 때 거리, Distance는 펜스 반영 후 실제 착지 거리다.
    // 점수는 실제 거리로 계산하고 HomeRun/HitFence는 그리기와 결과 처리에서 함께 사용한다.
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
        // 투구마다 이 범위에서 도착 시간을 무작위 선택한다. 값이 작을수록 공이 빨리 도착한다.
        public const double MinPitchSeconds = 1.05;
        public const double MaxPitchSeconds = 1.65;
        // 입력 즉시 타격하지 않고 0.08초 뒤 배트 접촉을 예약한다. 애니메이션의 접촉 키프레임과 공유한다.
        public const double SwingContactDelay = 0.08;
        public const double SwingDuration = 0.46;
        // 공 도착 시각과 배트 접촉 시각의 절대 차이가 이 범위를 넘으면 헛스윙이다.
        public const double HitWindow = 0.12;
        public const double ReadySeconds = 0.85;
        public const double ResultSeconds = 1.4;
        public const int FenceDistance = 130;
        public const int FenceHeight = 18;
        public const int BallRadius = 3;
        readonly Random random;
        // swung은 한 투구당 입력 1회를 보장하고 contactResolved는 예약된 접촉 판정의 중복 실행을 막는다.
        bool swung, contactResolved;
        // 현재 투구 단계 기준의 배트 접촉 예정 시각. 전역 Stopwatch 값이 아니다.
        double contactTime;

        public BaseballPhase Phase { get; private set; }
        public bool Active { get; private set; }
        public double Elapsed { get; private set; }
        public double PitchSeconds { get; private set; }
        // 스윙 시작 후 경과 초. -1은 스윙하지 않는 기본 자세를 의미한다.
        public double SwingAge { get; private set; }
        public BattingResult LastHit { get; private set; }
        public long Score { get; private set; }
        public int BestDistance { get; private set; }
        public int Attempts { get; private set; }
        public int Hits { get; private set; }
        // 먼 타구일수록 비행 연출 시간이 길다. 충돌 후 실제 거리 대신 원래 타구 거리를 기준으로 한다.
        public double FlightSeconds { get { return 1.25 + LastHit.CarryDistance / 160.0; } }
        // 홈런 가능 판정만으로 즉시 표시하지 않고 공이 실제 펜스 위치까지 진행한 후에 표시한다.
        public bool HomeRunVisible { get {
            return LastHit.HomeRun && (Phase == BaseballPhase.Result ||
                (Phase == BaseballPhase.Flight && Elapsed / FlightSeconds >= FenceDistance / (double)LastHit.CarryDistance));
        } }

        // 진행률을 0~1로 제한하고 시작 높이 30에서 지면 0으로 내려오는 포물선 형태를 계산한다.
        // 비거리별 최고점 변화는 시각적 연출을 위한 식이며, 펜스 판정도 이 식을 그대로 사용한다.
        public static double ArcHeight(int distance, double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double apex = 130 * (0.4 + distance / 160.0 * 0.6);
            return 30 * (1 - t) + 4 * apex * t * (1 - t);
        }

        // The same path drives drawing, fence clearance, and the final score.
        // 펜스에 맞기 전에는 원래 비거리를 따라 이동한다.
        // 맞은 뒤에는 펜스 위치에서 안쪽 2m 지점으로 후퇴하여 실제 착지 거리에 수렴한다.
        public double FlightDistance(double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double crossing = FenceDistance / (double)LastHit.CarryDistance;
            if (LastHit.HitFence && t >= crossing)
                return FenceDistance - 2 * (t - crossing) / (1 - crossing);
            return LastHit.CarryDistance * t;
        }

        // 펜스 충돌 뒤에는 충돌 순간 높이에서 지면까지 내려간다.
        // 화면과 판정이 다른 궤적을 쓰지 않도록 ArcHeight를 공통으로 호출한다.
        public double FlightHeight(double progress)
        {
            double t = Math.Max(0, Math.Min(1, progress));
            double crossing = FenceDistance / (double)LastHit.CarryDistance;
            if (LastHit.HitFence && t >= crossing)
                return ArcHeight(LastHit.CarryDistance, crossing) * (1 - t) / (1 - crossing);
            return ArcHeight(LastHit.CarryDistance, t);
        }

        // 실사용 난수 생성자를 사용한다. 테스트는 고정 시드 Random을 아래 생성자에 주입한다.
        public BaseballSession() : this(new Random()) { }
        // null 난수 공급자를 거부하고 첫 투구를 준비 상태로 초기화한다.
        public BaseballSession(Random randomSource) : this(randomSource, 0, 0, 0, 0) { }
        public BaseballSession(Random randomSource, int best, long score, int attempts, int hits)
        {
            BestDistance = Math.Max(0, Math.Min(160, best)); Score = Math.Max(0, score);
            Attempts = Math.Max(0, attempts); Hits = Math.Max(0, Math.Min(Attempts, hits));
            if (randomSource == null) throw new ArgumentNullException("randomSource");
            random = randomSource;
            ResetRound();
        }

        // 음수는 빠른 접촉, 양수는 늦은 접촉이다. 유효 오차 범위 안에서는 1.5승 곡선으로 정확도를 거리로 변환한다.
        // 펜스는 공의 중심만이 아니라 반지름까지 완전히 넘어야 홈런이며, 막히면 착지 거리를 128m로 보정한다.
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

        // 포커스 상실 시 진행 중인 투구와 예약 스윙은 버린다.
        // 이미 타격한 공의 비행/결과는 유지하고 Advance가 비활성 시간을 반영하지 않게 한다.
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

        // 이번 공의 상태만 초기화한다. 누적 점수·최고 거리·시도/안타 횟수는 다음 투구에도 유지한다.
        public void ResetRound()
        {
            Phase = BaseballPhase.Ready;
            Elapsed = 0;
            swung = contactResolved = false;
            SwingAge = -1;
            LastHit = new BattingResult { Grade = "" };
        }

        // 활성 투구 중 첫 입력만 접수한다. 현재 투구 시각에 접촉 지연을 더해 판정을 예약한다.
        // 반환값은 UI가 스윙 시작으로 인한 다시 그리기를 요청할지 결정하는 데 쓰인다.
        public bool Swing()
        {
            if (!Active || Phase != BaseballPhase.Pitch || swung) return false;
            swung = true;
            SwingAge = 0;
            contactTime = Elapsed + SwingContactDelay;
            return true;
        }

        // 유효한 활성 시간만 진행한다. 반환값은 상태/연출 변화로 다시 그리기가 필요한지 여부다.
        // 단계가 바뀌면 Elapsed를 0으로 재설정하며, 해당 호출의 초과 시간은 다음 단계로 이월하지 않는다.
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
                    // 이 프레임에서 예약 시각을 지났어도 판정에는 예약된 contactTime을 사용한다.
                    // 그려지는 프레임의 지연이 타격 정확도를 바꾸지 않도록 한다.
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

        // 결과 단계 진입 시 한 번만 시도 횟수와 점수를 확정한다.
        // 비행 중 예상 점수가 누적되지 않도록 명중 처리와 점수 반영 시점을 분리한다.
        public event Action AttemptCompleted;
        void CompleteAttempt()
        {
            if (Attempts < Int32.MaxValue) Attempts++;
            if (LastHit.Hit)
            {
                if (Hits < Int32.MaxValue) Hits++;
                Score = Score > Int64.MaxValue - LastHit.Points ? Int64.MaxValue : Score + LastHit.Points;
                BestDistance = Math.Max(BestDistance, LastHit.Distance);
            }
            Phase = BaseballPhase.Result;
            Elapsed = 0;
            if (AttemptCompleted != null) AttemptCompleted();
        }
    }
}

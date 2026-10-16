// 다람쥐의 수치 규칙만 담당하는 모델. 창·마우스·저장 파일에는 의존하지 않는다.
// 오프라인 시간은 계산하지 않고 호출자가 전달한 실행 중 경과 시간만 반영한다.

using System;

namespace TaskbarGames.Games.Pet
{
    public sealed class PetSession
    {
        // 포만감과 기분은 0~100의 실수로 유지하여 작은 시간 간격의 감소도 누적한다.
        // Meals는 먹이를 실제로 먹었을 때만 증가하며, 배치만 한 도토리는 포함하지 않는다.
        public double Fullness { get; private set; }
        public double Mood { get; private set; }
        public int Meals { get; private set; }
        // 새 펫의 초기 상태. 저장 복원은 아래의 값 전달 생성자를 사용한다.
        public PetSession() : this(75, 75, 0) { }
        // 파일에서 복원한 값도 정상 범위로 제한한다. 음수 섭취 횟수는 0으로 보정한다.
        public PetSession(double fullness, double mood, int meals)
        {
            Fullness = Clamp(fullness); Mood = Clamp(mood); Meals = Math.Max(0, meals);
        }
        // NaN은 기본값 75로 복구하고 나머지는 0~100으로 제한한다. 무한대도 양 끝값으로 제한된다.
        static double Clamp(double value) { return Double.IsNaN(value) ? 75 : Math.Max(0, Math.Min(100, value)); }
        // 음수·0·NaN·무한대 시간은 무시한다.
        // 포만감은 30초당 1, 기분은 45초당 1 감소하지만 0 아래로 내려가지 않는다.
        public void Advance(double seconds)
        {
            if (seconds <= 0 || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            Fullness = Clamp(Fullness - seconds / 30);
            Mood = Clamp(Mood - seconds / 45);
        }
        // 도착 후 섭취 확정 시 호출한다. 상한을 적용하고 횟수 오버플로를 방지한다.
        public void Feed() { Fullness = Clamp(Fullness + 22); Mood = Clamp(Mood + 5); if (Meals < int.MaxValue) Meals++; }
        // 쓰다듬기 보상. 입력 재사용 시간과 먹는 중 차단 여부는 PetGame에서 결정한다.
        public void Pet() { Mood = Clamp(Mood + 12); }
    }
}

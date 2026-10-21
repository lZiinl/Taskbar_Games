using System;
using System.Collections.Generic;
using System.Globalization;

namespace TaskbarGames.Shared
{
    public enum GameMode { Practice, Challenge }
    public enum GameKind { Fortress, Baseball, Runner }

    public sealed class GameRecord
    {
        public long BestScore;
        public int BestHits, BestHomeRuns, FinishedRuns, Clears;
        public double BestSeconds;
        public DateTime? BestScoreAtUtc, BestHitsAtUtc, BestHomeRunsAtUtc, BestSecondsAtUtc;
        public void Normalize()
        {
            BestScore = Math.Max(0, BestScore); BestHits = Math.Max(0, BestHits); BestHomeRuns = Math.Max(0, BestHomeRuns);
            FinishedRuns = Math.Max(0, FinishedRuns); Clears = Math.Max(0, Math.Min(FinishedRuns, Clears));
            if (Double.IsNaN(BestSeconds) || Double.IsInfinity(BestSeconds)) BestSeconds = 0;
            BestSeconds = Math.Max(0, BestSeconds);
        }
    }
    public sealed class TitleUnlock
    {
        public string Id = "";
        public DateTime EarnedAtUtc;
    }
    public static class RecordTime
    {
        public static string Format(DateTime? utc)
        { return !utc.HasValue || utc.Value == DateTime.MinValue ? "기록 없음" : utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
        public static DateTime? TitleEarned(GameData data, string id)
        {
            foreach (TitleUnlock unlock in data.TitleUnlocks) if (unlock.Id == id) return unlock.EarnedAtUtc;
            return null;
        }
    }
    public sealed class RunStats
    {
        public long Score;
        public int Attempts, Hits, HomeRuns, BestCombo, BestDistance;
        public double Seconds;
        public bool Ended, Cleared;
    }
    public sealed class Achievement
    {
        public readonly string Id, Name, Description;
        public readonly GameKind Game;
        readonly Func<RunStats, bool> condition;
        public Achievement(string id, string name, string description, GameKind game, Func<RunStats, bool> check)
        { Id = id; Name = name; Description = description; Game = game; condition = check; }
        public bool Matches(GameKind game, GameMode mode, RunStats stats)
        { return game == Game && mode == GameMode.Challenge && condition(stats); }
    }
    public static class Achievements
    {
        public static readonly Achievement[] All = {
            new Achievement("fortress.first", "첫 포성", "도전 한 판에서 첫 명중", GameKind.Fortress, s => s.Hits >= 1),
            new Achievement("fortress.1000", "견습 포격수", "도전 한 판 1,000점 달성", GameKind.Fortress, s => s.Score >= 1000),
            new Achievement("fortress.5000", "정밀 포격수", "도전 한 판 5,000점 달성", GameKind.Fortress, s => s.Score >= 5000),
            new Achievement("fortress.10000", "전장의 지배자", "도전 한 판 10,000점 달성", GameKind.Fortress, s => s.Score >= 10000),
            new Achievement("fortress.ten", "백발백중", "도전 한 판에서 10발 모두 명중", GameKind.Fortress, s => s.Hits >= 10),
            new Achievement("baseball.1000", "타석의 신예", "도전 한 판 1,000점 달성", GameKind.Baseball, s => s.Score >= 1000),
            new Achievement("baseball.5000", "안타 제조기", "도전 한 판 5,000점 달성", GameKind.Baseball, s => s.Score >= 5000),
            new Achievement("baseball.10000", "전설의 4번 타자", "도전 한 판 10,000점 달성", GameKind.Baseball, s => s.Score >= 10000),
            new Achievement("baseball.ten", "빈틈없는 타격", "도전 한 판에서 10안타", GameKind.Baseball, s => s.Hits >= 10),
            new Achievement("baseball.firsthr", "담장 너머로", "도전 한 판에서 첫 홈런", GameKind.Baseball, s => s.HomeRuns >= 1),
            new Achievement("baseball.tenhr", "홈런의 제왕", "도전 한 판에서 10홈런", GameKind.Baseball, s => s.HomeRuns >= 10),
            new Achievement("runner.clear", "한계 돌파자", "최고 속도에서 120초 생존", GameKind.Runner, s => s.Cleared && s.Seconds >= 120)
        };
        public static Achievement Find(string id) { foreach (Achievement a in All) if (a.Id == id) return a; return null; }
        public static string Equipped(GameData data, GameKind game)
        { return game == GameKind.Fortress ? data.FortressTitle : game == GameKind.Baseball ? data.BaseballTitle : data.RunnerTitle; }
        public static bool Equip(GameData data, GameKind game, string id)
        {
            Achievement a = Find(id);
            if (id != "" && (a == null || a.Game != game || !data.UnlockedTitles.Contains(id))) return false;
            if (game == GameKind.Fortress) data.FortressTitle = id;
            else if (game == GameKind.Baseball) data.BaseballTitle = id;
            else data.RunnerTitle = id;
            return true;
        }
        public static GameRecord Record(GameData data, GameKind game, GameMode mode)
        {
            if (game == GameKind.Fortress) return mode == GameMode.Challenge ? data.FortressChallenge : data.FortressPractice;
            if (game == GameKind.Baseball) return mode == GameMode.Challenge ? data.BaseballChallenge : data.BaseballPractice;
            return mode == GameMode.Challenge ? data.RunnerChallenge : data.RunnerPractice;
        }
    }

    // 한 인스턴스가 한 판을 담당한다. 재보고·종료 저장이 누적 기록이나 업적을 중복 지급하지 않는다.
    public sealed class ProgressTracker
    {
        readonly GameStore store;
        readonly GameKind game;
        readonly GameMode mode;
        readonly Func<DateTime> utcNow;
        long previousScore;
        int previousAttempts, previousHits;
        bool ended;
        readonly List<string> newTitles = new List<string>();
        public IList<string> NewTitles { get { return newTitles.AsReadOnly(); } }
        public ProgressTracker(GameStore storage, GameKind kind, GameMode gameMode) : this(storage, kind, gameMode, () => DateTime.UtcNow) { }
        public ProgressTracker(GameStore storage, GameKind kind, GameMode gameMode, Func<DateTime> clock)
        { store = storage; game = kind; mode = gameMode; utcNow = clock; }
        static int Add(int value, int delta) { return (int)Math.Min(Int32.MaxValue, (long)value + Math.Max(0, delta)); }
        public void Record(RunStats stats)
        {
            if (mode == GameMode.Challenge && OnlineSession.Current != null && OnlineSession.Current.game == game.ToString())
                OnlineSession.Current.Capture(stats);
            GameData data = store.Data;
            DateTime now = utcNow().ToUniversalTime();
            GameRecord record = Achievements.Record(data, game, mode);
            // 동점이나 종료 시 재저장은 최초 달성 일시를 바꾸지 않는다.
            if (stats.Score > record.BestScore) { record.BestScore = stats.Score; record.BestScoreAtUtc = now; }
            if (stats.Hits > record.BestHits) { record.BestHits = stats.Hits; record.BestHitsAtUtc = now; }
            if (stats.HomeRuns > record.BestHomeRuns) { record.BestHomeRuns = stats.HomeRuns; record.BestHomeRunsAtUtc = now; }
            if (stats.Seconds > record.BestSeconds) { record.BestSeconds = stats.Seconds; record.BestSecondsAtUtc = now; }
            if (stats.Ended && !ended)
            {
                record.FinishedRuns = Add(record.FinishedRuns, 1);
                if (stats.Cleared) record.Clears = Add(record.Clears, 1);
                ended = true;
            }
            if (game == GameKind.Fortress)
            {
                data.FortressShots = Add(data.FortressShots, stats.Attempts - previousAttempts);
                data.FortressHits = Add(data.FortressHits, stats.Hits - previousHits);
                if (stats.BestCombo > data.FortressBestCombo) { data.FortressBestCombo = stats.BestCombo; data.FortressBestComboAtUtc = now; }
            }
            else if (game == GameKind.Baseball)
            {
                long delta = Math.Max(0, stats.Score - previousScore);
                data.BaseballScore = data.BaseballScore > Int64.MaxValue - delta ? Int64.MaxValue : data.BaseballScore + delta;
                data.BaseballAttempts = Add(data.BaseballAttempts, stats.Attempts - previousAttempts);
                data.BaseballHits = Add(data.BaseballHits, stats.Hits - previousHits);
                if (stats.BestDistance > data.BaseballBestDistance) { data.BaseballBestDistance = stats.BestDistance; data.BaseballBestDistanceAtUtc = now; }
            }
            else if (stats.Score > data.RunnerBest) { data.RunnerBest = (int)Math.Min(Int32.MaxValue, stats.Score); data.RunnerBestAtUtc = now; }
            previousScore = stats.Score; previousAttempts = stats.Attempts; previousHits = stats.Hits;
            foreach (Achievement a in Achievements.All)
                if (a.Matches(game, mode, stats) && !data.UnlockedTitles.Contains(a.Id))
                {
                    data.UnlockedTitles.Add(a.Id); newTitles.Add(a.Name);
                    data.TitleUnlocks.Add(new TitleUnlock { Id = a.Id, EarnedAtUtc = now });
                }
            store.Save();
        }
    }
}

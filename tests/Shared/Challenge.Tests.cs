using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Launcher;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Runner;

public static class ChallengeRegressionTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class FarRandom : Random { public override double NextDouble() { return 0.99; } }
    static void Fire(FortressSession game, bool hit)
    {
        if (game.Result.Length > 0) game.Advance(2, 300);
        Assert(game.BeginCharge(300), "Challenge charge rejected.");
        double seconds = hit ? 2 * (game.TargetX - 87 - 70) / (1800 - 87 - 70) : 0;
        while (seconds > 0.00000001) { double step = Math.Min(0.01, seconds); game.Advance(step, 300); seconds -= step; }
        Assert(game.ReleaseCharge(), "Challenge shot rejected.");
        for (int i = 0; game.Flying && i < 2000; i++) game.Advance(0.01, 300);
        Assert(!game.Flying && game.WasHit == hit, "Unexpected challenge shot result.");
    }
    static void Hit(BaseballSession game, double dt)
    {
        if (game.Phase == BaseballPhase.Result) game.Advance(BaseballSession.ResultSeconds);
        game.Advance(BaseballSession.ReadySeconds);
        double press = game.PitchSeconds - BaseballSession.SwingContactDelay;
        while (game.Elapsed + dt < press) game.Advance(dt);
        game.Advance(press - game.Elapsed); Assert(game.Swing(), "Swing rejected.");
        for (int i = 0; game.Phase == BaseballPhase.Pitch && i < 100; i++) game.Advance(dt);
        Assert(game.Phase == BaseballPhase.Flight, "Perfect swing did not fly.");
        int oldAttempts = game.Attempts;
        Assert(!game.IsComplete, "Challenge ended before last ball landed.");
        game.SetActive(false); game.Advance(100);
        Assert(game.Attempts == oldAttempts, "Inactive flight scored."); game.SetActive(true);
        for (int i = 0; game.Phase == BaseballPhase.Flight && i < 1000; i++) game.Advance(dt);
        Assert(game.Attempts == oldAttempts + 1, "Landing was not counted exactly once.");
    }
    public static void Run(string artifacts)
    {
        FortressSession fortress = new FortressSession(new FarRandom(), GameMode.Challenge);
        fortress.ResetLayout(1800, 600, new PointF(87, 571), 300);
        int events = 0; fortress.ShotFinished += delegate { events++; };
        for (int i = 0; i < 10; i++) Fire(fortress, true);
        Assert(fortress.IsComplete && fortress.Shots == 10 && fortress.Hits == 10 && fortress.Score > 10000 && events == 10, "Ten-shot challenge did not complete.");
        long score = fortress.Score; fortress.Advance(10, 300);
        Assert(!fortress.BeginCharge(300) && fortress.Score == score && fortress.ResultRemaining == 0, "Eleventh shot allowed.");
        fortress.Restart(); fortress.ResetLayout(1800, 600, new PointF(87, 571), 300);
        Assert(!fortress.IsComplete && fortress.Score == 0 && fortress.Hits == 0 && fortress.Shots == 0, "Fortress retry retained run stats.");
        Fire(fortress, false); Assert(fortress.LastPoints == 0 && fortress.Score == 0, "Miss awarded distance points.");
        Assert(FortressSession.PointsForDistance(800, 1000) > FortressSession.PointsForDistance(200, 1000), "Distance does not increase score.");
        Assert(FortressSession.PointsForDistance(400, 1000) == FortressSession.PointsForDistance(800, 2000), "Display size changes relative distance score.");
        FortressSession practice = new FortressSession(new FarRandom(), GameMode.Practice); practice.ResetLayout(1800, 600, new PointF(87, 571), 300);
        for (int i = 0; i < 11; i++) Fire(practice, true);
        Assert(!practice.IsComplete && practice.Shots == 11, "Practice has challenge limit.");

        foreach (int fps in new[] { 30, 60, 144 })
        {
            BaseballSession baseball = new BaseballSession(new Random(42), GameMode.Challenge); baseball.SetActive(true);
            for (int i = 0; i < 10; i++) Hit(baseball, 1.0 / fps);
            Assert(baseball.IsComplete && baseball.Score == 16000 && baseball.Hits == 10 && baseball.HomeRuns == 10, "Ten-pitch challenge counts differ with FPS.");
            baseball.Advance(100); baseball.Advance(100);
            Assert(baseball.Attempts == 10 && !baseball.Swing() && baseball.Score == 16000, "Challenge added extra pitch or score.");
            baseball.Restart(); Assert(baseball.Score == 0 && baseball.HomeRuns == 0 && baseball.Attempts == 0 && !baseball.IsComplete, "Baseball retry retained run stats.");
        }
        BaseballSession misses = new BaseballSession(new Random(2), GameMode.Challenge); misses.SetActive(true);
        for (int i = 0; i < 10; i++)
        {
            if (i > 0) misses.Advance(BaseballSession.ResultSeconds);
            misses.Advance(BaseballSession.ReadySeconds); misses.Advance(misses.PitchSeconds + 0.31);
        }
        Assert(misses.IsComplete && misses.Hits == 0 && misses.Score == 0, "Misses did not consume ten pitches.");
        double lowest = 1000, highest = 0; BaseballSession speeds = new BaseballSession(new Random(5)); speeds.SetActive(true);
        for (int i = 0; i < 1000; i++)
        {
            speeds.ResetRound(); speeds.Advance(BaseballSession.ReadySeconds);
            Assert(speeds.PitchKph >= 90 && speeds.PitchKph <= 160 && speeds.PitchSeconds >= BaseballSession.MinPitchSeconds && speeds.PitchSeconds <= BaseballSession.MaxPitchSeconds, "Pitch outside speed range.");
            lowest = Math.Min(lowest, speeds.PitchKph); highest = Math.Max(highest, speeds.PitchKph);
        }
        Assert(lowest < 92 && highest > 158 && BaseballSession.MaxPitchKph == BaseballSession.PreviousMaxPitchKph + 20 && Math.Abs(BaseballSession.FenceHeight - 21.6) < 0.001, "Difficulty settings not applied.");
        bool newlyBlocked = false;
        for (int distance = 131; distance < 160; distance++)
        {
            double error = BaseballSession.HitWindow * (1 - Math.Pow((distance - 12) / 148.0, 2.0 / 3));
            double height = BaseballSession.ArcHeight(distance, 130.0 / distance);
            if (height > 21 && height <= BaseballSession.FenceHeight + BaseballSession.BallRadius) { Assert(BaseballSession.EvaluateTiming(error).HitFence, "Raised fence did not block a formerly clear hit."); newlyBlocked = true; }
        }
        Assert(newlyBlocked && BaseballSession.EvaluateTiming(0).HomeRun, "Fence makes all or no home runs possible.");
        Runner(); Rewards(artifacts); Timestamps(artifacts); Dialogs(artifacts); CommonHints(artifacts);
        Console.WriteLine("PASS: 10-shot/10-pitch modes, distance scoring, 90-160km/h, raised fence, 120s maximum-speed survival, per-run titles, persistence and mode/result/records UI.");
    }
    static void Runner()
    {
        Assert(RunnerSession.MinimumGap < 518, "Runner obstacles are not denser.");
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (int width in new[] { 320, 1280 })
        for (int seed = 0; seed < 12; seed++)
        {
            RunnerSession runner = new RunnerSession(new Random(seed), GameMode.Challenge, 0);
            runner.Active = true; Assert(runner.Speed == 560, "Challenge did not start at maximum speed."); runner.Jump();
            for (int i = 0; i < fps * 122 && runner.Phase == RunnerPhase.Running; i++)
            {
                foreach (RunnerObstacle obstacle in runner.Obstacles)
                    if (obstacle.X > 60 && obstacle.X < 61 + runner.Speed * 0.26 && runner.Height == 0) { runner.Jump(); break; }
                runner.Advance(1.0 / fps, width);
                if (i == fps * 30)
                {
                    double before = runner.SurvivalSeconds; runner.Active = false; runner.Advance(20, width);
                    Assert(runner.SurvivalSeconds == before, "Pause counted toward survival."); runner.Active = true;
                }
            }
            Assert(runner.Phase == RunnerPhase.Cleared && runner.SurvivalSeconds == 120, "Unclearable runner course: seed=" + seed + " fps=" + fps + " width=" + width);
            double distance = runner.Distance; runner.Advance(1, width); Assert(runner.Distance == distance, "Clear did not stop simulation.");
            runner.Restart(); Assert(runner.SurvivalSeconds == 0 && runner.Phase == RunnerPhase.Ready && runner.Speed == 560, "Runner retry changed challenge speed.");
        }
        RunnerSession collision = new RunnerSession(new Random(2), GameMode.Challenge, 0); collision.Active = true; collision.Jump();
        for (int i = 0; i < 600 && !collision.IsComplete; i++) collision.Advance(1.0 / 60, 480);
        Assert(collision.Phase == RunnerPhase.GameOver && !collision.Stats.Cleared && collision.SurvivalSeconds < 120, "Collision counted as success.");
    }
    static void Rewards(string artifacts)
    {
        GameStore split = new GameStore();
        for (int i = 0; i < 2; i++)
            new ProgressTracker(split, GameKind.Baseball, GameMode.Challenge).Record(new RunStats { Score = 800, Hits = 5, HomeRuns = 5, Attempts = 10, Ended = true });
        Assert(split.Data.BaseballHits == 10 && !split.Data.UnlockedTitles.Contains("baseball.ten") && !split.Data.UnlockedTitles.Contains("baseball.tenhr")
            && !split.Data.UnlockedTitles.Contains("baseball.1000"), "Counts or score were combined across runs for a title.");
        GameStore practice = new GameStore();
        new ProgressTracker(practice, GameKind.Fortress, GameMode.Practice).Record(new RunStats { Score = 20000, Hits = 10, Attempts = 10 });
        Assert(practice.Data.UnlockedTitles.Count == 0 && practice.Data.FortressPractice.BestScore == 20000 && practice.Data.FortressChallenge.BestScore == 0, "Practice unlocked challenge rewards.");
        string savePath = Path.Combine(artifacts, "titles-" + Guid.NewGuid().ToString("N") + ".xml");
        GameStore store = new GameStore(savePath);
        ProgressTracker fortress = new ProgressTracker(store, GameKind.Fortress, GameMode.Challenge);
        fortress.Record(new RunStats { Score = 999, Attempts = 1, Hits = 1 });
        Assert(store.Data.UnlockedTitles.Contains("fortress.first") && !store.Data.UnlockedTitles.Contains("fortress.1000"), "First-hit or point threshold wrong.");
        foreach (int score in new[] { 1000, 5000, 10000 })
        {
            fortress.Record(new RunStats { Score = score, Attempts = 9, Hits = 9 });
            Assert(store.Data.UnlockedTitles.Contains("fortress." + score), "Point milestone missing.");
        }
        RunStats perfect = new RunStats { Score = 16000, Attempts = 10, Hits = 10, HomeRuns = 10, Ended = true };
        fortress.Record(perfect); fortress.Record(perfect);
        ProgressTracker baseball = new ProgressTracker(store, GameKind.Baseball, GameMode.Challenge); baseball.Record(perfect); baseball.Record(perfect);
        new ProgressTracker(store, GameKind.Runner, GameMode.Challenge).Record(new RunStats { Seconds = 120, Score = 6720, Ended = true, Cleared = true });
        Assert(store.Data.UnlockedTitles.Count == Achievements.All.Length && store.Data.FortressChallenge.FinishedRuns == 1 && store.Data.BaseballChallenge.FinishedRuns == 1, "Rewards missing or completion counted twice.");
        Assert(store.Data.FortressTitle == "" && store.Data.BaseballTitle == "" && store.Data.RunnerTitle == "", "Earned title was automatically equipped.");
        GameStore unequipped = new GameStore(savePath); unequipped.Load();
        Assert(unequipped.Data.UnlockedTitles.Count == 12 && unequipped.Data.BaseballTitle == "", "Unequipped titles appeared after restart.");
        Assert(Achievements.Equip(store.Data, GameKind.Baseball, "baseball.tenhr") && !Achievements.Equip(store.Data, GameKind.Baseball, "fortress.ten"), "Cross-game title equipped.");
        store.Save(); GameStore reopened = new GameStore(savePath);
        // Reopen using an explicit path rather than touching the real user profile.
        reopened.Load(); Assert(reopened.Data.UnlockedTitles.Count == 12 && reopened.Data.BaseballTitle == "baseball.tenhr" && reopened.Data.BaseballChallenge.BestScore == 16000, "Titles or records lost on restart.");
        string legacy = Path.Combine(artifacts, "legacy-v1-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(legacy, "<GameData><Version>1</Version><BaseballScore>50000</BaseballScore><BaseballHits>20</BaseballHits><BaseballAttempts>30</BaseballAttempts><RunnerBest>800</RunnerBest></GameData>");
        GameStore migration = new GameStore(legacy); migration.Load();
        Assert(migration.Data.Version == 3 && migration.Data.BaseballScore == 50000 && migration.Data.RunnerBest == 800 && migration.Data.RunnerPractice.BestScore == 800 && migration.Data.UnlockedTitles.Count == 0 && migration.Data.BaseballChallenge.BestScore == 0, "Legacy totals became challenge rewards or were lost.");
    }
    static void Timestamps(string artifacts)
    {
        string path = Path.Combine(artifacts, "timestamps-" + Guid.NewGuid().ToString("N") + ".xml");
        GameStore store = new GameStore(path);
        DateTime first = new DateTime(2026, 10, 18, 1, 2, 3, DateTimeKind.Utc), now = first;
        ProgressTracker tracker = new ProgressTracker(store, GameKind.Baseball, GameMode.Challenge, () => now);
        RunStats stats = new RunStats { Score = 1600, Hits = 1, HomeRuns = 1, BestDistance = 160, Attempts = 1 };
        tracker.Record(stats);
        GameRecord record = store.Data.BaseballChallenge;
        Assert(record.BestScoreAtUtc == first && record.BestHitsAtUtc == first && record.BestHomeRunsAtUtc == first
            && store.Data.BaseballBestDistanceAtUtc == first && RecordTime.TitleEarned(store.Data, "baseball.firsthr") == first, "Achievement times not captured together.");
        now = first.AddDays(1); tracker.Record(stats);
        Assert(record.BestScoreAtUtc == first && RecordTime.TitleEarned(store.Data, "baseball.firsthr") == first, "Repeated result changed first achievement time.");
        new ProgressTracker(store, GameKind.Baseball, GameMode.Challenge, () => now).Record(new RunStats { Score = 100, Hits = 1, Attempts = 1 });
        Assert(record.BestScoreAtUtc == first && record.BestHitsAtUtc == first, "Lower or tied result changed record date.");
        tracker.Record(new RunStats { Score = 3200, Hits = 2, HomeRuns = 1, BestDistance = 160, Attempts = 2 });
        Assert(record.BestScoreAtUtc == now && record.BestHitsAtUtc == now && record.BestHomeRunsAtUtc == first, "Independent record dates were not preserved.");
        new ProgressTracker(store, GameKind.Runner, GameMode.Challenge, () => now).Record(new RunStats { Score = 6720, Seconds = 120, Ended = true, Cleared = true });
        new ProgressTracker(store, GameKind.Fortress, GameMode.Practice, () => now).Record(new RunStats { Score = 1000, Hits = 1, BestCombo = 1, Attempts = 1 });
        GameStore loaded = new GameStore(path); loaded.Load();
        Assert(loaded.Data.BaseballChallenge.BestScoreAtUtc == now && loaded.Data.BaseballChallenge.BestHomeRunsAtUtc == first
            && loaded.Data.RunnerChallenge.BestSecondsAtUtc == now && loaded.Data.RunnerBestAtUtc == now
            && loaded.Data.FortressBestComboAtUtc == now && loaded.Data.BaseballBestDistanceAtUtc == first
            && RecordTime.TitleEarned(loaded.Data, "baseball.firsthr") == first, "Timestamps lost during save/load.");
        Assert(loaded.Data.BaseballChallenge.BestScoreAtUtc.Value.Kind == DateTimeKind.Utc, "UTC timestamp kind lost.");
        Assert(RecordTime.Format(first) == first.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), "Display did not use local date and time.");
        string legacyPath = Path.Combine(artifacts, "untimed-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(legacyPath, "<GameData><Version>2</Version><BaseballChallenge><BestScore>1600</BestScore><BestHits>1</BestHits></BaseballChallenge><UnlockedTitles><string>baseball.1000</string></UnlockedTitles></GameData>");
        GameStore old = new GameStore(legacyPath); old.Load();
        new ProgressTracker(old, GameKind.Baseball, GameMode.Challenge, () => now).Record(stats);
        Assert(old.Data.Version == 3 && old.Data.BaseballChallenge.BestScoreAtUtc == null
            && RecordTime.TitleEarned(old.Data, "baseball.1000") == null && RecordTime.Format(null) == "기록 없음", "Legacy record received an invented date.");
        Console.WriteLine("PASS: record/title timestamps, ties, independent improvements, UTC round-trip, local display and undated profile migration.");
    }
    static void Dialogs(string artifacts)
    {
        GameStore original = GameEnvironment.Store; GameEnvironment.Store = new GameStore();
        try
        {
            foreach (GameKind kind in Enum.GetValues(typeof(GameKind)))
            {
                using (ModeSelectionForm picker = new ModeSelectionForm(kind))
                {
                    IntPtr handle = picker.Handle; Assert(picker.ClientSize == new Size(58, 74) && picker.Controls.Count == 4, "Mode selector layout changed.");
                    Render(picker, Path.Combine(artifacts, "mode-selector.png"));
                    typeof(Control).GetMethod("OnClick", Private).Invoke(picker.Controls[0], new object[] { EventArgs.Empty });
                    Assert(picker.SelectedMode == GameMode.Challenge && picker.DialogResult == DialogResult.OK, "Challenge selection failed.");
                }
                using (ModeSelectionForm picker = new ModeSelectionForm(kind))
                {
                    typeof(Control).GetMethod("OnClick", Private).Invoke(picker.Controls[1], new object[] { EventArgs.Empty });
                    Assert(picker.SelectedMode == GameMode.Practice && picker.DialogResult == DialogResult.OK, "Practice selection failed.");
                }
            }
            ProgressTracker progress = new ProgressTracker(GameEnvironment.Store, GameKind.Baseball, GameMode.Challenge);
            RunStats stats = new RunStats { Score = 16000, Attempts = 10, Hits = 10, HomeRuns = 10, Ended = true }; progress.Record(stats);
            using (RecordsForm records = new RecordsForm(GameKind.Baseball))
            {
                Button selector = (Button)records.Controls[0];
                Assert(selector.Text.StartsWith("야구"), "Records selected the wrong game.");
                Assert(records.Size == new Size(174, 148), "Records window is no longer compact.");
                ArcadeList list = (ArcadeList)records.Controls[2]; Button equip = (Button)records.Controls[3];
                list.SelectedIndex = 1;
                typeof(Control).GetMethod("OnClick", Private).Invoke(equip, new object[] { EventArgs.Empty });
                Assert(GameEnvironment.Store.Data.BaseballTitle == "baseball.1000", "Record equip button failed.");
                object worn = list.Items[1];
                Assert((Color)worn.GetType().GetProperty("Color").GetValue(worn, null) == Color.Yellow, "Equipped title is not yellow.");
                string earned = RecordTime.Format(RecordTime.TitleEarned(GameEnvironment.Store.Data, "baseball.1000"));
                string description = (string)worn.GetType().GetProperty("Description").GetValue(worn, null);
                Assert(description.Contains("획득 일시: " + earned), "Title tooltip omitted its acquisition date.");
                RecordInfoPopup info = (RecordInfoPopup)typeof(RecordsForm).GetField("info", Private).GetValue(records);
                typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(records.Controls[1], new object[] { EventArgs.Empty });
                Assert(info.Body.Contains(RecordTime.Format(GameEnvironment.Store.Data.BaseballChallenge.BestScoreAtUtc)), "Records panel omitted achievement time.");
                Assert(info.Right <= records.Left - 8 && !info.Bounds.IntersectsWith(records.Bounds), "Records panel covers the title list.");
                Render(info, Path.Combine(artifacts, "record-info.png"));
                typeof(Control).GetMethod("OnMouseMove", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, 5, list.GetItemRectangle(1).Top + 3, 0) });
                Assert(info.Body.Contains("획득 일시: " + earned), "Hover did not show acquisition time.");
                Render(info, Path.Combine(artifacts, "title-info.png"));
                Render(records, Path.Combine(artifacts, "records-with-arrows.png"));
                using (Bitmap panel = new Bitmap(Path.Combine(artifacts, "title-info.png")))
                using (Bitmap menu = new Bitmap(Path.Combine(artifacts, "records-with-arrows.png")))
                using (Bitmap combined = new Bitmap(panel.Width + 8 + menu.Width, Math.Max(panel.Height, menu.Height)))
                using (Graphics g = Graphics.FromImage(combined))
                {
                    g.Clear(Color.FromArgb(45, 49, 55)); g.DrawImageUnscaled(panel, 0, combined.Height - panel.Height);
                    g.DrawImageUnscaled(menu, panel.Width + 8, combined.Height - menu.Height);
                    combined.Save(Path.Combine(artifacts, "records-left-detail.png"));
                }
                Button previous = (Button)records.Controls[5], next = (Button)records.Controls[6];
                foreach (string expected in new[] { "포트리스", "다이노 러너", "야구" })
                {
                    typeof(Control).GetMethod("OnClick", Private).Invoke(previous, new object[] { EventArgs.Empty });
                    Assert(selector.Text == expected, "Reverse game selection failed.");
                }
                foreach (string expected in new[] { "다이노 러너", "포트리스", "야구" })
                {
                    typeof(Control).GetMethod("OnClick", Private).Invoke(next, new object[] { EventArgs.Empty });
                    Assert(selector.Text == expected, "Forward game selection failed.");
                }
                foreach (Keys key in new[] { Keys.Left, Keys.Right })
                {
                    object[] args = { new Message(), key };
                    typeof(RecordsForm).GetMethod("ProcessCmdKey", Private).Invoke(records, args);
                    Assert(selector.Text == (key == Keys.Left ? "포트리스" : "야구"), "Arrow-key game selection failed.");
                }
                int selectedBeforeScroll = list.SelectedIndex;
                typeof(Control).GetMethod("OnMouseWheel", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, 5, 5, -120) });
                Assert(list.TopIndex == 2 && list.SelectedIndex == selectedBeforeScroll, "Wheel scroll changed selection or did not move.");
                list.TopIndex = 0;
                Rectangle thumb = list.ScrollThumb;
                typeof(Control).GetMethod("OnMouseDown", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.Left, 1, thumb.Left + 2, thumb.Top + 1, 0) });
                typeof(Control).GetMethod("OnMouseMove", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.Left, 0, thumb.Left + 2, list.ScrollTrack.Bottom, 0) });
                typeof(Control).GetMethod("OnMouseUp", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.Left, 1, thumb.Left + 2, list.ScrollTrack.Bottom, 0) });
                Assert(list.TopIndex == list.MaximumTop, "Neon scrollbar drag did not reach the last title.");
                typeof(Control).GetMethod("OnKeyDown", Private).Invoke(list, new object[] { new KeyEventArgs(Keys.End) });
                Assert(list.SelectedIndex == list.Items.Count - 1, "Keyboard cannot reach the last title.");
                Rectangle area = new Rectangle(-1600, -200, 1600, 860), owner = new Rectangle(-182, 504, 174, 148);
                Point position = RecordInfoPopup.Place(owner, new Size(310, 110), area);
                Rectangle placed = new Rectangle(position, new Size(310, 110));
                Assert(area.Contains(placed) && placed.Right <= owner.Left - 8, "Negative-coordinate display placement failed.");
                owner = new Rectangle(138, 324, 174, 148); area = new Rectangle(0, 0, 320, 480);
                placed = new Rectangle(RecordInfoPopup.Place(owner, new Size(140, 110), area), new Size(140, 110));
                Assert(area.Contains(placed) && !placed.IntersectsWith(owner), "Narrow display fallback overlaps records.");
                // Keep one locked reward in the screenshot and ensure it cannot be equipped.
                GameEnvironment.Store.Data.UnlockedTitles.Remove("baseball.tenhr");
                for (int i = 0; i < 3; i++) typeof(Control).GetMethod("OnClick", Private).Invoke(selector, new object[] { EventArgs.Empty });
                Assert(selector.Text.StartsWith("야구"), "Records game selection did not cycle.");
                list.SelectedIndex = list.Items.Count - 1;
                object locked = list.SelectedItem;
                Assert((Color)locked.GetType().GetProperty("Color").GetValue(locked, null) == Color.White && !equip.Enabled, "Locked title color or equip state is wrong.");
                Render(records, Path.Combine(artifacts, "records-locked.png"));
                list.SelectedIndex = 1;
                Render(records, Path.Combine(artifacts, "records-titles.png"));
                typeof(Control).GetMethod("OnClick", Private).Invoke(equip, new object[] { EventArgs.Empty });
                Assert(GameEnvironment.Store.Data.BaseballTitle == "", "Record unequip button failed.");
            }
            using (ChallengeResultForm result = new ChallengeResultForm(GameKind.Baseball, stats, progress))
            {
                Assert(result.Size == GameEnvironment.SelectionSize, "Result window differs from game selector size.");
                Render(result, Path.Combine(artifacts, "challenge-result.png"));
                foreach (Control control in result.Controls) Assert(result.ClientRectangle.Contains(control.Bounds), "Result control is clipped.");
                typeof(Control).GetMethod("OnClick", Private).Invoke(result.Controls[4], new object[] { EventArgs.Empty });
                Assert(result.DialogResult == DialogResult.Retry, "Compact retry button failed.");
            }
            using (FortressGame fortress = new FortressGame(GameMode.Challenge)) RenderGame(fortress, Path.Combine(artifacts, "fortress-challenge.png"));
            using (BaseballGame baseball = new BaseballGame(GameMode.Challenge))
            {
                BaseballSession model = (BaseballSession)typeof(BaseballGame).GetField("session", Private).GetValue(baseball);
                model.SetActive(true); model.Advance(BaseballSession.ReadySeconds);
                RenderGame(baseball, Path.Combine(artifacts, "baseball-challenge.png"));
                Assert(model.Mode == GameMode.Challenge && model.Attempts == 0 && model.Score == 0, "Game did not start a fresh challenge.");
            }
            GameEnvironment.Store.Data.RunnerBest = 6720;
            GameEnvironment.Store.Data.RunnerPractice.BestScore = 100;
            GameEnvironment.Store.Data.RunnerChallenge.BestScore = 6720;
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
                using (RunnerGame runner = new RunnerGame(mode))
                {
                    RunnerSession model = (RunnerSession)typeof(RunnerGame).GetField("session", Private).GetValue(runner);
                    Assert(model.Best == (mode == GameMode.Practice ? 100 : 6720), "Runner HI mixed records from different modes.");
                    if (mode == GameMode.Challenge) RenderGame(runner, Path.Combine(artifacts, "runner-challenge.png"));
                }
        }
        finally { GameEnvironment.Store = original; }
    }
    public static void RunFocusedHints(string artifacts) { CommonHints(artifacts, true); }
    static void CommonHints(string artifacts, bool verifyFocus = false)
    {
        using (LauncherForm launcher = new LauncherForm())
        using (MonitorSelectionForm monitor = new MonitorSelectionForm())
        using (ModeSelectionForm mode = new ModeSelectionForm(GameKind.Baseball))
        using (ChallengeResultForm result = new ChallengeResultForm(GameKind.Baseball, new RunStats { Score = 1000 },
            new ProgressTracker(new GameStore(), GameKind.Baseball, GameMode.Challenge)))
        {
            foreach (Form form in new Form[] { launcher, monitor, mode, result })
            {
                form.Show(); Application.DoEvents();
                form.Location = new Point(GameEnvironment.Display.WorkingArea.Left + 200, GameEnvironment.Display.WorkingArea.Top + 120);
                form.Activate(); Application.DoEvents();
                if (verifyFocus) Assert(Form.ActiveForm == form, "Active-window tooltip test could not activate the owner: " + form.GetType().Name);
                ArcadeHints hints = (ArcadeHints)form.GetType().GetField("hints", Private).GetValue(form);
                Control button = form.Controls[0];
                typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(button, new object[] { EventArgs.Empty });
                Application.DoEvents();
                Assert(hints.Popup.IsShowing, "Native help was not painted for " + form.GetType().Name);
                if (verifyFocus) Assert(Form.ActiveForm == form, "Native tooltip stole focus from its active owner.");
                Assert(!String.IsNullOrEmpty(hints.Popup.Body) && hints.Popup.Heading == "HELP", "Common help panel is missing.");
                Assert(hints.Popup.Right <= form.Left - 8 && !hints.Popup.Bounds.IntersectsWith(form.Bounds), "Help panel overlaps buttons.");
                // Windows can deliver the next MouseEnter before the previous MouseLeave.
                Control nextButton = form.Controls[1];
                typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(nextButton, new object[] { EventArgs.Empty });
                typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(button, new object[] { EventArgs.Empty });
                Application.DoEvents();
                Assert(hints.Popup.IsShowing, "Previous button's late leave hid the new explanation: " + form.GetType().Name);
                typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(nextButton, new object[] { EventArgs.Empty });
                Assert(!hints.Popup.IsShowing, "Help remained after mouse leave: " + form.GetType().Name);
                using (Form other = new Form { ShowInTaskbar = false, Size = new Size(80, 60), StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) })
                {
                    other.Show(); other.Activate(); Application.DoEvents();
                    if (verifyFocus) Assert(Form.ActiveForm == other, "Inactive-owner test did not move focus.");
                    typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(button, new object[] { EventArgs.Empty });
                    System.Diagnostics.Stopwatch wait = System.Diagnostics.Stopwatch.StartNew();
                    while (!hints.Popup.IsShowing && wait.ElapsedMilliseconds < 500) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    Assert(hints.Popup.IsShowing && (!verifyFocus || Form.ActiveForm == other), "Inactive owner tooltip did not paint or stole focus.");
                    typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(button, new object[] { EventArgs.Empty });
                    other.Close();
                }
                form.Hide(); Application.DoEvents();
            }
            Control exit = launcher.Controls["ExitButton"], back = mode.Controls[3], resultBack = result.Controls[5];
            Color exitColor = (Color)exit.GetType().GetProperty("Accent").GetValue(exit, null);
            Assert(exit.GetType() == back.GetType() && back.GetType() == resultBack.GetType()
                && (Color)back.GetType().GetProperty("Accent").GetValue(back, null) == exitColor && exit.Size == back.Size && back.Size == resultBack.Size, "EXIT/BACK styles differ.");
            Color chal = (Color)mode.Controls[0].GetType().GetProperty("Accent").GetValue(mode.Controls[0], null);
            Color practice = (Color)mode.Controls[1].GetType().GetProperty("Accent").GetValue(mode.Controls[1], null);
            Color record = (Color)mode.Controls[2].GetType().GetProperty("Accent").GetValue(mode.Controls[2], null);
            Assert(chal != practice && chal != record && practice != record && chal != exitColor, "Mode buttons do not have distinct colors.");
            foreach (Control control in mode.Controls) Assert(mode.ClientRectangle.Contains(control.Bounds), "Mode button clipped.");
            Render(mode, Path.Combine(artifacts, "mode-selector.png"));
            ArcadeHints modeHints = (ArcadeHints)typeof(ModeSelectionForm).GetField("hints", Private).GetValue(mode);
            typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(mode.Controls[0], new object[] { EventArgs.Empty });
            Render(modeHints.Popup, Path.Combine(artifacts, "mode-help.png"));
        }
        using (TaskbarGames.Games.Pet.PetGame pet = new TaskbarGames.Games.Pet.PetGame())
        {
            ArcadeHints hints = (ArcadeHints)pet.GetType().GetField("hint", Private).GetValue(pet);
            ToolStripDropDown menu = (ToolStripDropDown)pet.GetType().GetField("menu", Private).GetValue(pet);
            menu.Location = new Point(GameEnvironment.Display.WorkingArea.Right - 150, GameEnvironment.Display.WorkingArea.Bottom - 50);
            menu.PerformLayout();
            ToolStripItem item = menu.Items[2];
            // Hidden ToolStrip items suppress native mouse events; exercise the same item-help path directly.
            hints.ShowMenuHint(pet, menu, item);
            Assert(!menu.ShowItemToolTips && hints.Popup.Body == item.ToolTipText && !hints.Popup.Bounds.IntersectsWith(menu.Bounds), "Pet menu help: native=" + menu.ShowItemToolTips + " body=" + hints.Popup.Body + " expected=" + item.ToolTipText + " item=" + item.Bounds + " menu=" + menu.Bounds + " popup=" + hints.Popup.Bounds);
        }
        using (RecordsForm records = new RecordsForm(GameKind.Baseball))
        {
            records.Show(); Application.DoEvents();
            RecordInfoPopup info = (RecordInfoPopup)typeof(RecordsForm).GetField("info", Private).GetValue(records);
            Control summary = records.Controls[1], list = records.Controls[2], equip = records.Controls[3];
            typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(summary, new object[] { EventArgs.Empty });
            typeof(Control).GetMethod("OnMouseMove", Private).Invoke(list, new object[] { new MouseEventArgs(MouseButtons.None, 0, 5, 5, 0) });
            typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(summary, new object[] { EventArgs.Empty });
            Application.DoEvents();
            Assert(info.IsShowing && info.Heading == "TITLE", "Summary leave hid the hovered title.");
            typeof(Control).GetMethod("OnMouseEnter", Private).Invoke(equip, new object[] { EventArgs.Empty });
            typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(list, new object[] { EventArgs.Empty });
            Application.DoEvents();
            Assert(info.IsShowing, "List leave hid the equip explanation.");
            typeof(Control).GetMethod("OnMouseLeave", Private).Invoke(equip, new object[] { EventArgs.Empty });
            Assert(!info.IsShowing, "Equip explanation did not close on leave.");
            records.Hide();
        }
        Console.WriteLine("PASS: common left-side help panels, pet menu hints, unified EXIT/BACK style and distinct mode colors.");
    }
    static void RenderGame(Form game, string path)
    {
        using (Bitmap image = new Bitmap(game.ClientSize.Width, game.ClientSize.Height))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.Clear(Color.FromArgb(225, 231, 239));
            game.GetType().GetMethod("OnPaint", Private).Invoke(game, new object[] { new PaintEventArgs(g, game.ClientRectangle) });
            int height = Math.Min(110, image.Height);
            using (Bitmap crop = image.Clone(new Rectangle(0, image.Height - height, Math.Min(image.Width, 1024), height), image.PixelFormat)) crop.Save(path);
        }
    }
    static void Render(Form form, string path)
    {
        IntPtr handle = form.Handle;
        using (Bitmap image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.Clear(form.BackColor);
            typeof(Control).GetMethod("OnPaint", Private).Invoke(form, new object[] { new PaintEventArgs(g, form.ClientRectangle) });
            foreach (Control control in form.Controls)
                using (Bitmap child = new Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(child, control.ClientRectangle);
                    g.DrawImageUnscaled(child, control.Location);
                }
            image.Save(path);
        }
    }
}

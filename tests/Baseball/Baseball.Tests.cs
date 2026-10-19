// 야구 타격 규칙과 실제 창 연결을 검증하는 회귀 테스트.
// 시간 오차를 직접 주입하여 프레임 속도가 바뀌어도 거리와 점수가 일정한지 확인한다.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Baseball;

public static class BaseballRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    // 조건 실패 시 예외로 중단하여 PowerShell에서도 오류를 확인할 수 있게 한다.
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    // 고정 시드의 세션을 활성화하고 투구 단계까지 진행해 테스트마다 같은 출발 조건을 만든다.
    static BaseballSession NewPitch(int seed)
    {
        BaseballSession game = new BaseballSession(new Random(seed));
        game.SetActive(true);
        game.Advance(BaseballSession.ReadySeconds);
        Assert(game.Phase == BaseballPhase.Pitch, "Pitch did not start.");
        return game;
    }

    // 목표 타격 오차에 맞는 입력 시각을 계산한 뒤 일정 프레임 간격으로 진행한다.
    // 입력 시각과 예약된 접촉 시각을 구분해 SwingContactDelay가 판정에 반영되는지 검사한다.
    static BaseballSession Hit(double error, double frameSeconds)
    {
        BaseballSession game = NewPitch(42);
        double pressAt = game.PitchSeconds - BaseballSession.SwingContactDelay + error;
        while (game.Elapsed + frameSeconds < pressAt) game.Advance(frameSeconds);
        game.Advance(pressAt - game.Elapsed);
        Assert(game.Swing(), "First swing was rejected.");
        Assert(!game.Swing(), "Multiple swings accepted in one pitch.");
        for (int i = 0; game.Phase == BaseballPhase.Pitch && i < 1000; i++) game.Advance(frameSeconds);
        Assert(game.Phase == BaseballPhase.Flight, "Valid timing did not hit.");
        return game;
    }

    // 투구 범위·타이밍 등급·점수·키 반복·포커스 취소·비행 일시 정지와 펜스 결과를 순서대로 확인한다.
    public static void Run(string artifacts, bool includeUI = true)
    {
        BattingResult perfect = BaseballSession.EvaluateTiming(0);
        Assert(perfect.Hit && perfect.Distance == 160 && perfect.Points == 1600 && perfect.Grade == "PERFECT", "Perfect hit scoring failed.");
        Assert(BaseballSession.EvaluateTiming(0.04).Distance > BaseballSession.EvaluateTiming(0.09).Distance, "Timing quality does not change distance.");
        Assert(BaseballSession.EvaluateTiming(-0.04).Distance == BaseballSession.EvaluateTiming(0.04).Distance, "Early/late quality differs.");
        Assert(!BaseballSession.EvaluateTiming(0.121).Hit && !BaseballSession.EvaluateTiming(-0.121).Hit, "Out-of-window swing was a hit.");
        CheckFence();
        double min = Double.MaxValue, max = 0;
        for (int i = 0; i < 100; i++)
        {
            BaseballSession game = NewPitch(i);
            Assert(game.PitchSeconds >= BaseballSession.MinPitchSeconds && game.PitchSeconds <= BaseballSession.MaxPitchSeconds, "Pitch speed outside range.");
            min = Math.Min(min, game.PitchSeconds);
            max = Math.Max(max, game.PitchSeconds);
        }
        Assert(max - min > 0.4, "Random pitch speeds do not vary enough.");
        foreach (double frame in new double[] { 1.0 / 30, 1.0 / 60, 1.0 / 144 })
        {
            BaseballSession game = Hit(0, frame);
            Assert(game.LastHit.Distance == 160, "Frame rate changed perfect hit quality.");
            game.Advance(game.FlightSeconds);
            Assert(game.Score == 1600 && game.Hits == 1 && game.Attempts == 1 && game.BestDistance == 160, "Landing score not counted exactly once.");
            game.Advance(0.3);
            Assert(game.Score == 1600, "Result counted twice.");
            game.Advance(BaseballSession.ResultSeconds);
            Assert(game.Phase == BaseballPhase.Ready, "Next round did not start.");
        }
        BaseballSession early = NewPitch(7);
        early.Swing();
        early.Advance(early.PitchSeconds + 0.31);
        Assert(early.Score == 0 && early.Attempts == 1 && early.LastHit.Grade == "EARLY", "Early miss not recorded.");
        BaseballSession noSwing = NewPitch(9);
        noSwing.Advance(noSwing.PitchSeconds + 0.31);
        Assert(noSwing.Attempts == 1 && noSwing.Hits == 0 && noSwing.Score == 0, "No-swing miss failed.");
        BaseballSession late = NewPitch(12);
        late.Advance(late.PitchSeconds + 0.27);
        late.Swing();
        late.Advance(0.04);
        Assert(late.Phase == BaseballPhase.Result && late.LastHit.Grade == "LATE" && late.Score == 0,
            "A swing after the ball passed did not show late feedback.");
        Assert(late.Advance(0.1) && late.SwingAge > 0, "Late-miss follow-through stopped animating during result.");
        Assert(late.Advance(BaseballSession.SwingDuration) && late.SwingAge < 0, "Swing did not redraw its final recovery pose.");

        BaseballSession cancelled = NewPitch(1);
        cancelled.Advance(cancelled.PitchSeconds - BaseballSession.SwingContactDelay);
        cancelled.Swing();
        cancelled.SetActive(false);
        Assert(!cancelled.Advance(10) && !cancelled.Swing(), "Inactive game accepts input or advances.");
        Assert(cancelled.Phase == BaseballPhase.Ready && cancelled.Attempts == 0 && cancelled.SwingAge < 0, "Focus loss did not cancel pending contact.");
        cancelled.SetActive(true);
        cancelled.Advance(BaseballSession.ReadySeconds);
        cancelled.Advance(cancelled.PitchSeconds + 0.31);
        Assert(cancelled.Score == 0, "Cancelled swing leaked into next pitch.");

        BaseballSession flight = Hit(0.04, 1.0 / 60);
        double before = flight.Elapsed;
        flight.SetActive(false);
        flight.Advance(20);
        Assert(flight.Elapsed == before && flight.Score == 0, "Background flight progressed.");
        flight.SetActive(true);
        flight.Advance(flight.FlightSeconds);
        Assert(flight.Score == flight.LastHit.Points && flight.Score > 0, "Paused flight did not resume.");
        Console.WriteLine("PASS: random pitch bounds; timing-distance scoring at 30/60/144 FPS; misses, one swing per pitch, focus cancellation and flight resume.");

        if (!includeUI) return;
        RenderAndCheckWindow(artifacts);
        RenderSwingPoses(artifacts);
        RenderPitcherPoses(artifacts);
    }

    // 타격 거리 식의 역함수로 원하는 비거리를 만드는 오차를 구해 펜스 경계 사례를 준비한다.
    static double ErrorForDistance(int distance)
    {
        return BaseballSession.HitWindow * (1 - Math.Pow((distance - 12) / 148.0, 2.0 / 3));
    }

    // 펜스 앞/충돌/홈런 경계와 표시 시점, 착지 시에만 점수를 주는 규칙을 검증한다.
    static void CheckFence()
    {
        Assert(!BaseballSession.EvaluateTiming(ErrorForDistance(130)).HomeRun, "Landing at the fence counted as a home run.");
        BaseballSession wall = Hit(ErrorForDistance(134), 1.0 / 60);
        Assert(wall.LastHit.HitFence && !wall.LastHit.HomeRun, "A low ball passed through the fence.");
        double crossing = 130.0 / wall.LastHit.CarryDistance;
        Assert(Math.Abs(wall.FlightDistance(crossing) - 130) < 0.001, "Wall impact was misplaced.");
        Assert(wall.FlightHeight(crossing) <= BaseballSession.FenceHeight + BaseballSession.BallRadius, "Wall collision above fence.");
        Assert(wall.FlightDistance(1) == 128 && wall.FlightHeight(1) == 0, "Wall rebound did not land inside the field.");
        wall.Advance(wall.FlightSeconds);
        Assert(wall.Score == 1280 && wall.LastHit.Distance == 128, "Fence rebound scored its predicted carry instead of landing.");
        BaseballSession homer = Hit(0, 1.0 / 60);
        Assert(!homer.HomeRunVisible && homer.Score == 0, "Home run announced before crossing the fence.");
        homer.Advance(homer.FlightSeconds * 130 / 160 - 0.001);
        Assert(!homer.HomeRunVisible, "Home run announced too early.");
        homer.Advance(0.002);
        Assert(homer.HomeRunVisible && homer.Score == 0, "Fence crossing did not announce home run, or scored before landing.");
        homer.SetActive(false);
        double paused = homer.Elapsed;
        homer.Advance(2);
        Assert(homer.HomeRunVisible && homer.Elapsed == paused, "Home run flight did not pause.");
        homer.SetActive(true);
        homer.Advance(homer.FlightSeconds);
        Assert(homer.HomeRunVisible && homer.Score == 1600, "Home run landing score failed.");
        Console.WriteLine("PASS: 130m fence, low-ball rebound, home run crossing, focus pause and landing-only scoring.");
    }

    // 스윙의 준비·접촉·팔로스루·복귀 시각을 나란히 그려 관절 애니메이션을 육안 확인할 수 있게 한다.
    static void RenderSwingPoses(string artifacts)
    {
        Type animator = typeof(BaseballGame).Assembly.GetType("TaskbarGames.Games.Baseball.BatterAnimation");
        MethodInfo draw = animator.GetMethod("Draw", new[] { typeof(Graphics), typeof(int), typeof(double) });
        double[] times = { -1, 0.02, 0.045, BaseballSession.SwingContactDelay, 0.13, 0.22, 0.32, BaseballSession.SwingDuration };
        using (Bitmap strip = new Bitmap(128 * times.Length, 100))
        using (Graphics g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(235, 238, 243));
            for (int i = 0; i < times.Length; i++)
            {
                g.TranslateTransform(i * 128, 0);
                draw.Invoke(null, new object[] { g, 80, times[i] });
                using (Pen baseline = new Pen(Color.FromArgb(187, 197, 210))) g.DrawLine(baseline, 32, 82, 114, 82);
                g.ResetTransform();
            }
            strip.Save(Path.Combine(artifacts, "baseball-swing-poses.png"), ImageFormat.Png);
            for (int i = 1; i < times.Length; i++)
                for (int y = 66; y <= 80; y++)
                    for (int x = 45; x < 94; x++)
                        Assert(strip.GetPixel(x, y) == strip.GetPixel(i * 128 + x, y), "Batter feet or legs moved during swing.");
        }
    }

    // 폼 내부 테스트 진입점은 Reflection으로 호출하여 테스트 편의를 위한 공개 API 추가를 피한다.
    static void Call(BaseballGame game, string name, params object[] args)
    {
        typeof(BaseballGame).GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(game, args);
    }

    // 투수 준비 동작의 여러 시점을 고정해 이미지로 저장한다. UI 타이머에 따른 우연한 프레임을 사용하지 않는다.
    static void RenderPitcherPoses(string artifacts)
    {
        double[] times = { 0, 0.48, 0.58, 0.67, 0.76, 0.849, 0.85, 0.97, 1.10 };
        using (BaseballGame game = new BaseballGame())
        using (Bitmap frame = new Bitmap(game.Width, game.Height))
        using (Bitmap strip = new Bitmap(times.Length * 90, 90))
        using (Graphics source = Graphics.FromImage(frame))
        using (Graphics target = Graphics.FromImage(strip))
        {
            ((Timer)typeof(BaseballGame).GetField("timer", Private).GetValue(game)).Stop();
            BaseballSession state = (BaseballSession)typeof(BaseballGame).GetField("session", Private).GetValue(game);
            int pitcher = (int)typeof(BaseballGame).GetField("pitcherX", Private).GetValue(game);
            float scale = (float)typeof(BaseballGame).GetField("GameScale", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
            state.SetActive(true);
            for (int i = 0; i < times.Length; i++)
            {
                state.ResetRound();
                state.Advance(Math.Min(times[i], BaseballSession.ReadySeconds));
                if (times[i] > BaseballSession.ReadySeconds) state.Advance(times[i] - BaseballSession.ReadySeconds);
                source.Clear(Color.FromArgb(235, 238, 243));
                Call(game, "OnPaint", new PaintEventArgs(source, game.ClientRectangle));
                target.DrawImage(frame, new Rectangle(i * 90, 0, 90, 90),
                    new Rectangle((int)(pitcher * scale) - 45, frame.Height - 90, 90, 90), GraphicsUnit.Pixel);
            }
            strip.Save(Path.Combine(artifacts, "baseball-pitcher-poses.png"), ImageFormat.Png);
        }
    }

    // 실제 OnPaint 출력을 비트맵에 그리고 착지 결과 색상의 존재 여부를 검사한다.
    // 하단 게임 영역만 잘라 저장하여 검토할 화면 크기를 줄인다.
    static void Render(BaseballGame game, string path)
    {
        using (Bitmap bitmap = new Bitmap(game.Width, game.Height))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 238, 243));
            Call(game, "OnPaint", new PaintEventArgs(graphics, game.ClientRectangle));
            BaseballSession state = (BaseballSession)typeof(BaseballGame).GetField("session", Private).GetValue(game);
            // Result gold is used only for the landed shot's distance/grade and marker.
            int resultPixels = 0;
            int gold = Color.FromArgb(255, 233, 77).ToArgb();
            for (int y = bitmap.Height - 52; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == gold) resultPixels++;
            if (state.Phase == BaseballPhase.Flight) Assert(resultPixels == 0, "Distance/result appeared before landing.");
            if (state.Phase == BaseballPhase.Result && state.LastHit.Hit) Assert(resultPixels > 0, "Landing result was not rendered.");
            using (Bitmap crop = bitmap.Clone(new Rectangle(0, bitmap.Height - 190, bitmap.Width, 190), bitmap.PixelFormat))
                crop.Save(path, ImageFormat.Png);
        }
    }

    // 폼의 입력 이벤트·재그리기 요청·Esc 종료를 검사하고 투구/비행/결과 화면을 렌더링한다.
    static void RenderAndCheckWindow(string artifacts)
    {
        using (BaseballGame game = new BaseballGame())
        {
            Type type = typeof(BaseballGame);
            Timer timer = (Timer)type.GetField("timer", Private).GetValue(game);
            timer.Stop();
            BaseballSession session = (BaseballSession)type.GetField("session", Private).GetValue(game);
            IntPtr handle = game.Handle;
            session.SetActive(false);
            int invalidations = 0;
            game.Invalidated += delegate { invalidations++; };
            for (int i = 0; i < 120; i++) Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Inactive baseball repaints: invalidations=" + invalidations + ", interval=" + timer.Interval + ", active=" + session.Active + ", bounds=" + game.Bounds + ", work=" + Screen.PrimaryScreen.WorkingArea);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge < 0, "Inactive window accepted a swing.");
            session.SetActive(true);
            session.Advance(0.6);
            Render(game, Path.Combine(artifacts, "baseball-windup.png"));
            session.Advance(BaseballSession.ReadySeconds);
            session.Advance(session.PitchSeconds * 0.6);
            Render(game, Path.Combine(artifacts, "baseball-pitch.png"));
            session.Advance(session.PitchSeconds - BaseballSession.SwingContactDelay - session.Elapsed);
            type.GetField("lastTime", Private).SetValue(game, ((Stopwatch)type.GetField("clock", Private).GetValue(game)).Elapsed.TotalSeconds);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge == 0, "Space does not swing.");
            session.Advance(0.01);
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(session.SwingAge > 0, "Auto-repeat restarted swing.");
            session.Advance(BaseballSession.SwingContactDelay);
            Assert(session.Phase == BaseballPhase.Flight, "Timed window swing did not hit.");
            Render(game, Path.Combine(artifacts, "baseball-swing.png"));
            session.Advance(0.55);
            Render(game, Path.Combine(artifacts, "baseball-flight.png"));
            session.Advance(session.FlightSeconds * 0.84 - session.Elapsed);
            Render(game, Path.Combine(artifacts, "baseball-home-run.png"));
            session.Advance(session.FlightSeconds);
            Render(game, Path.Combine(artifacts, "baseball-result.png"));
            game.Width = 1366;
            Render(game, Path.Combine(artifacts, "baseball-result-1366.png"));
            typeof(Form).GetMethod("OnDeactivate", Private).Invoke(game, new object[] { EventArgs.Empty });
            Assert(!session.Active && timer.Interval == 250, "Focus event did not pause baseball.");
            bool closed = false;
            game.FormClosed += delegate { closed = true; };
            Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Escape));
            Assert(closed && game.IsDisposed, "Esc did not close baseball.");
        }
        Console.WriteLine("PASS: baseball window input, key-repeat protection, focus pause, 120 idle ticks, Esc and rendered pitch/flight/result views.");
    }
}

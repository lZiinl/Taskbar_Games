// 포트리스 창의 실제 배치와 입력/갱신 동작을 확인하는 회귀 테스트.
// 테스트용 생성자는 사용자 색상 설정을 읽거나 이전하지 않는다.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Fortress;

public static class FortressRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type GameType = typeof(FortressGame);

    // 매번 가능한 가장 왼쪽 과녁을 만들어 점수판과의 최소 여백이라는 최악 조건을 재현한다.
    sealed class LeftmostRandom : Random
    {
        // 난수 범위의 하한을 고정하여 배치 검증을 결정적으로 만든다.
        public override double NextDouble() { return 0; }
    }

    // 검증에 필요한 내부 필드를 읽는다. 운영 코드에 테스트 전용 공개 속성을 추가하지 않는다.
    static object Get(FortressGame game, string name)
    {
        return GameType.GetField(name, Private).GetValue(game);
    }

    // 해상도·점수·시간 등의 경계 조건을 직접 구성하는 테스트 도우미다.
    static void Set(FortressGame game, string name, object value)
    {
        GameType.GetField(name, Private).SetValue(game, value);
    }

    // 대상 클래스에 직접 선언된 비공개 메서드를 찾아 호출한다.
    static void Call(FortressGame game, string name, params object[] args)
    {
        GameType.GetMethod(name, Private | BindingFlags.DeclaredOnly).Invoke(game, args);
    }

    // 잘못된 상태를 예외로 알린다. 테스트 스크립트의 ErrorActionPreference=Stop과 함께 실패를 전달한다.
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // 논리 좌표를 실제 0.6배 화면 좌표로 변환해 과녁 외곽과 HUD 사이 여백을 검사한다.
    static void AssertClear(FortressGame game)
    {
        Rectangle panel = (Rectangle)Get(game, "panel");
        double targetLeft = Math.Round((float)Get(game, "targetX") * 0.6) - 12;
        double panelRight = Math.Round(panel.X * 0.6) + Math.Round(panel.Width * 0.6);
        Assert(targetLeft - panelRight >= 7, "Target overlaps score panel or loses its 8px margin.");
        Assert((float)Get(game, "targetX") * 0.6 + 12 <= game.ClientSize.Width, "Target leaves screen.");
    }

    // 실제 폼 렌더러를 사용하고 하단 일부를 PNG로 저장한다. 일반 데스크톱 캡처를 필요로 하지 않는다.
    static void Render(FortressGame game, string path)
    {
        using (Bitmap bitmap = new Bitmap(game.ClientSize.Width, game.ClientSize.Height))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(235, 238, 243));
            Call(game, "OnPaint", new PaintEventArgs(graphics, game.ClientRectangle));
            using (Bitmap crop = bitmap.Clone(new Rectangle(0, bitmap.Height - 80, 600, 80), bitmap.PixelFormat))
                crop.Save(path, ImageFormat.Png);
        }
    }

    // 6개 화면 폭과 5개 점수 자릿수에서 배치를 확인한다.
    // 대기 중 불필요한 재그리기, 충전·발사·파편·결과 전환 및 포커스 변화를 함께 검증한다.
    public static void Run(string artifacts)
    {
        Directory.CreateDirectory(artifacts);
        using (FortressGame game = (FortressGame)Activator.CreateInstance(GameType, Private, null, new object[] { false }, null))
        {
            Timer timer = (Timer)Get(game, "timer");
            timer.Stop();
            Set(game, "random", new LeftmostRandom());
            Set(game, "focused", true);
            foreach (int width in new int[] { 640, 800, 1024, 1366, 1920, 3840 })
            foreach (int count in new int[] { 0, 9, 99, 999, 9999 })
            {
                game.ClientSize = new Size(width, 768);
                Set(game, "ground", (768 - 2) / 0.6f);
                Set(game, "panel", new Rectangle(126, (int)((768 - 2) / 0.6f) - 24, 200, 24));
                Set(game, "hits", count);
                Set(game, "shots", count);
                Call(game, "UpdatePanelLayout");
                Call(game, "NewTarget");
                AssertClear(game);
                if (width == 1366 && count == 0) Render(game, Path.Combine(artifacts, "fortress-1366.png"));

                // Digit growth is reserved before aiming, not during flight.
                Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
                float aimedAt = (float)Get(game, "targetX");
                Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
                AssertClear(game);
                Assert((float)Get(game, "targetX") == aimedAt, "Target moved after firing.");
                Set(game, "flying", false);
            }
            Console.WriteLine("PASS: target clearance at six screen widths and five score digit boundaries.");

            Set(game, "hits", 0);
            Set(game, "shots", 0);
            Call(game, "ResetLayout");
            IntPtr handle = game.Handle;
            int invalidations = 0;
            game.Invalidated += delegate { invalidations++; };
            for (int i = 0; i < 120; i++) Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0, "Idle ticks repaint the screen.");
            Assert(timer.Interval == 250, "Idle timer is still running at animation speed.");

            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            Assert(timer.Interval == 16, "Charging does not wake animation timer.");
            invalidations = 0;
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 0.02);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && (double)Get(game, "charge") > 0, "Charging no longer animates.");

            Set(game, "charge", 1.0);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0 && timer.Interval == 250, "Full charge keeps repainting.");
            Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space));
            Assert((bool)Get(game, "flying") && timer.Interval == 16, "Full charge release does not start flight.");
            invalidations = 0;
            Set(game, "lastTime", ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds - 0.02);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && (double)Get(game, "flightTime") > 0, "Flight no longer animates.");

            double now = ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds;
            Call(game, "Finish", false, now);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations == 0, "Static MISS label keeps repainting.");
            Set(game, "resultUntil", -1.0);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert((string)Get(game, "result") == "" && invalidations > 0, "Result expiry does not repaint.");

            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            typeof(Form).GetMethod("OnDeactivate", Private).Invoke(game, new object[] { EventArgs.Empty });
            Assert(!(bool)Get(game, "charging") && (double)Get(game, "charge") == 0 && timer.Interval == 250,
                "Focus loss does not cancel charge and return to idle.");

            now = ((Stopwatch)Get(game, "clock")).Elapsed.TotalSeconds;
            Call(game, "Finish", true, now);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(invalidations > 0 && timer.Interval == 16, "Impact particles no longer animate.");
            Call(game, "UpdateDebris", 2.0);
            Set(game, "resultUntil", -1.0);
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(timer.Interval == 250, "Completed effects do not return to idle.");

            // The low-frequency poll must still detect display/work-area changes.
            game.Bounds = new Rectangle(game.Left, game.Top, game.Width - 1, game.Height);
            invalidations = 0;
            Call(game, "Tick", game, EventArgs.Empty);
            Assert(game.Bounds == Screen.PrimaryScreen.WorkingArea && invalidations > 0,
                "Idle work-area changes are not handled.");
            Console.WriteLine("PASS: 120 idle ticks with zero redraws; charge, full charge, flight, results, focus loss, particles and work-area reset.");
            timer.Stop();
        }
    }
}

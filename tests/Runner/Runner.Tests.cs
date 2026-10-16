// 공룡 러너의 물리·입력 규칙·가속·랜덤 간격·창 배치를 검증한다.
// 고정 시드로 재현성을 확보하고 실제 대기 없이 모델 시간을 진행해 장시간 플레이를 검사한다.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Runner;

public static class RunnerRegressionTests
{
    // 실패 원인을 예외 메시지로 남겨 테스트 실행 스크립트가 즉시 중단되게 한다.
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    // 비활성 점프 차단, 이중 점프 차단, 키를 뗀 시점별 높이, 충돌/재시작/최고 점수를 검증한다.
    // 마지막에는 실제 폼의 OnPaint를 호출해 흑백 러너 미리보기를 저장한다.
    public static void Run(string artifacts)
    {
        RunnerSession paused = new RunnerSession(new Random(1));
        Assert(!paused.Jump(), "Inactive runner must ignore jumps.");
        paused.Active = true; paused.Jump();
        Assert(!paused.Jump(), "Jump must not stack before the first frame.");
        paused.Advance(0.1, 600); paused.Active = false;
        double height = paused.Height, distance = paused.Distance;
        paused.Advance(2, 600);
        Assert(paused.Height == height && paused.Distance == distance, "Inactive runner must pause.");
        RunnerSession shortJump = new RunnerSession(new Random(2));
        RunnerSession fullJump = new RunnerSession(new Random(2));
        shortJump.Active = fullJump.Active = true;
        shortJump.Jump(); fullJump.Jump(); shortJump.ReleaseJump();
        shortJump.Advance(0.2, 600); fullJump.Advance(0.2, 600);
        Assert(fullJump.Height > shortJump.Height + 15, "Holding jump must reach higher than tapping.");
        double baseline = -1;
        RunnerSession jumping = new RunnerSession(new Random(7)); jumping.Active = true; jumping.Jump();
        double smallestGap = Double.MaxValue, largestGap = 0;
        Assert(jumping.Speed == RunnerSession.InitialSpeed, "Runner should start at its initial speed.");
        // 210초를 시뮬레이션해 180초 가속 이후 최고 속도 구간까지 검사한다.
        // 장애물 접근 시간을 속도에 맞춰 계산하는 자동 점프로 최소 간격의 통과 가능성을 확인한다.
        for (int i = 0; i < 60 * 210; i++)
        {
            foreach (RunnerObstacle obstacle in jumping.Obstacles)
                if (obstacle.X + obstacle.Width > 46 && obstacle.X > 60 && obstacle.X < 61 + jumping.Speed * 0.26 && jumping.Height == 0)
                { jumping.Jump(); break; }
            jumping.Advance(1.0 / 60, 1280);
            if (i == 60 * 90 - 1) Assert(Math.Abs(jumping.Speed - 350) < 0.02, "Halfway through the ramp speed must be 350 pixels per second.");
            if (i == 60 * 180 - 1) Assert(Math.Abs(jumping.Speed - 560) < 0.02, "Runner should reach four times initial speed at 180 seconds.");
            for (int j = 1; j < jumping.Obstacles.Count; j++)
            {
                double gap = jumping.Obstacles[j].X - jumping.Obstacles[j-1].X - jumping.Obstacles[j-1].Width;
                Assert(gap >= RunnerSession.MinimumGap - 2, "Random gaps must allow landing and the next jump at top speed.");
                smallestGap = Math.Min(smallestGap, gap); largestGap = Math.Max(largestGap, gap);
            }
        }
        Assert(jumping.Phase == RunnerPhase.Running && jumping.Speed == RunnerSession.MaximumSpeed, "Well-timed jumps must clear obstacles all the way to maximum speed.");
        Assert(largestGap - smallestGap > 100, "Obstacle gaps should visibly vary.");
        // 같은 시드와 입력에서 프레임 간격만 바꾸어 고정 단계 물리가 비슷한 충돌 위치를 내는지 비교한다.
        foreach (int fps in new int[] { 30, 60, 144 })
        {
            RunnerSession game = new RunnerSession(new Random(3)); game.Active = true; game.Jump();
            for (int i = 0; i < fps * 15 && game.Phase == RunnerPhase.Running; i++) game.Advance(1.0 / fps, 480);
            Assert(game.Phase == RunnerPhase.GameOver && game.Best > 0, "A grounded dinosaur must collide and record score.");
            if (baseline < 0) baseline = game.Distance;
            else Assert(Math.Abs(game.Distance - baseline) < 1, "Collision must not depend on frame rate.");
            int best = game.Best;
            Assert(game.Jump() && game.Score == 0 && game.Best == best && game.Obstacles.Count == 0, "Restart should clear course but keep best.");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (RunnerGame form = new RunnerGame())
        {
            RunnerSession game = (RunnerSession)typeof(RunnerGame).GetField("session", flags).GetValue(form);
            Assert(form.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom && form.Height == 100 && form.Width == Screen.PrimaryScreen.WorkingArea.Width / 2,
                "Runner must align to taskbar and use half the screen width.");
            game.Active = true; game.Jump();
            for (int i = 0; i < 350; i++) game.Advance(0.01, form.Width);
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(225,231,239));
                typeof(RunnerGame).GetMethod("OnPaint", flags).Invoke(form, new object[] { new PaintEventArgs(g, form.ClientRectangle) });
                bitmap.Save(Path.Combine(artifacts, "runner.png"), ImageFormat.Png);
            }
        }
        Console.WriteLine("PASS: runner pause, variable jump, collision at 30/60/144 FPS, restart, best score and rendered view.");
    }
}

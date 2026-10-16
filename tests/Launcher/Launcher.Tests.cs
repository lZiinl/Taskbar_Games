// 선택창의 배치·게임 단일 실행·복귀·종료를 검증하는 회귀 테스트.
// 테스트용 창은 실제 화면에 표시하지 않고 메시지 처리/그리기 진입점을 호출한다.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Launcher;

public static class LauncherRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    // 표시 요청 여부는 기록하되 실제 창은 숨겨 자동 테스트가 사용자 화면을 가리지 않게 한다.
    sealed class HiddenLauncher : LauncherForm
    {
        public bool RequestedVisible;
        // 키 처리 경로를 직접 호출하여 자식 버튼 포커스와 관계없이 Esc가 종료로 이어지는지 검사한다.
        public bool PressEscape()
        {
            Message message = Message.Create(Handle, 0x100, (IntPtr)Keys.Escape, IntPtr.Zero);
            return ProcessCmdKey(ref message, Keys.Escape);
        }
        protected override void SetVisibleCore(bool value) { RequestedVisible = value; base.SetVisibleCore(false); }
    }
    // 핸들은 생성해 Close/Dispose 생명주기를 실행하되 화면 표시만 억제하는 대역 게임이다.
    sealed class HiddenGame : Form
    {
        public HiddenGame() { IntPtr handle = Handle; }
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
    }
    // 조건 실패는 예외로 전달해 PowerShell 실행 스크립트를 실패시킨다.
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    // 운영 코드의 공개 API를 늘리지 않고 internal OpenGame을 Reflection으로 호출한다.
    static void Open(LauncherForm launcher, Func<Form> factory)
    {
        typeof(LauncherForm).GetMethod("OpenGame", Private).Invoke(launcher, new object[] { factory });
    }
    // 현재 게임 소유권을 검사하기 위해 private activeGame 참조를 읽는다.
    static object Active(LauncherForm launcher) { return typeof(LauncherForm).GetField("activeGame", Private).GetValue(launcher); }

    // 아이콘/종료 버튼 수와 작업표시줄 정렬을 확인하고 선택창 미리보기를 저장한다.
    // 중복 생성 차단 → 게임 종료 후 복귀 → 재선택 → 앱 종료 및 Esc/종료 버튼 경로를 검증한다.
    public static void Run(string artifacts)
    {
        using (HiddenLauncher launcher = new HiddenLauncher())
        {
            IntPtr handle = launcher.Handle;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Assert(launcher.Controls.Count == 5 && launcher.Controls["ExitButton"] != null, "Launcher must offer four games and exit.");
            Assert(launcher.ClientSize == new Size(58, 74) && launcher.Right == work.Right - 8 && launcher.Bottom == work.Bottom - 8,
                "Compact launcher is not anchored above the taskbar's right edge: size=" + launcher.ClientSize + ", bounds=" + launcher.Bounds + ", work=" + work);
            using (Bitmap preview = new Bitmap(launcher.ClientSize.Width, launcher.ClientSize.Height))
            {
                launcher.DrawToBitmap(preview, launcher.ClientRectangle);
                // Hidden test forms do not paint their children through DrawToBitmap.
                // Render the actual card painters separately into their client bounds.
                using (Graphics canvas = Graphics.FromImage(preview))
                    foreach (Control card in launcher.Controls)
                        using (Bitmap cardImage = new Bitmap(card.Width, card.Height))
                        using (Graphics cardGraphics = Graphics.FromImage(cardImage))
                        {
                            card.GetType().GetMethod("OnPaint", Private).Invoke(card,
                                new object[] { new PaintEventArgs(cardGraphics, card.ClientRectangle) });
                            canvas.DrawImageUnscaled(cardImage, card.Location);
                        }
                preview.Save(Path.Combine(artifacts, "launcher.png"), ImageFormat.Png);
            }
            HiddenGame first = new HiddenGame();
            Open(launcher, delegate { return first; });
            Assert(Object.ReferenceEquals(first, Active(launcher)) && !launcher.RequestedVisible, "Launcher did not own game and hide.");
            int creations = 0;
            Open(launcher, delegate { creations++; return new HiddenGame(); });
            Assert(creations == 0, "Second game was allowed to start.");
            first.Close();
            Assert(first.IsDisposed && Active(launcher) == null && launcher.RequestedVisible, "Closing game did not return to selection.");
            HiddenGame second = new HiddenGame();
            Open(launcher, delegate { return second; });
            Assert(Object.ReferenceEquals(second, Active(launcher)), "Second selection did not start after first closed.");
            launcher.Close();
            Assert(second.IsDisposed && launcher.IsDisposed, "Application shutdown left game alive.");
        }
        using (HiddenLauncher launcher = new HiddenLauncher())
        {
            IntPtr handle = launcher.Handle;
            launcher.ActiveControl = launcher.Controls[0];
            Assert(launcher.PressEscape() && launcher.IsDisposed, "Escape with a selected game button must close launcher.");
        }
        using (HiddenLauncher launcher = new HiddenLauncher())
        {
            IntPtr handle = launcher.Handle;
            Control exit = launcher.Controls["ExitButton"];
            typeof(Control).GetMethod("OnClick", Private).Invoke(exit, new object[] { EventArgs.Empty });
            Assert(launcher.IsDisposed, "Exit button must close launcher.");
        }
        Console.WriteLine("PASS: single active game, launcher hide/return, reselection, exit button and Escape shutdown; launcher preview rendered.");
    }
}

// 실행 진입점. 같은 Windows 로그인 세션에서는 게임 모음 하나만 실행한다.
// LauncherForm이 메시지 루프의 주 창이며, 개별 게임 창은 런처가 소유한다.

using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TaskbarGames.Launcher;
using TaskbarGames.Shared;

[assembly: AssemblyTitle("Taskbar_Games")]
[assembly: AssemblyProduct("Taskbar_Games")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: AssemblyInformationalVersion(TaskbarGames.Shared.ReleaseInfo.Version)]

namespace TaskbarGames
{
    internal static class Program
    {
        [STAThread]
        // WinForms/대화상자에 필요한 STA 스레드에서 시작한다.
        // Mutex의 소유 여부로 중복 실행을 판별하고, 주 창이 닫혀 루프를 빠져나가면 소유권을 해제한다.
        static void Main()
        {
            bool firstInstance;
            // Local 네임스페이스는 다른 사용자 세션까지 실행을 막지 않는다.
            // 이름은 실행 파일 경로와 무관하므로 같은 세션에서 복사본을 실행해도 중복으로 판단한다.
            using (Mutex instance = new Mutex(true, @"Local\Taskbar_Games", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Taskbar_Games가 이미 실행 중입니다. 작업표시줄에서 열려 있는 창을 선택해 주세요.",
                        "Taskbar_Games", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    Application.EnableVisualStyles();
                    System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                    Application.SetCompatibleTextRenderingDefault(false);
                    // 이 호출은 런처가 종료될 때까지 UI 이벤트를 처리한다.
                    // 게임 창만 닫는 경우 런처가 다시 표시되므로 애플리케이션은 계속 실행된다.
                    GameEnvironment.Store = GameStore.OpenDefault();
                    GameEnvironment.Display.Select(GameEnvironment.Store.Data.MonitorDevice);
                    using (MonitorSelectionForm monitor = new MonitorSelectionForm())
                        if (monitor.ShowDialog() != DialogResult.OK) return;
                    Application.Run(new LauncherForm());
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}

using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using TaskbarGames.Launcher;

[assembly: AssemblyTitle("Taskbar_Games")]
[assembly: AssemblyProduct("Taskbar_Games")]
[assembly: AssemblyVersion("0.2.0.0")]

namespace TaskbarGames
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool firstInstance;
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
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new LauncherForm());
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}

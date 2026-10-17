using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGames.Shared
{
    public sealed class DisplayInfo
    {
        public readonly string DeviceName;
        public readonly Rectangle WorkingArea;
        public readonly bool Primary;
        public DisplayInfo(string deviceName, Rectangle workingArea, bool primary)
        { DeviceName = deviceName; WorkingArea = workingArea; Primary = primary; }
    }

    // 매번 현재 목록에서 장치 이름을 해석한다. 분리된 모니터는 기본 모니터로 안전하게 대체한다.
    public sealed class DisplaySelection
    {
        readonly Func<DisplayInfo[]> screens;
        public string DeviceName { get; private set; }
        public DisplaySelection() : this(delegate {
            return Array.ConvertAll(Screen.AllScreens, s => new DisplayInfo(s.DeviceName, s.WorkingArea, s.Primary));
        }) { }
        public DisplaySelection(Func<DisplayInfo[]> source) { screens = source; DeviceName = ""; }
        public DisplayInfo[] Screens { get { return screens(); } }
        public void Select(string name) { DeviceName = name ?? ""; }
        public DisplayInfo Current
        {
            get
            {
                DisplayInfo[] all = Screens;
                foreach (DisplayInfo screen in all) if (screen.DeviceName == DeviceName) return screen;
                foreach (DisplayInfo screen in all) if (screen.Primary) return screen;
                if (all.Length > 0) return all[0];
                throw new InvalidOperationException("No display available.");
            }
        }
        public Rectangle WorkingArea { get { return Current.WorkingArea; } }
    }

    public static class GameEnvironment
    {
        public static GameStore Store = new GameStore();
        public static DisplaySelection Display = new DisplaySelection();
        public static readonly Size SelectionSize = new Size(58, 74);
        public static Point SelectionLocation(Size size)
        {
            Rectangle area = Display.WorkingArea;
            return new Point(Math.Max(area.Left, area.Right - size.Width - 8), Math.Max(area.Top, area.Bottom - size.Height - 8));
        }
    }
}

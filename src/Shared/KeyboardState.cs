using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarGames.Shared
{
    internal static class KeyboardState
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);

        public static bool SpaceDown { get { return (GetAsyncKeyState((int)Keys.Space) & 0x8000) != 0; } }
    }
}

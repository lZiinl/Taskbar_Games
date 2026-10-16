// 포커스를 되찾는 순간의 실제 키 상태를 읽는 Windows 입력 보조 코드.
// 키 이벤트만으로는 다른 앱에서 이미 누르고 있던 키를 구분할 수 없어 이 상태를 함께 사용한다.

using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarGames.Shared
{
    internal static class KeyboardState
    {
        [DllImport("user32.dll")]
        // user32의 키 상태 조회. 반환값 최상위 비트가 1이면 현재 물리적으로 눌린 상태다.
        // 이전 호출 이후의 눌림을 뜻하는 최하위 비트는 여기서 사용하지 않는다.
        static extern short GetAsyncKeyState(int key);

        // 게임 재활성화 때 SpaceDown이면 먼저 키를 떼도록 입력 잠금을 건다.
        // 다른 프로그램에서 입력하던 스페이스가 곧바로 발사·스윙·점프로 이어지는 것을 막는다.
        public static bool SpaceDown { get { return (GetAsyncKeyState((int)Keys.Space) & 0x8000) != 0; } }
    }
}

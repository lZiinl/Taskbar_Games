// 도토리 배치용 임시 입력 창과 배치된 도토리 하나를 표시하는 창.
// 화면 전체 색을 바꾸지 않고 투명색 영역을 사용하며, 마우스 캡처는 배치 중에만 유지한다.

using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Pet
{
    // A temporary selection surface; no global mouse hook or input capture after placement.
    internal sealed class AcornPlacement : Form
    {
        // 선택 결과는 클라이언트 상대 좌표가 아닌 화면 X좌표다. PetGame이 접근 가능한 범위로 보정한다.
        internal int SelectedX { get; private set; }
        int previewX;
        readonly Timer displayTimer = new Timer();
        // 작업 영역 하단의 24px 투명 띠를 생성한다. 커서 X만 위치 선택에 사용한다.
        // 왼쪽 클릭은 확정, 다른 마우스 버튼은 취소이며 포커스를 잃어도 닫는다.
        public AcornPlacement()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Rectangle area = GameEnvironment.Display.WorkingArea;
            MinimumSize = MaximumSize = new Size(area.Width, 24);
            Bounds = new Rectangle(area.Left, area.Bottom - 24, area.Width, 24);
            BackColor = TransparencyKey = Color.Magenta;
            TopMost = true;
            ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross;
            previewX = PointToClient(Cursor.Position).X;
            MouseMove += delegate(object sender, MouseEventArgs e) { previewX = e.X; Invalidate(); };
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { SelectedX = Left + e.X; DialogResult = DialogResult.OK; }
                else DialogResult = DialogResult.Cancel;
                Close();
            };
            Deactivate += delegate { Close(); };
            displayTimer.Interval = 250;
            displayTimer.Tick += delegate {
                Rectangle current = GameEnvironment.Display.WorkingArea;
                if (Left != current.Left || Bottom != current.Bottom || Width != current.Width)
                { DialogResult = DialogResult.Cancel; Close(); }
            };
            displayTimer.Start();
        }
        // 창이 활성화된 뒤 캡처를 시작해야 배경의 투명한 부분이나 창 밖의 클릭도 받을 수 있다.
        // 따라서 클릭의 Y와 무관하게 원하는 가로 위치를 선택할 수 있다.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Capture only for this explicit placement step. The transparent background
            // does not tint the desktop, and clicks still select a horizontal position.
            Capture = true;
        }
        // 확정·취소·포커스 상실의 모든 종료 경로에서 마우스 캡처를 해제한다.
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Capture = false;
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) displayTimer.Dispose(); base.Dispose(disposing); }
        // Esc는 도토리 배치만 취소한다. 부모인 다람쥐 게임까지 종료하지 않는다.
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        { if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; } return base.ProcessCmdKey(ref msg, keys); }
        // 다람쥐의 중심 오프셋 38px를 기준으로 미리보기를 제한한다.
        // 배경 안내선이나 반투명 덮개는 그리지 않고 도토리 모양만 표시한다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int bounded = Math.Max(38, Math.Min(Width - 38, previewX));
            AcornMarker.DrawAcorn(e.Graphics, bounded - 5, Height - 12);
        }
    }
    // 도토리마다 하나씩 소유되는 작은 표시 창. 먹거나 게임이 닫힐 때 Dispose한다.
    internal sealed class AcornMarker : Form
    {
        // 16×16 크기를 고정해 네이티브 창 생성 시 자동 최소 크기 확장으로 좌표가 틀어지는 것을 막는다.
        public AcornMarker()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            MinimumSize = MaximumSize = new Size(16, 16);
            ClientSize = new Size(16, 16); BackColor = TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true;
        }
        // 먹이를 추가해도 현재 활성 게임이나 업무 앱에서 포커스를 빼앗지 않는다.
        protected override bool ShowWithoutActivation { get { return true; } }
        // WS_EX_NOACTIVATE는 클릭 활성화를 막고, WS_EX_TRANSPARENT는 형제 창과의 그리기 순서를 조정한다.
        // 이 스타일만으로 모든 입력이 통과한다고 가정하지 않으며 실제 표시 영역 자체를 작게 유지한다.
        protected override CreateParams CreateParams
        { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x20; return cp; } }
        // 배치 미리보기·메뉴 아이콘·실제 도토리에 공통으로 사용하는 픽셀 그림이다.
        internal static void DrawAcorn(Graphics g, int x, int y)
        {
            using (SolidBrush cap = new SolidBrush(Color.FromArgb(119, 72, 46)))
            using (SolidBrush nut = new SolidBrush(Color.FromArgb(245, 184, 115)))
            {
                g.FillRectangle(cap, x + 4, y, 2, 2); g.FillRectangle(cap, x + 1, y + 2, 9, 3);
                g.FillRectangle(nut, x + 2, y + 5, 7, 4); g.FillRectangle(nut, x + 3, y + 9, 5, 1);
                g.FillRectangle(Brushes.Bisque, x + 3, y + 5, 2, 2);
            }
        }
        // 16px 창 안에서 아래쪽으로 배치해 작업표시줄 위 지면에 맞춘다.
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); DrawAcorn(e.Graphics, 2, 4); }
    }
}

// 검정·형광색 공통 UI 테마와 12색 탱크 팔레트.
// 공룡 러너의 실제 게임 화면은 별도 흑백 렌더링을 사용하며 공통 앱 아이콘만 공유한다.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGames.Shared
{
    internal static class ArcadeTheme
    {
        // 실행 파일에 포함된 아이콘을 한 번 읽어 모든 게임 창에서 공유한다.
        // 공유 객체이므로 각 창이 이 정적 Icon을 개별 Dispose하지 않는다.
        public static readonly Icon AppIcon = LoadIcon();
        // 빌드 스크립트가 TaskbarGames.ico 이름으로 넣은 리소스를 읽는다.
        // 스트림을 닫아도 아이콘이 유효하도록 복제본을 반환한다.
        static Icon LoadIcon()
        {
            using (System.IO.Stream stream = typeof(ArcadeTheme).Assembly.GetManifestResourceStream("TaskbarGames.ico"))
            using (Icon source = new Icon(stream)) return (Icon)source.Clone();
        }
        public static readonly Color Black = Color.FromArgb(8, 9, 14);
        public static readonly Color Panel = Color.FromArgb(17, 20, 29);
        public static readonly Color Neon = Color.FromArgb(85, 255, 160);
        public static readonly Color Cyan = Color.FromArgb(66, 232, 255);
        public static readonly Color Pink = Color.FromArgb(255, 91, 183);
        // 문자 메뉴와 이미지 도구막대가 같은 렌더러를 쓰게 설정한다.
        // 이미지 여백 옵션은 ContextMenuStrip에만 있으므로 형식 확인 후 적용한다.
        public static void Style(ToolStripDropDown menu)
        {
            menu.Renderer = new ArcadeMenuRenderer();
            menu.BackColor = Black; menu.ForeColor = Neon;
            ContextMenuStrip context = menu as ContextMenuStrip;
            if (context != null) context.ShowImageMargin = false;
            menu.Padding = new Padding(5);
            menu.Font = new Font("Malgun Gothic", 9f);
        }
        // Windows 기본 그라데이션·둥근 외곽 대신 픽셀 게임과 어울리는 단색 배경/테두리를 사용한다.
        sealed class ArcadeMenuRenderer : ToolStripProfessionalRenderer
        {
            // 모서리를 둥글게 깎지 않아 작은 메뉴의 테두리가 또렷하게 이어지도록 한다.
            public ArcadeMenuRenderer() { RoundedEdges = false; }
            // 메뉴 전체를 공통 검정색으로 지운다. 항목 호버 배경은 별도 단계에서 그린다.
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { e.Graphics.Clear(Black); }
            // 메뉴 경계 안쪽에 형광색 1픽셀 테두리를 그린다.
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { using (Pen p = new Pen(Neon)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
            // 선택된 활성 항목만 어두운 초록색으로 채운다. 비활성 항목은 강조하지 않는다.
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected && e.Item.Enabled)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 64, 48))) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
            }
            // 다람쥐 이미지 버튼도 일반 메뉴 항목과 같은 호버 배경을 사용한다.
            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            { OnRenderMenuItemBackground(e); }
            // 활성/선택/비활성 색상을 직접 지정해 OS 기본 비활성 글자색에 덮이지 않게 한다.
            // 한글 설명은 픽셀 글꼴 대신 TextRenderer로 출력한다.
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color = e.Item.Enabled ? (e.Item.Selected ? Color.White : Neon) : Cyan;
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
            }
            // 양옆 여백을 둔 짧은 구분선. 메뉴의 작은 폭을 유지하면서 항목 묶음을 나눈다.
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            { using (Pen p = new Pen(Color.FromArgb(42, 81, 66))) e.Graphics.DrawLine(p, 7, 3, e.Item.Width - 8, 3); }
        }
    }

    // 4열×3행 색상 선택 대화상자. 선택하면 OK와 SelectedColor를 반환하고 취소는 현재 색을 유지한다.
    internal sealed class TankPalette : Form
    {
        // 선택 가능한 12가지 대표색. 순수 마젠타(투명색)는 포함하지 않는다.
        internal static readonly Color[] Colors = {
            Color.FromArgb(85,255,160), Color.FromArgb(174,255,64), Color.FromArgb(255,233,77), Color.FromArgb(255,156,61),
            Color.FromArgb(255,85,104), Color.FromArgb(255,91,183), Color.FromArgb(190,113,255), Color.FromArgb(108,132,255),
            Color.FromArgb(66,232,255), Color.FromArgb(44,191,181), Color.FromArgb(238,243,255), Color.FromArgb(136,150,174)
        };
        internal Color SelectedColor { get; private set; }
        // 92×82px로 네이티브 최소/최대 크기를 고정해 표시 시 불필요한 오른쪽 여백이 생기지 않게 한다.
        // 커서가 있는 모니터의 작업 영역 안으로 위치를 제한하며 현재 색과 같은 칸을 표시한다.
        public TankPalette(Color current)
        {
            SelectedColor = current;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            // Pin native tracking limits too; Windows otherwise expands tiny forms when their handle is created.
            MinimumSize = MaximumSize = new Size(92, 82);
            ClientSize = new Size(92, 82); BackColor = ArcadeTheme.Black;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(Cursor.Position.X, work.Right - Width)),
                Math.Max(work.Top, Math.Min(Cursor.Position.Y - Height, work.Bottom - Height)));
            for (int i = 0; i < Colors.Length; i++)
            {
                // 각 반복의 색을 지역 변수로 복사해 Click 클로저가 해당 칸의 색을 기억하게 한다.
                Color color = Colors[i];
                Swatch button = new Swatch(color, current.ToArgb() == color.ToArgb());
                button.Bounds = new Rectangle(5 + i % 4 * 21, 16 + i / 4 * 16, 19, 14);
                button.AccessibleName = "탱크 색상 " + (i + 1);
                button.Click += delegate { SelectedColor = color; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(button);
            }
            Button cancel = new PaletteCancel { Bounds = new Rectangle(5, 66, 82, 12), DialogResult = DialogResult.Cancel };
            Controls.Add(cancel); CancelButton = cancel;
            Deactivate += delegate { if (!IsDisposed) Close(); };
        }
        // 작은 타이틀과 외곽선만 그린다. 색상 칸과 닫기는 자식 컨트롤이 그린다.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(ArcadeTheme.Neon)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            PixelArt.Text(e.Graphics, "TANK COLOR", ArcadeTheme.Neon, 6, 5);
        }
        // 12픽셀 높이에서도 읽을 수 있는 픽셀 CLOSE / ESC 버튼. 기본 Button 입력 동작을 유지한다.
        sealed class PaletteCancel : Button
        {
            // 문자 그림과 별개로 스크린 리더가 알 수 있는 한국어 이름을 제공한다.
            public PaletteCancel() { AccessibleName = "닫기 (Esc)"; Cursor = Cursors.Hand; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(ArcadeTheme.Panel);
                using (Pen p = new Pen(Focused ? Color.White : ArcadeTheme.Pink)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                PixelArt.Text(e.Graphics, "CLOSE / ESC", ArcadeTheme.Pink, 8, 3);
            }
        }
        // 색상 칸. 선택된 현재 색은 + 표시, 키보드 포커스는 흰 테두리로 구분한다.
        sealed class Swatch : Button
        {
            readonly Color color; readonly bool selected;
            // 선택 후보 색과 현재 설정과의 일치 여부를 캡처한다. 클릭 결과는 부모 팔레트가 처리한다.
            public Swatch(Color value, bool active) { color = value; selected = active; Cursor = Cursors.Hand; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(ArcadeTheme.Black);
                using (SolidBrush b = new SolidBrush(color)) e.Graphics.FillRectangle(b, 3, 3, Width - 6, Height - 6);
                using (Pen p = new Pen(Focused ? Color.White : selected ? ArcadeTheme.Neon : ArcadeTheme.Panel))
                    e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
                if (selected) PixelArt.Text(e.Graphics, "+", ArcadeTheme.Black, Width / 2 - 2, Height / 2 - 3);
            }
        }
    }
}

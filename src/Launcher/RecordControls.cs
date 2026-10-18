using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Launcher
{
    // 모든 작은 창과 아이콘 메뉴에서 같은 비활성 설명 패널을 사용한다.
    public sealed class ArcadeHints : IDisposable
    {
        object activeSource;
        void HideAll() { activeSource = null; Popup.Hide(); }
        // WinForms는 새 버튼의 Enter 뒤에 이전 버튼의 Leave를 보낼 수 있다.
        // 이전 버튼이 지금 표시 중인 다른 버튼의 안내까지 닫지 않도록 소유자를 확인한다.
        void HideSource(object source) { if (Object.ReferenceEquals(activeSource, source)) HideAll(); }
        sealed class Binding
        {
            public Control Control; public string Text;
            public EventHandler Enter, Leave; public MouseEventHandler Down;
        }
        readonly List<Binding> bindings = new List<Binding>();
        readonly Dictionary<Form, EventHandler> owners = new Dictionary<Form, EventHandler>();
        readonly List<Action> detachMenus = new List<Action>();
        public readonly RecordInfoPopup Popup = new RecordInfoPopup();
        void Watch(Form owner)
        {
            if (owners.ContainsKey(owner)) return;
            EventHandler hide = delegate { HideAll(); };
            owners.Add(owner, hide); owner.VisibleChanged += hide; owner.LocationChanged += hide;
        }
        public void SetToolTip(Control control, string text)
        {
            foreach (Binding existing in bindings) if (existing.Control == control) { existing.Text = text; return; }
            Binding binding = new Binding { Control = control, Text = text };
            binding.Enter = delegate {
                Form owner = control.FindForm(); if (owner == null || owner.IsDisposed) return;
                Watch(owner); Popup.Present(owner, "HELP", binding.Text, ArcadeTheme.Cyan);
                activeSource = control;
            };
            binding.Leave = delegate { HideSource(control); }; binding.Down = delegate { HideSource(control); };
            control.MouseEnter += binding.Enter; control.MouseLeave += binding.Leave; control.MouseDown += binding.Down;
            bindings.Add(binding);
        }
        public void AttachMenu(Form owner, ToolStripDropDown menu)
        {
            Watch(owner); menu.ShowItemToolTips = false;
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripItem target = item;
                EventHandler enter = delegate { ShowMenuHint(owner, menu, target); };
                EventHandler leave = delegate { HideSource(target); };
                target.MouseEnter += enter; target.MouseLeave += leave;
                detachMenus.Add(delegate { target.MouseEnter -= enter; target.MouseLeave -= leave; });
            }
            MouseEventHandler move = delegate(object sender, MouseEventArgs e) {
                ToolStripItem item = null;
                foreach (ToolStripItem candidate in menu.Items)
                    if (candidate.Available && candidate.Bounds.Contains(e.Location)) { item = candidate; break; }
                ShowMenuHint(owner, menu, item);
            };
            EventHandler hide = delegate { if (!menu.ClientRectangle.Contains(menu.PointToClient(Cursor.Position))) HideAll(); };
            ToolStripDropDownClosedEventHandler closed = delegate { HideAll(); };
            menu.MouseMove += move; menu.MouseLeave += hide; menu.Closed += closed;
            detachMenus.Add(delegate { menu.MouseMove -= move; menu.MouseLeave -= hide; menu.Closed -= closed; });
        }
        public void ShowMenuHint(Form owner, ToolStripDropDown menu, ToolStripItem item)
        {
            if (item == null || String.IsNullOrEmpty(item.ToolTipText)) { HideAll(); return; }
            Popup.Present(owner, menu.Bounds, "HELP", item.ToolTipText, ArcadeTheme.Cyan);
            activeSource = item;
        }
        public void RemoveAll()
        {
            HideAll();
            foreach (Binding binding in bindings) {
                binding.Control.MouseEnter -= binding.Enter; binding.Control.MouseLeave -= binding.Leave; binding.Control.MouseDown -= binding.Down;
            }
            bindings.Clear();
        }
        public void Dispose()
        {
            RemoveAll(); foreach (Action detach in detachMenus) detach(); detachMenus.Clear();
            foreach (KeyValuePair<Form, EventHandler> owner in owners) {
                owner.Key.VisibleChanged -= owner.Value; owner.Key.LocationChanged -= owner.Value;
            }
            owners.Clear(); Popup.Dispose();
        }
    }

    // 목록과 스크롤을 함께 그려 Windows 기본 스크롤바가 나타나지 않게 한다.
    public sealed class ArcadeList : Control
    {
        public readonly List<object> Items = new List<object>();
        public Func<object, Color> ItemColor = item => Color.White;
        public const int ItemHeight = 16;
        int selected = -1, top, dragOffset = -1, wheel;
        public event EventHandler SelectedIndexChanged, Scrolled;
        public int VisibleRows { get { return Math.Max(1, Height / ItemHeight); } }
        public int MaximumTop { get { return Math.Max(0, Items.Count - VisibleRows); } }
        public int TopIndex { get { return top; } set { SetTop(value); } }
        public object SelectedItem { get { return selected >= 0 && selected < Items.Count ? Items[selected] : null; } }
        public int SelectedIndex
        {
            get { return selected; }
            set {
                selected = value >= 0 && value < Items.Count ? value : -1;
                if (selected >= 0) SetTop(selected < top ? selected : selected >= top + VisibleRows ? selected - VisibleRows + 1 : top);
                else SetTop(top);
                Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        public Rectangle ScrollTrack { get { return new Rectangle(Width - 10, 11, 9, Math.Max(1, Height - 22)); } }
        public Rectangle ScrollThumb
        {
            get {
                Rectangle track = ScrollTrack;
                int height = Math.Min(track.Height, Math.Max(9, track.Height * VisibleRows / Math.Max(1, Items.Count)));
                int offset = MaximumTop == 0 ? 0 : (track.Height - height) * top / MaximumTop;
                return new Rectangle(track.X + 1, track.Y + offset, track.Width - 2, height);
            }
        }
        public ArcadeList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true; BackColor = ArcadeTheme.Panel; AccessibleName = "칭호 목록";
        }
        void SetTop(int value)
        {
            int next = Math.Max(0, Math.Min(MaximumTop, value));
            if (top == next) return;
            top = next; Invalidate(); if (Scrolled != null) Scrolled(this, EventArgs.Empty);
        }
        public Rectangle GetItemRectangle(int index) { return new Rectangle(0, (index - top) * ItemHeight, Width - 13, ItemHeight); }
        public int IndexFromPoint(Point point)
        {
            if (point.X < 0 || point.X >= Width - 13 || point.Y < 0 || point.Y >= VisibleRows * ItemHeight) return -1;
            int index = top + point.Y / ItemHeight; return index < Items.Count ? index : -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            for (int i = top; i < Math.Min(Items.Count, top + VisibleRows); i++)
            {
                Rectangle row = GetItemRectangle(i);
                if (i == selected) using (Brush fill = new SolidBrush(Color.FromArgb(29, 47, 43))) e.Graphics.FillRectangle(fill, row);
                TextRenderer.DrawText(e.Graphics, Items[i].ToString(), Font, row, ItemColor(Items[i]),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (i == selected && Focused) using (Pen pen = new Pen(ArcadeTheme.Cyan)) e.Graphics.DrawLine(pen, 0, row.Top, 0, row.Bottom - 1);
            }
            if (MaximumTop == 0) return;
            using (Brush dark = new SolidBrush(ArcadeTheme.Black)) e.Graphics.FillRectangle(dark, ScrollTrack);
            using (Brush neon = new SolidBrush(ArcadeTheme.Neon)) e.Graphics.FillRectangle(neon, ScrollThumb);
            using (Pen line = new Pen(ArcadeTheme.Cyan))
            {
                int x = Width - 6;
                e.Graphics.DrawLines(line, new[] { new Point(x - 3, 7), new Point(x, 4), new Point(x + 3, 7) });
                e.Graphics.DrawLines(line, new[] { new Point(x - 3, Height - 8), new Point(x, Height - 5), new Point(x + 3, Height - 8) });
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus();
            if (e.X < Width - 13) { int index = IndexFromPoint(e.Location); if (index >= 0) SelectedIndex = index; return; }
            if (MaximumTop == 0) return;
            if (e.Y < 11) SetTop(top - 1);
            else if (e.Y >= Height - 11) SetTop(top + 1);
            else if (ScrollThumb.Contains(e.Location)) { dragOffset = e.Y - ScrollThumb.Top; Capture = true; }
            else SetTop(top + (e.Y < ScrollThumb.Top ? -VisibleRows : VisibleRows));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragOffset >= 0)
            {
                int travel = ScrollTrack.Height - ScrollThumb.Height;
                if (travel > 0) SetTop((int)Math.Round((double)(e.Y - dragOffset - ScrollTrack.Top) * MaximumTop / travel));
                return;
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragOffset = -1; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragOffset = -1; base.OnMouseCaptureChanged(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            wheel += e.Delta;
            int steps = wheel / 120; wheel %= 120;
            SetTop(top - steps * 2); base.OnMouseWheel(e);
        }
        protected override bool IsInputKey(Keys keyData)
        { Keys key = keyData & Keys.KeyCode; return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int next = selected;
            switch (e.KeyCode) {
                case Keys.Up: next--; break; case Keys.Down: next++; break;
                case Keys.Home: next = 0; break; case Keys.End: next = Items.Count - 1; break;
                case Keys.PageUp: next -= VisibleRows; break; case Keys.PageDown: next += VisibleRows; break;
                default: base.OnKeyDown(e); return;
            }
            SelectedIndex = Math.Max(0, Math.Min(Items.Count - 1, next)); e.Handled = true; base.OnKeyDown(e);
        }
    }

    // Form은 레이아웃과 미리보기 렌더링만 담당하고 실제 안내는 비활성 팝업 메뉴로 표시한다.
    // 별도 Form.Show를 사용하지 않아 활성 창 전환과 설명창 표시가 충돌하지 않는다.
    public sealed class RecordInfoPopup : Form
    {
        public string Heading { get; private set; }
        public string Body { get; private set; }
        Color accent;
        readonly PassiveHelpDropDown nativeTip = new PassiveHelpDropDown();
        readonly Panel canvas = new Panel();
        readonly ToolStripControlHost content;
        Control tipOwner;
        public bool IsShowing { get { return nativeTip.Visible; } }
        public RecordInfoPopup()
        {
            Text = "Taskbar_Games - 안내"; AccessibleName = "게임 안내";
            FormBorderStyle = FormBorderStyle.None; AutoScaleMode = AutoScaleMode.None;
            BackColor = ArcadeTheme.Black; ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            nativeTip.AutoClose = false; nativeTip.AutoSize = false; nativeTip.Padding = Padding.Empty;
            nativeTip.DropShadowEnabled = false; nativeTip.ShowItemToolTips = false;
            canvas.Margin = Padding.Empty; canvas.Padding = Padding.Empty;
            canvas.Paint += delegate(object sender, PaintEventArgs e) { DrawPanel(e.Graphics, canvas.Size); };
            content = new ToolStripControlHost(canvas) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
            nativeTip.Items.Add(content);
        }
        public new void Hide()
        {
            // AutoClose=false에서는 Hide의 자동 닫기 사유가 무시되므로 명시적으로 닫는다.
            nativeTip.Close(ToolStripDropDownCloseReason.CloseCalled); tipOwner = null;
        }
        public static Point Place(Rectangle owner, Size size, Rectangle area)
        {
            int x = owner.Left - size.Width - 8;
            int y = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, owner.Bottom - size.Height));
            // 아주 좁은 화면은 목록을 덮지 않는 위쪽 공간을 사용한다.
            if (x < area.Left) { x = Math.Max(area.Left, Math.Min(area.Right - size.Width, owner.Left)); y = owner.Top - size.Height - 8; }
            return new Point(x, y);
        }
        public void Present(Form owner, string heading, string body, Color color)
        { Present(owner, owner.Bounds, heading, body, color); }
        public void Present(Form owner, Rectangle anchor, string heading, string body, Color color)
        {
            string oldBody = Body, oldHeading = Heading;
            Color oldColor = accent; Rectangle oldBounds = Bounds;
            Heading = heading; Body = body; accent = color;
            Rectangle area = GameEnvironment.Display.WorkingArea;
            int available = anchor.Left - area.Left - 8;
            int preferred = Math.Max(140, Math.Min(310, TextRenderer.MeasureText(body, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 20));
            int width = Math.Min(preferred, Math.Max(140, available));
            int height = TextRenderer.MeasureText(body, Font, new Size(width - 20, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 42;
            ClientSize = new Size(width, Math.Max(74, height));
            Location = Place(anchor, Size, area); Invalidate();
            if (!owner.Visible) { Hide(); return; }
            if (IsShowing && tipOwner == owner && oldBounds == Bounds && oldBody == body && oldHeading == heading && oldColor == color) return;
            Hide(); tipOwner = owner;
            canvas.Size = content.Size = nativeTip.Size = ClientSize;
            canvas.Invalidate(); nativeTip.Show(Location);
        }
        sealed class PassiveHelpDropDown : ToolStripDropDown
        {
            protected override CreateParams CreateParams
            { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawPanel(e.Graphics, ClientSize);
        }
        void DrawPanel(Graphics graphics, Size size)
        {
            graphics.Clear(ArcadeTheme.Black);
            using (Pen border = new Pen(ArcadeTheme.Cyan)) graphics.DrawRectangle(border, 0, 0, size.Width - 1, size.Height - 1);
            using (Pen line = new Pen(Color.FromArgb(37, 67, 59))) graphics.DrawLine(line, 9, 27, size.Width - 10, 27);
            PixelArt.Text(graphics, Heading ?? "", accent, 10, 11);
            TextRenderer.DrawText(graphics, Body ?? "", Font, new Rectangle(10, 34, size.Width - 20, size.Height - 40), Color.WhiteSmoke,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            using (Brush dot = new SolidBrush(accent)) graphics.FillRectangle(dot, size.Width - 15, 10, 5, 5);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Hide(); nativeTip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

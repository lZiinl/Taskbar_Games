using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarGames.Shared
{
    internal static class ArcadeTheme
    {
        public static readonly Color Black = Color.FromArgb(8, 9, 14);
        public static readonly Color Panel = Color.FromArgb(17, 20, 29);
        public static readonly Color Neon = Color.FromArgb(85, 255, 160);
        public static readonly Color Cyan = Color.FromArgb(66, 232, 255);
        public static readonly Color Pink = Color.FromArgb(255, 91, 183);
        public static void Style(ContextMenuStrip menu)
        {
            menu.Renderer = new ArcadeMenuRenderer();
            menu.BackColor = Black; menu.ForeColor = Neon;
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(5);
            menu.Font = new Font("Malgun Gothic", 9f);
        }
        sealed class ArcadeMenuRenderer : ToolStripProfessionalRenderer
        {
            public ArcadeMenuRenderer() { RoundedEdges = false; }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { e.Graphics.Clear(Black); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            { using (Pen p = new Pen(Neon)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Selected && e.Item.Enabled)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 64, 48))) e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color = e.Item.Enabled ? (e.Item.Selected ? Color.White : Neon) : Cyan;
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            { using (Pen p = new Pen(Color.FromArgb(42, 81, 66))) e.Graphics.DrawLine(p, 7, 3, e.Item.Width - 8, 3); }
        }
    }

    internal sealed class TankPalette : Form
    {
        internal static readonly Color[] Colors = {
            Color.FromArgb(85,255,160), Color.FromArgb(174,255,64), Color.FromArgb(255,233,77), Color.FromArgb(255,156,61),
            Color.FromArgb(255,85,104), Color.FromArgb(255,91,183), Color.FromArgb(190,113,255), Color.FromArgb(108,132,255),
            Color.FromArgb(66,232,255), Color.FromArgb(44,191,181), Color.FromArgb(238,243,255), Color.FromArgb(136,150,174)
        };
        internal Color SelectedColor { get; private set; }
        public TankPalette(Color current)
        {
            SelectedColor = current;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(92, 82); BackColor = ArcadeTheme.Black;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(Cursor.Position.X, work.Right - Width)),
                Math.Max(work.Top, Math.Min(Cursor.Position.Y - Height, work.Bottom - Height)));
            for (int i = 0; i < Colors.Length; i++)
            {
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
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(ArcadeTheme.Neon)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            PixelArt.Text(e.Graphics, "TANK COLOR", ArcadeTheme.Neon, 6, 5);
        }
        sealed class PaletteCancel : Button
        {
            public PaletteCancel() { AccessibleName = "닫기 (Esc)"; Cursor = Cursors.Hand; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(ArcadeTheme.Panel);
                using (Pen p = new Pen(Focused ? Color.White : ArcadeTheme.Pink)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                PixelArt.Text(e.Graphics, "CLOSE / ESC", ArcadeTheme.Pink, 8, 3);
            }
        }
        sealed class Swatch : Button
        {
            readonly Color color; readonly bool selected;
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

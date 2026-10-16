using System;
using System.Drawing;
using System.Windows.Forms;
using TaskbarGames.Shared;

namespace TaskbarGames.Games.Pet
{
    // A temporary selection surface; no global mouse hook or input capture after placement.
    internal sealed class AcornPlacement : Form
    {
        internal int SelectedX { get; private set; }
        int previewX;
        public AcornPlacement()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.WorkingArea;
            BackColor = ArcadeTheme.Black; Opacity = 0.85; TopMost = true;
            ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross;
            previewX = PointToClient(Cursor.Position).X;
            MouseMove += delegate(object sender, MouseEventArgs e) { previewX = e.X; Invalidate(); };
            MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { SelectedX = Left + e.X; DialogResult = DialogResult.OK; }
                else DialogResult = DialogResult.Cancel;
                Close();
            };
            Deactivate += delegate { Close(); };
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        { if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; } return base.ProcessCmdKey(ref msg, keys); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(ArcadeTheme.Neon)) e.Graphics.DrawLine(p, 0, Height - 2, Width, Height - 2);
            TextRenderer.DrawText(e.Graphics, "도토리를 놓을 위치를 클릭하세요 · Esc / 우클릭: 취소", SystemFonts.MessageBoxFont,
                new Point(Math.Max(12, Width / 2 - 180), Height - 70), ArcadeTheme.Neon);
            int bounded = Math.Max(38, Math.Min(Width - 38, previewX));
            AcornMarker.DrawAcorn(e.Graphics, bounded - 5, Height - 12);
        }
    }
    internal sealed class AcornMarker : Form
    {
        public AcornMarker()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(12, 12); BackColor = TransparencyKey = Color.Magenta;
            ShowInTaskbar = false; TopMost = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x20; return cp; } }
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
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); DrawAcorn(e.Graphics, 0, 0); }
    }
}

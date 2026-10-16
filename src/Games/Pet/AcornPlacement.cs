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
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
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
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Capture only for this explicit placement step. The transparent background
            // does not tint the desktop, and clicks still select a horizontal position.
            Capture = true;
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Capture = false;
            base.OnFormClosed(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        { if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; } return base.ProcessCmdKey(ref msg, keys); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
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

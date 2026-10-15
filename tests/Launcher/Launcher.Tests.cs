using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Launcher;

public static class LauncherRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class HiddenLauncher : LauncherForm
    {
        public bool RequestedVisible;
        protected override void SetVisibleCore(bool value) { RequestedVisible = value; base.SetVisibleCore(false); }
    }
    sealed class HiddenGame : Form
    {
        public HiddenGame() { IntPtr handle = Handle; }
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Open(LauncherForm launcher, Func<Form> factory)
    {
        typeof(LauncherForm).GetMethod("OpenGame", Private).Invoke(launcher, new object[] { factory });
    }
    static object Active(LauncherForm launcher) { return typeof(LauncherForm).GetField("activeGame", Private).GetValue(launcher); }

    public static void Run(string artifacts)
    {
        using (HiddenLauncher launcher = new HiddenLauncher())
        {
            IntPtr handle = launcher.Handle;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Assert(launcher.ClientSize == new Size(68, 36) && launcher.Right == work.Right - 8 && launcher.Bottom == work.Bottom - 8,
                "Compact launcher is not anchored above the taskbar's right edge: size=" + launcher.ClientSize + ", bounds=" + launcher.Bounds + ", work=" + work);
            using (Bitmap preview = new Bitmap(launcher.ClientSize.Width, launcher.ClientSize.Height))
            {
                launcher.DrawToBitmap(preview, launcher.ClientRectangle);
                // Hidden test forms do not paint their children through DrawToBitmap.
                // Render the actual card painters separately into their client bounds.
                using (Graphics canvas = Graphics.FromImage(preview))
                    foreach (Control card in launcher.Controls)
                        using (Bitmap cardImage = new Bitmap(card.Width, card.Height))
                        using (Graphics cardGraphics = Graphics.FromImage(cardImage))
                        {
                            card.GetType().GetMethod("OnPaint", Private).Invoke(card,
                                new object[] { new PaintEventArgs(cardGraphics, card.ClientRectangle) });
                            canvas.DrawImageUnscaled(cardImage, card.Location);
                        }
                preview.Save(Path.Combine(artifacts, "launcher.png"), ImageFormat.Png);
            }
            HiddenGame first = new HiddenGame();
            Open(launcher, delegate { return first; });
            Assert(Object.ReferenceEquals(first, Active(launcher)) && !launcher.RequestedVisible, "Launcher did not own game and hide.");
            int creations = 0;
            Open(launcher, delegate { creations++; return new HiddenGame(); });
            Assert(creations == 0, "Second game was allowed to start.");
            first.Close();
            Assert(first.IsDisposed && Active(launcher) == null && launcher.RequestedVisible, "Closing game did not return to selection.");
            HiddenGame second = new HiddenGame();
            Open(launcher, delegate { return second; });
            Assert(Object.ReferenceEquals(second, Active(launcher)), "Second selection did not start after first closed.");
            launcher.Close();
            Assert(second.IsDisposed && launcher.IsDisposed, "Application shutdown left game alive.");
        }
        Console.WriteLine("PASS: single active game, launcher hide/return, reselection and application shutdown; launcher preview rendered.");
    }
}

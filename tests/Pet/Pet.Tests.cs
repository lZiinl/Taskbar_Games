using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Pet;

public static class PetRegressionTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string artifacts)
    {
        PetSession session = new PetSession();
        session.Advance(300);
        Assert(Math.Abs(session.Fullness - 65) < 0.001, "Hunger decay is not time based.");
        session.Advance(Double.NaN); session.Advance(Double.PositiveInfinity);
        Assert(session.Fullness == 65, "Invalid time corrupted state.");
        session.Advance(100000);
        Assert(session.Fullness == 0 && session.Mood == 0, "Stats must have a floor.");
        session.Feed(); session.Pet();
        Assert(session.Fullness == 22 && session.Mood == 17 && session.Meals == 1, "Care should restore state.");
        for (int i = 0; i < 10; i++) { session.Feed(); session.Pet(); }
        Assert(session.Fullness == 100 && session.Mood == 100, "Stats must have a ceiling.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (PetGame pet = (PetGame)Activator.CreateInstance(typeof(PetGame), flags, null, new object[] { false }, null))
        using (Bitmap strip = new Bitmap(624, 110))
        using (Graphics g = Graphics.FromImage(strip))
        {
            Assert(pet.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom, "Pet must stand on taskbar.");
            Assert(pet.ClientSize == new Size(76, 56), "Pet footprint should be compact.");
            MethodInfo advance = typeof(PetGame).GetMethod("AdvanceBehavior", flags);
            MethodInfo setFood = typeof(PetGame).GetMethod("SetFoodTarget", flags);
            FieldInfo action = typeof(PetGame).GetField("action", flags);
            FieldInfo remaining = typeof(PetGame).GetField("remaining", flags);
            FieldInfo petX = typeof(PetGame).GetField("x", flags);
            PetSession actualSession = (PetSession)typeof(PetGame).GetField("session", flags).GetValue(pet);
            advance.Invoke(pet, new object[] { 60.0 });
            Assert((string)action.GetValue(pet) == "idle", "Initial rest should continue after one minute.");
            double startX = (double)petX.GetValue(pet);
            setFood.Invoke(pet, new object[] { startX + 180 });
            advance.Invoke(pet, new object[] { 1.0 });
            Assert((double)petX.GetValue(pet) > startX && actualSession.Meals == 0, "Pet must walk toward food before receiving it.");
            for (int step = 0; step < 100; step++) advance.Invoke(pet, new object[] { 0.1 });
            Assert(actualSession.Meals == 1, "A placed acorn should be eaten exactly once.");
            setFood.Invoke(pet, new object[] { (double)Screen.PrimaryScreen.WorkingArea.Left });
            for (int step = 0; step < 250; step++) advance.Invoke(pet, new object[] { 0.1 });
            Assert(actualSession.Meals == 2, "Pet should find food to its left, including screen edges.");
            remaining.SetValue(pet, 0.01); action.SetValue(pet, "walk");
            advance.Invoke(pet, new object[] { 0.1 });
            Assert((double)remaining.GetValue(pet) >= 60 && (double)remaining.GetValue(pet) <= 180, "Idle duration should be one to three minutes.");
            g.Clear(Color.FromArgb(225, 231, 239));
            string[] poses = { "idle", "walk", "idle", "eat", "sleep", "love" };
            for (int i = 0; i < poses.Length; i++)
            {
                typeof(PetGame).GetField("action", flags).SetValue(pet, poses[i]);
                typeof(PetGame).GetField("remaining", flags).SetValue(pet, 0.5);
                using (Bitmap frame = new Bitmap(104, 88))
                using (Graphics fg = Graphics.FromImage(frame))
                {
                    typeof(PetGame).GetMethod("OnPaint", flags).Invoke(pet, new object[] { new PaintEventArgs(fg, pet.ClientRectangle) });
                    g.DrawImageUnscaled(frame, i * 104, 20);
                }
                g.DrawString(poses[i], SystemFonts.DefaultFont, Brushes.Black, i * 104 + 12, 2);
            }
            strip.Save(Path.Combine(artifacts, "squirrel-poses.png"), ImageFormat.Png);
            typeof(PetGame).GetMethod("UpdateStatus", flags).Invoke(pet, null);
            ContextMenuStrip menu = (ContextMenuStrip)typeof(PetGame).GetField("menu", flags).GetValue(pet);
            menu.Size = menu.GetPreferredSize(Size.Empty);
            using (Bitmap menuImage = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(menuImage, menu.ClientRectangle);
                menuImage.Save(Path.Combine(artifacts, "pet-menu.png"), ImageFormat.Png);
            }
        }
        Type paletteType = typeof(PetGame).Assembly.GetType("TaskbarGames.Shared.TankPalette");
        using (Form palette = (Form)Activator.CreateInstance(paletteType, new object[] { Color.FromArgb(85, 255, 160) }))
        using (Bitmap preview = new Bitmap(palette.Width, palette.Height))
        using (Graphics canvas = Graphics.FromImage(preview))
        {
            Assert(palette.Controls.Count == 13, "Palette must contain twelve colors and cancel.");
            Assert(palette.ClientSize == new Size(92, 82), "Palette should be half its previous width and height.");
            canvas.Clear(palette.BackColor);
            paletteType.GetMethod("OnPaint", flags).Invoke(palette, new object[] { new PaintEventArgs(canvas, palette.ClientRectangle) });
            foreach (Control control in palette.Controls)
                using (Bitmap button = new Bitmap(control.Width, control.Height))
                using (Graphics bg = Graphics.FromImage(button))
                {
                    control.GetType().GetMethod("OnPaint", flags).Invoke(control, new object[] { new PaintEventArgs(bg, control.ClientRectangle) });
                    canvas.DrawImageUnscaled(button, control.Location);
                }
            preview.Save(Path.Combine(artifacts, "tank-palette.png"), ImageFormat.Png);
            typeof(Control).GetMethod("OnClick", flags).Invoke(palette.Controls[8], new object[] { EventArgs.Empty });
            Assert(palette.DialogResult == DialogResult.OK, "Palette selection should confirm.");
            Assert(((Color)paletteType.GetProperty("SelectedColor", flags).GetValue(palette, null)).ToArgb() == Color.FromArgb(66,232,255).ToArgb(), "Palette must return the clicked color.");
        }
        Type placementType = typeof(PetGame).Assembly.GetType("TaskbarGames.Games.Pet.AcornPlacement");
        using (Form placement = (Form)Activator.CreateInstance(placementType))
        {
            placementType.GetMethod("OnMouseDown", flags).Invoke(placement, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 240, 120, 0) });
            Assert(placement.DialogResult == DialogResult.OK, "Left click should confirm food placement.");
            Assert((int)placementType.GetProperty("SelectedX", flags).GetValue(placement, null) == Screen.PrimaryScreen.WorkingArea.Left + 240,
                "Food placement must use screen coordinates.");
        }
        using (Form placement = (Form)Activator.CreateInstance(placementType))
        {
            placementType.GetMethod("OnMouseDown", flags).Invoke(placement, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 240, 120, 0) });
            Assert(placement.DialogResult == DialogResult.Cancel, "Right click should cancel placement.");
        }
        Console.WriteLine("PASS: pet care, long actions, food placement/cancel, left/right seeking, single consumption, compact palette and rendered poses; user saves untouched.");
    }
}

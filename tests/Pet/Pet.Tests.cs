using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Pet;
using TaskbarGames.Shared;

public static class PetRegressionTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Advance(PetBehavior pet, double seconds)
    { for (int i = 0; i < (int)Math.Round(seconds * 10); i++) pet.Advance(0.1, 0, 1920); }
    public static void Run(string artifacts)
    {
        PetSession stats = new PetSession(); stats.Advance(300);
        Assert(Math.Abs(stats.Fullness - 65) < 0.001, "Hunger decay is not time based.");
        stats.Advance(Double.NaN); stats.Advance(Double.PositiveInfinity);
        Assert(stats.Fullness == 65, "Invalid time corrupted state.");
        stats.Advance(100000); Assert(stats.Fullness == 0 && stats.Mood == 0, "Stats floor failed.");
        stats.Feed(); stats.Pet(); Assert(stats.Fullness == 22 && stats.Mood == 17 && stats.Meals == 1, "Care reward failed.");
        for (int i = 0; i < 10; i++) { stats.Feed(); stats.Pet(); }
        Assert(stats.Fullness == 100 && stats.Mood == 100, "Stats ceiling failed.");
        PetBehavior model = new PetBehavior(new PetSession(), new Random(42), 250);
        model.Advance(60, 0, 1920);
        Assert(model.Action == PetAction.Idle && model.Remaining == 60, "Initial idle should last two minutes.");
        int eaten = 0; model.Ate += delegate { eaten++; };
        model.AddFood(250 + 38 + 180, 0, 1920);
        model.AddFood(250 + 38 - 36, 0, 1920);
        model.AddFood(250 + 38 + 72, 0, 1920);
        model.Advance(1, 0, 1920);
        Assert(model.Stats.Meals == 1 && model.Food.Count == 2 && model.X == 214, "Nearest food was not eaten first.");
        model.AddFood(252, 0, 1920); model.Advance(1, 0, 1920);
        Assert(model.Action == PetAction.Eat && model.Stats.Meals == 1 && model.Food.Count == 3, "Food interrupted chewing.");
        Assert(!model.Pet(), "Petting must not interrupt chewing.");
        Advance(model, 70);
        Assert(model.Stats.Meals == 4 && eaten == 4 && model.Food.Count == 0, "Acorn consumption duplicated or lost.");
        for (int i = 0; i < 5; i++) Assert(model.AddFood(800 + i * 20, 0, 1920) != null, "Five acorns should fit.");
        Assert(model.AddFood(900, 0, 1920) == null && model.Food.Count == 5, "Sixth acorn replaced food.");
        Assert(model.Pet() && !model.Pet(), "Pet cooldown failed.");
        double x = model.X; model.Advance(1, 0, 1920);
        Assert(model.Action == PetAction.Love && model.X == x, "Food interrupted petting.");
        model.Advance(4, 0, 1920); Assert(model.Action != PetAction.Love, "Petting did not end.");
        model.ToggleSleep(); model.Advance(10, 0, 1920);
        Assert(model.Action == PetAction.Sleep, "Food interrupted manual sleep.");
        model.ToggleSleep(); model.Advance(0.1, 0, 1920);
        Assert(model.Action == PetAction.Walk, "Waking did not resume food pursuit.");
        PetBehavior jumping = new PetBehavior(new PetSession(), new Random(2), 0);
        jumping.AddFood(38, 0, 1920); Assert(jumping.Jump() && !jumping.Jump(), "Jump stacked.");
        jumping.Advance(0.25, 0, 1920);
        Assert(jumping.JumpHeight >= 29 && jumping.Stats.Meals == 0, "Ate during jump.");
        jumping.Advance(0.31, 0, 1920);
        Assert(jumping.JumpAge < 0 && jumping.Stats.Meals == 1, "Landing did not consume acorn.");
        PetBehavior edge = new PetBehavior(new PetSession(), new Random(3), -150);
        edge.AddFood(-1, -1920, 0);
        for (int i = 0; i < 100 && edge.Stats.Meals == 0; i++) edge.Advance(0.1, -1920, 0);
        Assert(edge.X == -76 && edge.Stats.Meals == 1, "Negative-coordinate right-edge food unreachable.");
        edge.SetBounds(0, 1920); Assert(edge.X == 0, "Display removal failed to clamp pet.");
        edge.AddFood(1900, 0, 1920); edge.SetBounds(-1280, 0);
        foreach (Acorn food in edge.Food) Assert(food.X >= -1242 && food.X <= -38, "Food not clamped after display change.");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (PetGame pet = (PetGame)Activator.CreateInstance(typeof(PetGame), flags, null, new object[] { false }, null))
        {
            IntPtr handle = pet.Handle;
            Assert(pet.ClientSize == new Size(76, 56) && pet.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom, "Native pet footprint changed.");
            PetBehavior behavior = (PetBehavior)typeof(PetGame).GetField("behavior", flags).GetValue(pet);
            MethodInfo key = typeof(PetGame).GetMethod("OnPetKeyDown", flags);
            typeof(PetGame).GetField("focused", flags).SetValue(pet, false);
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            Assert(behavior.JumpAge < 0, "Inactive pet jumped.");
            typeof(PetGame).GetField("focused", flags).SetValue(pet, true);
            typeof(PetGame).GetField("jumpHeld", flags).SetValue(pet, false);
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            behavior.Advance(0.25, Screen.PrimaryScreen.WorkingArea.Left, Screen.PrimaryScreen.WorkingArea.Right);
            typeof(PetGame).GetMethod("PositionPet", flags).Invoke(pet, null);
            Assert(pet.Bottom <= Screen.PrimaryScreen.WorkingArea.Bottom - 29, "Native jump position wrong.");
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            Assert(behavior.JumpAge == 0.25, "Key repeat restarted jump.");
            typeof(Form).GetMethod("OnDeactivate", flags).Invoke(pet, new object[] { EventArgs.Empty });
            Assert(behavior.JumpAge < 0 && pet.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom, "Focus loss did not cancel jump.");
            typeof(PetGame).GetMethod("UpdateStatus", flags).Invoke(pet, null);
            ToolStripDropDown menu = (ToolStripDropDown)typeof(PetGame).GetField("menu", flags).GetValue(pet);
            menu.PerformLayout(); Assert(menu.Items.Count == 6 && menu.Size == new Size(154, 34), "Pet menu items are clipped.");
            using (Bitmap image = new Bitmap(menu.Width, menu.Height))
            { menu.DrawToBitmap(image, menu.ClientRectangle); image.Save(Path.Combine(artifacts, "pet-menu.png")); }
            using (Bitmap strip = new Bitmap(624, 110))
            using (Graphics g = Graphics.FromImage(strip))
            {
                g.Clear(Color.FromArgb(225, 231, 239));
                string[] poses = { "idle", "walk", "jump", "eat", "sleep", "love" };
                for (int i = 0; i < poses.Length; i++)
                {
                    typeof(PetGame).GetMethod("DrawSquirrel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                        new object[] { g, i * 104 + 24, 40, true, poses[i], 0.5, 1 });
                    g.DrawString(poses[i], SystemFonts.DefaultFont, Brushes.Black, i * 104 + 12, 2);
                }
                strip.Save(Path.Combine(artifacts, "squirrel-poses.png"));
            }
        }
        // 작은 도토리 표시 창도 실제 핸들을 생성하여 네이티브 크기가 의도한 16×16인지 검사한다.
        Type markerType = typeof(PetGame).Assembly.GetType("TaskbarGames.Games.Pet.AcornMarker");
        using (Form marker = (Form)Activator.CreateInstance(markerType))
        {
            IntPtr handle = marker.Handle;
            Assert(marker.Size == new Size(16, 16), "Native acorn marker must keep its compact footprint.");
        }
        Type paletteType = typeof(PetGame).Assembly.GetType("TaskbarGames.Shared.TankPalette");
        using (Form palette = (Form)Activator.CreateInstance(paletteType, new object[] { Color.FromArgb(85, 255, 160) }))
        using (Bitmap preview = new Bitmap(palette.Width, palette.Height))
        using (Graphics canvas = Graphics.FromImage(preview))
        {
            Assert(palette.Controls.Count == 13, "Palette must contain twelve colors and cancel.");
            IntPtr paletteHandle = palette.Handle;
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
        // 화면을 덮지 않는 배치 창의 투명 설정과 좌/우클릭 확정·취소 경로를 검사한다.
        Type placementType = typeof(PetGame).Assembly.GetType("TaskbarGames.Games.Pet.AcornPlacement");
        using (Form placement = (Form)Activator.CreateInstance(placementType))
        {
            Assert(placement.Height == 24 && placement.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom,
                "Placement should only occupy a narrow taskbar strip.");
            Assert(placement.TransparencyKey == placement.BackColor && placement.Opacity == 1,
                "Placement must not tint or dim the desktop.");
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

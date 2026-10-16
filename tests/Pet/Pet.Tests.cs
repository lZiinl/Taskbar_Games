// 다람쥐의 수치·다중 먹이·돌봄 우선순위·아이콘 메뉴와 화면 끝 버그를 검사한다.
// persist=false 생성자를 사용해 실제 사용자 저장 파일을 읽거나 수정하지 않는다.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Games.Pet;

public static class PetRegressionTests
{
    // 실패 조건을 설명하는 예외를 발생시킨다. 반환값 대신 예외를 써 PowerShell에서도 실패를 감지한다.
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    // 모델 수치 → 행동/먹이 → 화면 그리기 → 다중 먹이/5개 제한/돌봄 → 양 끝 이동 → 팔레트/배치 순서로 검증한다.
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
            // 실제 네이티브 창 생성 후 크기를 검사해야 Windows 최소 창 크기 보정으로 생긴 버그를 잡을 수 있다.
            // 생성자 직후 ClientSize만 검사하면 실제 실행 때의 크기 확장을 놓친다.
            IntPtr petHandle = pet.Handle;
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
            ToolStripDropDown menu = (ToolStripDropDown)typeof(PetGame).GetField("menu", flags).GetValue(pet);
            menu.PerformLayout();
            Assert(menu.Items.Count == 5 && menu.Width <= 140 && menu.Height <= 40, "Pet menu must be a compact row of five icons.");
            using (Bitmap menuImage = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(menuImage, menu.ClientRectangle);
                menuImage.Save(Path.Combine(artifacts, "pet-menu.png"), ImageFormat.Png);
            }
        }
        using (PetGame pet = (PetGame)Activator.CreateInstance(typeof(PetGame), flags, null, new object[] { false }, null))
        {
            MethodInfo add = typeof(PetGame).GetMethod("SetFoodTarget", flags);
            MethodInfo step = typeof(PetGame).GetMethod("AdvanceBehavior", flags);
            FieldInfo position = typeof(PetGame).GetField("x", flags);
            FieldInfo action = typeof(PetGame).GetField("action", flags);
            // 다른 거리의 먹이를 놓아 입력 순서가 아니라 가장 가까운 것을 먼저 먹는지 확인한다.
            double origin = Screen.PrimaryScreen.WorkingArea.Left + 250;
            position.SetValue(pet, origin);
            add.Invoke(pet, new object[] { origin + 38 + 180 });
            add.Invoke(pet, new object[] { origin + 38 - 36 });
            add.Invoke(pet, new object[] { origin + 38 + 72 });
            System.Collections.ICollection food = (System.Collections.ICollection)typeof(PetGame).GetField("food", flags).GetValue(pet);
            Assert(food.Count == 3, "New acorns must preserve existing acorns.");
            step.Invoke(pet, new object[] { 1.0 });
            PetSession state = (PetSession)typeof(PetGame).GetField("session", flags).GetValue(pet);
            Assert(state.Meals == 1 && food.Count == 2 && Math.Abs((double)position.GetValue(pet) - origin + 36) < 0.1,
                "Pet must eat nearest acorn first rather than first or last placed.");
            add.Invoke(pet, new object[] { origin + 38 - 36 });
            step.Invoke(pet, new object[] { 1.0 });
            Assert(state.Meals == 1 && food.Count == 3 && (string)action.GetValue(pet) == "eat", "Adding food must not interrupt chewing.");
            for (int i = 0; i < 700; i++) step.Invoke(pet, new object[] { 0.1 });
            // 목록에서 제거와 점수 반영이 중복되지 않는지 전체 먹이 소진 후 확인한다.
            Assert(state.Meals == 4 && food.Count == 0, "Every queued acorn must be eaten exactly once.");
            for (int i = 0; i < 5; i++) Assert((bool)add.Invoke(pet, new object[] { origin + 100 + i * 20 }), "Five acorns should be allowed.");
            Assert(!(bool)add.Invoke(pet, new object[] { origin + 250 }) && food.Count == 5, "Sixth acorn must be rejected without removing existing food.");
            typeof(PetGame).GetMethod("Stroke", flags).Invoke(pet, null);
            double beforeCare = (double)position.GetValue(pet);
            step.Invoke(pet, new object[] { 1.0 });
            Assert((string)action.GetValue(pet) == "love" && (double)position.GetValue(pet) == beforeCare && food.Count == 5,
                "Petting must interrupt food pursuit and keep queued acorns.");
            step.Invoke(pet, new object[] { 4.0 });
            Assert((string)action.GetValue(pet) != "love", "Pet must resume pursuit after petting.");
            // Move away from any food so sleeping tests pursuit rather than chewing.
            action.SetValue(pet, "walk");
            typeof(PetGame).GetMethod("StartAction", flags).Invoke(pet, new object[] { "sleep", 300.0 });
            step.Invoke(pet, new object[] { 10.0 });
            Assert((string)action.GetValue(pet) == "sleep", "Manual sleep must take precedence over queued food.");
        }
        using (PetGame pet = (PetGame)Activator.CreateInstance(typeof(PetGame), flags, null, new object[] { false }, null))
        {
            IntPtr handle = pet.Handle;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            FieldInfo position = typeof(PetGame).GetField("x", flags);
            FieldInfo action = typeof(PetGame).GetField("action", flags);
            MethodInfo add = typeof(PetGame).GetMethod("SetFoodTarget", flags);
            MethodInfo step = typeof(PetGame).GetMethod("AdvanceBehavior", flags);
            PetSession state = (PetSession)typeof(PetGame).GetField("session", flags).GetValue(pet);
            position.SetValue(pet, (double)area.Right - 160);
            add.Invoke(pet, new object[] { (double)area.Right - 1 });
            for (int i = 0; i < 50 && state.Meals == 0; i++) step.Invoke(pet, new object[] { 0.1 });
            Assert(state.Meals == 1 && pet.Right == area.Right, "Native pet window must reach and eat right-edge acorn.");
            action.SetValue(pet, "walk");
            position.SetValue(pet, (double)area.Left + 100);
            add.Invoke(pet, new object[] { (double)area.Left });
            for (int i = 0; i < 50 && state.Meals == 1; i++) step.Invoke(pet, new object[] { 0.1 });
            Assert(state.Meals == 2 && pet.Left == area.Left, "Native pet window must reach and eat left-edge acorn.");
        }
        // 점프는 먹이 추적과 함께 진행되며 공중 섭취·키 자동 반복·포커스 밖 입력은 막는다.
        using (PetGame pet = (PetGame)Activator.CreateInstance(typeof(PetGame), flags, null, new object[] { false }, null))
        {
            IntPtr handle = pet.Handle;
            MethodInfo key = typeof(PetGame).GetMethod("OnPetKeyDown", flags);
            MethodInfo step = typeof(PetGame).GetMethod("AdvanceBehavior", flags);
            FieldInfo age = typeof(PetGame).GetField("jumpAge", flags);
            FieldInfo held = typeof(PetGame).GetField("jumpHeld", flags);
            FieldInfo focus = typeof(PetGame).GetField("focused", flags);
            focus.SetValue(pet, false);
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            Assert((double)age.GetValue(pet) < 0, "Inactive pet must not jump.");
            focus.SetValue(pet, true); held.SetValue(pet, false);
            double center = (double)typeof(PetGame).GetField("x", flags).GetValue(pet) + 38;
            typeof(PetGame).GetMethod("SetFoodTarget", flags).Invoke(pet, new object[] { center });
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            step.Invoke(pet, new object[] { 0.25 });
            PetSession state = (PetSession)typeof(PetGame).GetField("session", flags).GetValue(pet);
            Assert(pet.Bottom <= Screen.PrimaryScreen.WorkingArea.Bottom - 29 && state.Meals == 0,
                "Jump should lift pet without eating food in midair.");
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            Assert((double)age.GetValue(pet) == 0.25, "Held Space must not restart jump.");
            step.Invoke(pet, new object[] { 0.31 });
            Assert((double)age.GetValue(pet) < 0 && pet.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom && state.Meals == 1,
                "Pet should land and then eat the waiting acorn.");
            typeof(PetGame).GetField("action", flags).SetValue(pet, "idle");
            held.SetValue(pet, false);
            key.Invoke(pet, new object[] { pet, new KeyEventArgs(Keys.Space) });
            step.Invoke(pet, new object[] { 0.1 });
            typeof(Form).GetMethod("OnDeactivate", flags).Invoke(pet, new object[] { EventArgs.Empty });
            Assert((double)age.GetValue(pet) < 0 && pet.Bottom == Screen.PrimaryScreen.WorkingArea.Bottom,
                "Focus loss must return pet safely to the ground.");
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

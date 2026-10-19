using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Launcher;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Fortress;
using TaskbarGames.Games.Pet;
using TaskbarGames.Games.Runner;

public static class SharedRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static object Field(object value, string name) { return value.GetType().GetField(name, Private).GetValue(value); }
    static void Call(object value, string name, params object[] args)
    {
        MethodInfo method = value.GetType().GetMethod(name, Private | BindingFlags.DeclaredOnly)
            ?? value.GetType().GetMethod(name, Private);
        method.Invoke(value, args);
    }
    public static void Run(string artifacts, bool includeUI = true)
    {
        string directory = Path.Combine(artifacts, "storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Storage(directory);
        if (!includeUI) { Console.WriteLine("PASS: profile storage, migration and recovery."); return; }
        GameStore originalStore = GameEnvironment.Store;
        DisplaySelection originalDisplay = GameEnvironment.Display;
        try
        {
            GameEnvironment.Store = new GameStore();
            Displays(artifacts);
            GameEnvironment.Display = originalDisplay;
            ReopenGames(Path.Combine(directory, "games.xml"));
        }
        finally { GameEnvironment.Store = originalStore; GameEnvironment.Display = originalDisplay; }
        Console.WriteLine("PASS: atomic profile/backup recovery, legacy migration, corrupt/future saves, game reopen, monitor picker paging, negative coordinates and display removal.");
    }
    static void Storage(string directory)
    {
        string path = Path.Combine(directory, "profile.xml");
        GameStore store = new GameStore(path); store.Load();
        Assert(store.Data.PetFullness == 75 && store.Data.RunnerBest == 0, "Fresh defaults invalid.");
        store.Data.RunnerBest = 123; store.Data.PetMeals = 5;
        store.Data.PetFullness = 42.125; store.Data.TankColor = 0x123456;
        store.Data.BaseballScore = 5000000000; store.Data.BaseballAttempts = 3; store.Data.BaseballHits = 2;
        store.Data.FortressHits = 5; store.Data.FortressShots = 10; store.Data.FortressBestCombo = 3;
        store.Data.MonitorDevice = "DISPLAY2";
        Assert(store.Save(), "Initial save failed.");
        GameStore loaded = new GameStore(path); loaded.Load();
        Assert(loaded.Data.RunnerBest == 123 && loaded.Data.PetMeals == 5 && loaded.Data.PetFullness == 42.125
            && loaded.Data.TankColor == 0x123456 && loaded.Data.BaseballScore == 5000000000
            && loaded.Data.BaseballHits == 2 && loaded.Data.FortressBestCombo == 3 && loaded.Data.MonitorDevice == "DISPLAY2", "Round-trip lost fields.");
        loaded.Data.RunnerBest = 456; Assert(loaded.Save() && File.Exists(path + ".bak"), "Replace did not keep backup.");
        // Failed writes must leave the complete previous file intact.
        using (FileStream locked = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        { loaded.Data.RunnerBest = 999; Assert(!loaded.Save() && loaded.Error.Length > 0, "Write failure was not reported."); }
        GameStore unchanged = new GameStore(path); unchanged.Load(); Assert(unchanged.Data.RunnerBest == 456, "Failed write damaged profile.");
        File.WriteAllText(path, "<broken");
        GameStore recovered = new GameStore(path); recovered.Load();
        Assert(recovered.Data.RunnerBest == 123 && recovered.Error.Length > 0, "Backup recovery failed.");
        Assert(recovered.Save(), "Recovered profile cannot save.");
        GameStore healthy = new GameStore(path); healthy.Load(); Assert(healthy.Data.RunnerBest == 123 && healthy.Error == "", "Recovery not persisted.");
        string corruptPath = Path.Combine(directory, "corrupt.xml"); File.WriteAllText(corruptPath, "broken");
        GameStore corrupt = new GameStore(corruptPath); corrupt.Load();
        Assert(!corrupt.Save() && File.ReadAllText(corruptPath) == "broken", "Unreadable original overwritten.");
        string futurePath = Path.Combine(directory, "future.xml"); File.WriteAllText(futurePath, "<GameData><Version>4</Version></GameData>");
        GameStore future = new GameStore(futurePath); future.Load(); Assert(!future.Save(), "Future profile overwritten.");
        string invalidPath = Path.Combine(directory, "invalid.xml");
        File.WriteAllText(invalidPath, "<GameData><Version>1</Version><RunnerBest>-1</RunnerBest><PetFullness>NaN</PetFullness><PetMood>150</PetMood><PetMeals>-2</PetMeals><TankColor>-4</TankColor></GameData>");
        GameStore invalid = new GameStore(invalidPath); invalid.Load();
        Assert(invalid.Data.RunnerBest == 0 && invalid.Data.PetFullness == 75 && invalid.Data.PetMood == 100 && invalid.Data.PetMeals == 0 && invalid.Data.TankColor == 0x5DB86A, "Loaded values not normalized.");
        string oldPet = Path.Combine(directory, "squirrel.txt"), oldColor = Path.Combine(directory, "tank-color.txt");
        File.WriteAllLines(oldPet, new[] { "33.5", "81.25", "9" }); File.WriteAllText(oldColor, "ABCDEF");
        string migratedPath = Path.Combine(directory, "migrated.xml");
        GameStore migration = new GameStore(migratedPath);
        migration.ImportLegacy(oldPet, () => 0x223344, oldColor);
        Assert(migration.Data.PetFullness == 33.5 && migration.Data.PetMeals == 9 && migration.Data.TankColor == 0x223344, "Legacy settings or registry precedence lost.");
        Assert(File.Exists(oldPet) && File.Exists(oldColor), "Migration deleted source data.");
        migration.Data.PetMeals = 12; migration.Save(); migration.ImportLegacy(oldPet, () => 0, oldColor);
        Assert(migration.Data.PetMeals == 12 && migration.Data.TankColor == 0x223344, "Legacy import ran twice.");
        GameStore fileColor = new GameStore(Path.Combine(directory, "file-color.xml"));
        fileColor.ImportLegacy(oldPet, () => null, oldColor); Assert(fileColor.Data.TankColor == 0xABCDEF, "Legacy file fallback failed.");
        GameStore failedMigration = new GameStore(Path.Combine(directory, "failed-import.xml"));
        failedMigration.ImportLegacy(oldPet, delegate { throw new IOException(); }, oldColor);
        Assert(!failedMigration.Save() && !File.Exists(Path.Combine(directory, "failed-import.xml")), "Incomplete migration committed defaults.");
    }
    static void Displays(string artifacts)
    {
        DisplayInfo primary = new DisplayInfo("DISPLAY1", new Rectangle(0, 0, 1920, 1040), true);
        DisplayInfo secondary = new DisplayInfo("DISPLAY2", new Rectangle(-1600, -200, 1600, 860), false);
        DisplayInfo[] screens = new[] { primary, secondary };
        GameEnvironment.Display = new DisplaySelection(() => screens);
        GameEnvironment.Display.Select("DISPLAY2");
        using (MonitorSelectionForm picker = new MonitorSelectionForm())
        using (LauncherForm launcher = new LauncherForm())
        using (FortressGame fortress = new FortressGame())
        using (BaseballGame baseball = new BaseballGame())
        using (RunnerGame runner = new RunnerGame())
        using (PetGame pet = new PetGame())
        {
            IntPtr a = picker.Handle, b = launcher.Handle;
            Assert(picker.ClientSize == new Size(58, 74) && picker.ClientSize == launcher.ClientSize && picker.Size == launcher.Size, "Monitor picker size differs from launcher.");
            Assert(picker.Controls.Count == 3, "Two monitors plus exit expected.");
            Assert(launcher.Right == secondary.WorkingArea.Right - 8 && launcher.Bottom == secondary.WorkingArea.Bottom - 8, "Launcher on wrong display.");
            Assert(fortress.Bounds == secondary.WorkingArea && baseball.Bounds == secondary.WorkingArea, "Full-area games on wrong display.");
            Assert(runner.Left == -1600 && runner.Bottom == 660 && runner.Width == 800, "Runner on wrong display.");
            Assert(pet.Left >= -1600 && pet.Right <= 0 && pet.Bottom == 660, "Pet on wrong display.");
            Type placementType = typeof(PetGame).Assembly.GetType("TaskbarGames.Games.Pet.AcornPlacement");
            using (Form placement = (Form)Activator.CreateInstance(placementType))
            {
                Assert(placement.Left == -1600 && placement.Bottom == 660, "Acorn picker on wrong display.");
                Call(placement, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 240, 12, 0));
                Assert((int)placementType.GetProperty("SelectedX", Private).GetValue(placement, null) == -1360, "Acorn global coordinates wrong.");
            }
            using (Bitmap preview = new Bitmap(58, 74))
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                Call(picker, "OnPaint", new PaintEventArgs(graphics, picker.ClientRectangle));
                foreach (Control control in picker.Controls)
                    using (Bitmap button = new Bitmap(control.Width, control.Height))
                    using (Graphics bg = Graphics.FromImage(button))
                    {
                        Call(control, "OnPaint", new PaintEventArgs(bg, control.ClientRectangle));
                        graphics.DrawImageUnscaled(button, control.Location);
                    }
                preview.Save(Path.Combine(artifacts, "monitor-selector.png"));
            }
            // A disconnect dynamically repositions every live surface on the next tick.
            screens = new[] { primary };
            Call(fortress, "Tick", fortress, EventArgs.Empty); Call(baseball, "Tick", baseball, EventArgs.Empty);
            Call(pet, "Tick", pet, EventArgs.Empty);
            typeof(Timer).GetMethod("OnTick", Private).Invoke(Field(runner, "timer"), new object[] { EventArgs.Empty });
            Call(launcher, "PositionAboveTaskbar"); Call(picker, "RefreshDisplays");
            Assert(fortress.Bounds == primary.WorkingArea && baseball.Bounds == primary.WorkingArea && runner.Left == 0
                && pet.Left >= 0 && launcher.Right == 1912 && picker.Right == 1912, "Disconnected display did not fall back.");
            Assert(picker.Controls.Count == 2, "Monitor picker did not refresh after disconnect.");
        }
        screens = new[] { primary, secondary, new DisplayInfo("DISPLAY3", primary.WorkingArea, false), new DisplayInfo("DISPLAY4", primary.WorkingArea, false), new DisplayInfo("DISPLAY5", primary.WorkingArea, false) };
        using (MonitorSelectionForm picker = new MonitorSelectionForm())
        {
            Assert(picker.Controls.Count == 5, "First page should have four monitors.");
            typeof(Control).GetMethod("OnClick", Private).Invoke(picker.Controls[4], new object[] { EventArgs.Empty });
            Assert(picker.Controls.Count == 2 && picker.Controls[0].AccessibleName == "모니터 5", "Fifth monitor inaccessible.");
            typeof(Control).GetMethod("OnClick", Private).Invoke(picker.Controls[0], new object[] { EventArgs.Empty });
            Assert(picker.DialogResult == DialogResult.OK && GameEnvironment.Display.DeviceName == "DISPLAY5" && GameEnvironment.Store.Data.MonitorDevice == "DISPLAY5", "Selection not applied or saved.");
        }
        using (MonitorSelectionForm picker = new MonitorSelectionForm())
        {
            Assert(picker.Controls[0].AccessibleName == "모니터 5", "Saved display page not restored.");
            object[] args = { new Message(), Keys.Escape };
            bool handled = (bool)typeof(MonitorSelectionForm).GetMethod("ProcessCmdKey", Private).Invoke(picker, args);
            Assert(handled && picker.DialogResult == DialogResult.Cancel && GameEnvironment.Display.DeviceName == "DISPLAY5", "Escape did not cancel without changing display.");
        }
        GameEnvironment.Display.Select("missing"); Assert(GameEnvironment.Display.Current.DeviceName == "DISPLAY1", "Missing saved device did not fall back.");
    }
    static void ReopenGames(string path)
    {
        GameEnvironment.Store = new GameStore(path);
        using (BaseballGame game = new BaseballGame())
        {
            BaseballSession session = (BaseballSession)Field(game, "session");
            session.SetActive(true); session.Advance(BaseballSession.ReadySeconds);
            session.Advance(session.PitchSeconds - BaseballSession.SwingContactDelay); session.Swing();
            session.Advance(BaseballSession.SwingContactDelay); session.Advance(session.FlightSeconds);
            Assert(GameEnvironment.Store.Data.BaseballScore == 1600, "Landing not saved.");
        }
        using (RunnerGame game = new RunnerGame())
        {
            IntPtr handle = game.Handle;
            RunnerSession session = (RunnerSession)Field(game, "session"); session.Active = true; session.Jump();
            for (int i = 0; i < 120; i++) session.Advance(1.0 / 60, game.Width);
            game.Close(); Assert(GameEnvironment.Store.Data.RunnerBest > 0, "Active run lost on close.");
        }
        using (PetGame game = new PetGame())
        {
            IntPtr handle = game.Handle;
            PetBehavior model = (PetBehavior)Field(game, "behavior");
            Rectangle area = GameEnvironment.Display.WorkingArea;
            model.AddFood(model.X + 38, area.Left, area.Right); model.Advance(0.1, area.Left, area.Right);
            game.Close(); Assert(GameEnvironment.Store.Data.PetMeals == 1, "Pet meal not saved.");
        }
        using (FortressGame game = new FortressGame())
        {
            IntPtr handle = game.Handle;
            typeof(FortressGame).GetField("focused", Private).SetValue(game, true);
            Call(game, "OnKeyDown", game, new KeyEventArgs(Keys.Space));
            Call(game, "OnKeyUp", game, new KeyEventArgs(Keys.Space)); game.Close();
            Assert(GameEnvironment.Store.Data.FortressShots == 1, "Fortress shot not saved.");
        }
        int best = GameEnvironment.Store.Data.RunnerBest;
        GameEnvironment.Store = new GameStore(path); GameEnvironment.Store.Load();
        using (BaseballGame baseball = new BaseballGame())
        using (RunnerGame runner = new RunnerGame())
        using (PetGame pet = new PetGame())
        using (FortressGame fortress = new FortressGame())
        {
            Assert(((BaseballSession)Field(baseball, "session")).Score == 0 && GameEnvironment.Store.Data.BaseballBestDistance == 160
                && GameEnvironment.Store.Data.BaseballPractice.BestScore == 1600, "Baseball record not restored separately from new run.");
            Assert(((RunnerSession)Field(runner, "session")).Best == best, "Runner best not restored.");
            Assert(((PetBehavior)Field(pet, "behavior")).Stats.Meals == 1, "Pet not restored.");
            Assert(((FortressSession)Field(fortress, "session")).Shots == 0 && GameEnvironment.Store.Data.FortressShots == 1, "New fortress run should start fresh and preserve lifetime records.");
        }
    }
}

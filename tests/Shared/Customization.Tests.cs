using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskbarGames.Shared;
using TaskbarGames.Games.Baseball;
using TaskbarGames.Games.Pet;

public static class CustomizationRegressionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static object Field(object target, string name) { return target.GetType().GetField(name, Private).GetValue(target); }
    static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Private).Invoke(target, args); }
    public static void Run(string artifacts)
    {
        // Random leisure must be reachable, finite and cosmetic; acorns still take priority.
        PetBehavior pet = new PetBehavior(new PetSession(), new Random(37), 300);
        HashSet<PetAction> seen = new HashSet<PetAction>();
        double previousX = pet.X; PetAction previous = pet.Action;
        for (int i = 0; i < 40000; i++)
        {
            pet.Advance(0.1, -1920, 0);
            if (PetBehavior.IsLeisure(pet.Action))
            {
                seen.Add(pet.Action);
                if (pet.Action == previous) Assert(pet.X == previousX, "Leisure should pause walking.");
            }
            Assert(pet.X >= -1920 && pet.X <= -76, "Pet escaped the display during leisure.");
            previous = pet.Action; previousX = pet.X;
        }
        Assert(seen.Count == 4, "Not all four activities appeared during seeded walks.");
        Assert(pet.Stats.Fullness == 75 && pet.Stats.Mood == 75 && pet.Stats.Meals == 0, "Cosmetic snacks changed care stats.");
        for (int i = 0; i < 30000 && !PetBehavior.IsLeisure(pet.Action); i++) pet.Advance(0.1, -1920, 0);
        Assert(PetBehavior.IsLeisure(pet.Action), "Leisure was not reachable again.");
        pet.AddFood(pet.X + 38, -1920, 0); pet.Advance(0.1, -1920, 0);
        Assert(pet.Action == PetAction.Eat && pet.Stats.Meals == 1, "Leisure blocked real food.");
        PetBehavior timing = new PetBehavior(new PetSession(), new Random(37), 0);
        for (int i = 0; i < 10000 && !PetBehavior.IsLeisure(timing.Action); i++) timing.Advance(0.1, 0, 1920);
        Assert(PetBehavior.IsLeisure(timing.Action), "Timed activity did not start.");
        double activityX = timing.X;
        for (int i = 0; i < 130; i++) timing.Advance(0.1, 0, 1920);
        Assert(timing.Action == PetAction.Walk && timing.X != activityX, "Leisure did not return to walking.");
        GameStore original = GameEnvironment.Store;
        string profile = Path.Combine(artifacts, "customization-profile-" + Guid.NewGuid().ToString("N") + ".xml");
        GameStore store = new GameStore(profile); store.Data.RunnerBest = 123;
        GameEnvironment.Store = store;
        try
        {
            using (BaseballGame game = new BaseballGame(GameMode.Challenge))
            {
                game.Show(); Application.DoEvents();
                ((Timer)Field(game, "timer")).Stop();
                BaseballSession session = (BaseballSession)Field(game, "session");
                session.SetActive(true); session.Advance(0.9);
                Call(game, "OpenTeamMenu", true, new Point(60, game.Height - 50));
                double elapsed = session.Elapsed; int attempts = session.Attempts;
                Call(game, "Tick", game, EventArgs.Empty);
                Call(game, "OnGameKeyDown", game, new KeyEventArgs(Keys.Space));
                Assert(session.Elapsed == elapsed && session.Attempts == attempts && session.SwingAge < 0, "Team selection advanced play or accepted a swing.");
                ToolStripDropDown menu = (ToolStripDropDown)Field(game, "teamMenu");
                Assert(menu.Items.Count == 10, "KBO team list is incomplete.");
                menu.PerformLayout();
                Assert(menu.Size == new Size(106, 122), "Team grid exceeded its compact size.");
                for (int i = 0; i < menu.Items.Count; i++)
                {
                    ToolStripItem item = menu.Items[i];
                    Assert(item.DisplayStyle == ToolStripItemDisplayStyle.Image && item.Image != null && item.Text.Length == 0, "Team grid must show only logos.");
                    Assert(menu.ClientRectangle.Contains(item.Bounds), "Team logo was clipped.");
                    Assert(item.Bounds.X == menu.Items[i % 3].Bounds.X && item.Bounds.Y == menu.Items[i / 3 * 3].Bounds.Y, "Team grid is not three columns by four rows.");
                }
                menu.Items[0].PerformClick(); menu.Close();
                Assert(store.Data.BatterTeam == "lg" && store.Data.PitcherTeam == "", "Batter choice affected pitcher.");
                Call(game, "OpenTeamMenu", false, new Point(300, game.Height - 50));
                menu.Items[6].PerformClick(); menu.Close();
                Assert(store.Data.PitcherTeam == "lotte" && store.Data.BatterTeam == "lg", "Pitcher choice affected batter.");
                game.Close();
            }
            using (PetGame game = new PetGame())
            {
                PetBehavior behavior = (PetBehavior)Field(game, "behavior");
                double fullness = behavior.Stats.Fullness;
                Acorn food = (Acorn)Call(game, "AddFood", behavior.X + 38);
                Type markerType = typeof(PetGame).Assembly.GetType("TaskbarGames.Games.Pet.AcornMarker");
                Form marker = (Form)Activator.CreateInstance(markerType);
                ((System.Collections.IDictionary)Field(game, "markers")).Add(food, marker);
                string[] foods = { "도토리", "뼈 간식", "생선", "대나무" };
                foreach (PetAnimal animal in Enum.GetValues(typeof(PetAnimal)))
                {
                    Call(game, "SelectAnimal", animal);
                    Assert(behavior.Stats.Fullness == fullness && behavior.Stats.Meals == 0, "Changing animals reset care state.");
                    ToolStripDropDown careMenu = (ToolStripDropDown)Field(game, "menu");
                    Assert(careMenu.Items[1].Text == foods[(int)animal] + " 놓기", "Food menu does not match the animal.");
                    Assert((PetAnimal)markerType.GetProperty("Animal").GetValue(marker, null) == animal && behavior.Food.Count == 1, "Changing animals failed to update placed food without losing it.");
                }
                Assert(((ContextMenuStrip)Field(game, "animalMenu")).Items.Count == 4, "Animal menu is incomplete.");
            }
            GameStore loaded = new GameStore(profile); loaded.Load();
            Assert(loaded.Data.BatterTeam == "lg" && loaded.Data.PitcherTeam == "lotte" && loaded.Data.PetAnimal == "panda", "Appearance choices were not saved.");
            Assert(loaded.Data.RunnerBest == 123, "Appearance changes reset records.");
            GameEnvironment.Store = loaded;
            using (BaseballGame game = new BaseballGame())
                Assert(((TeamUniform)Field(game, "batterUniform")).Id == "lg" && ((TeamUniform)Field(game, "pitcherUniform")).Id == "lotte", "Uniforms did not restore on reopen.");
            using (PetGame game = new PetGame()) Assert((PetAnimal)Field(game, "animal") == PetAnimal.Panda, "Animal did not restore on reopen.");
            Assert(TeamUniform.Find("invalid", true) == TeamUniform.DefaultBatter && PetAppearance.Parse("invalid") == PetAnimal.Squirrel, "Unknown appearance settings do not fall back safely.");
        }
        finally { GameEnvironment.Store = original; }
        Console.WriteLine("PASS: compact 3x4 logo grid, independent uniforms, paused selection, animal-specific food and saved appearances.");
    }
}

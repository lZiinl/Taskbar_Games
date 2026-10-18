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
        foreach (PetAnimal animal in Enum.GetValues(typeof(PetAnimal)))
            foreach (string pose in new[] { "cola", "popcorn", "read", "watchtv" })
                using (Bitmap first = new Bitmap(76, 56))
                using (Bitmap next = new Bitmap(76, 56))
                {
                    using (Graphics g = Graphics.FromImage(first)) PetAppearance.Draw(g, animal, 22, 27, true, pose, 0, 1);
                    using (Graphics g = Graphics.FromImage(next)) PetAppearance.Draw(g, animal, 22, 27, true, pose, 1, 1);
                    bool changed = false;
                    for (int y = 0; y < first.Height; y++)
                        for (int x = 0; x < first.Width; x++)
                            if (first.GetPixel(x, y) != next.GetPixel(x, y)) changed = true;
                    Assert(changed, "Activity has no animated frames: " + animal + " " + pose);
                }

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
                ContextMenuStrip menu = (ContextMenuStrip)Field(game, "teamMenu");
                Assert(menu.Items.Count == 10, "KBO team list is incomplete.");
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
                foreach (PetAnimal animal in Enum.GetValues(typeof(PetAnimal)))
                {
                    Call(game, "SelectAnimal", animal);
                    Assert(behavior.Stats.Fullness == fullness && behavior.Stats.Meals == 0, "Changing animals reset care state.");
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
        Render(artifacts);
        Console.WriteLine("PASS: 10 independent team uniforms, selection pauses play, four animals, cosmetic leisure, food priority and appearance persistence.");
    }
    static void Render(string artifacts)
    {
        string[] poses = { "idle", "walk", "sleep", "eat", "cola", "popcorn", "read", "watchtv" };
        using (Bitmap image = new Bitmap(800, 360))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.Clear(Color.FromArgb(220, 227, 236));
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < poses.Length; col++)
                {
                    int x = col * 100, y = row * 90;
                    g.DrawString(((PetAnimal)row).ToString() + " " + poses[col], SystemFonts.DefaultFont, Brushes.Black, x, y);
                    PetAppearance.Draw(g, (PetAnimal)row, x + 20, y + 28, true, poses[col], 0.5, 1);
                    PetAppearance.Draw(g, (PetAnimal)row, x + 20, y + 59, false, poses[col], 1, 1);
                }
            image.Save(Path.Combine(artifacts, "pet-animals-activities.png"));
        }
        Type animator = typeof(BaseballGame).Assembly.GetType("TaskbarGames.Games.Baseball.BatterAnimation");
        MethodInfo draw = animator.GetMethod("Draw", new[] { typeof(Graphics), typeof(int), typeof(double), typeof(TeamUniform) });
        string[] pitcher = (string[])typeof(BaseballGame).GetField("Pitcher", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        using (Bitmap image = new Bitmap(600, 200))
        using (Graphics g = Graphics.FromImage(image))
        {
            g.Clear(Color.FromArgb(220, 227, 236));
            for (int i = 0; i < TeamUniform.All.Length; i++)
            {
                TeamUniform team = TeamUniform.All[i];
                System.Drawing.Drawing2D.GraphicsState state = g.Save();
                g.TranslateTransform(i % 5 * 120, i / 5 * 100);
                g.DrawString(team.Name, SystemFonts.DefaultFont, Brushes.Black, 2, 2);
                g.TranslateTransform(0, 20); g.ScaleTransform(0.65f, 0.65f);
                draw.Invoke(null, new object[] { g, 80, -1.0, team });
                team.DrawSprite(g, pitcher, 125, 32);
                g.Restore(state);
            }
            image.Save(Path.Combine(artifacts, "baseball-team-uniforms.png"));
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAIkariWarriors.Engine;
using AVAIkariWarriors.Views;
using Controls = AVAIkariWarriors.Engine.Controls;

namespace AVAIkariWarriors.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set IKARI_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaikariwarriors-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("IKARI_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAIkariWarriors-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilots play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        WarriorPilot a = new(), b = new();
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(a.Decide(g, 0), g.Players[1].Joined ? b.Decide(g, 1) : default);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RED", 186500, 6);
        g.HighScores.Insert("BLU", 121200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A stage, and the computer at work in it (with one commando or both) until something worth showing.</summary>
    private static void Fight(int stage, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200, int players = 2)
    {
        var (w, c, g) = Open(seed);
        g.Go(stage, players);
        for (int i = 0; i < players; i++) g.SetLives(i, 30);
        Assert.True(PlayUntil(g, c, g => g.Phase is Phase.Play or Phase.Finale && g.Fighting.All(p => p.Safe <= 0) && g.Fighting.Count() == players && busy(g), seconds), $"no {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void A_two_player_firefight() => Fight(1, "firefight", g =>
        g.Stage.Number == 1 && g.Camera > 200 && g.Soldiers.Count(s => s.Y < g.ViewTop - 20 && s.Y > g.Camera + 60) >= 3 && g.Bullets.Any(b => !b.Hostile)
        && g.Players.All(p => p.Tank == null && p.Y > g.Camera + 60 && InTheOpen(g, p)), seed: 4, seconds: 300);

    /// <summary>Not under the trees, where he'd be hidden.</summary>
    private static bool InTheOpen(GameEngine g, Warrior p) =>
        new[] { (0, 0), (0, 1), (0, -1) }.All(d => g.Stage.At(Field.ColOf(p.X) + d.Item1, Field.RowOf(p.Y) + d.Item2) != Ground.Tree);

    [AvaloniaFact]
    public void In_a_tank() => Fight(1, "tank", g =>
        g.Players.Any(p => p.Tank != null && p.Y > g.Camera + 150) && g.Shells.Any(s => !s.Hostile) && g.Soldiers.Count(s => s.Y < g.ViewTop) >= 2, seconds: 300);

    [AvaloniaFact]
    public void A_grenade_going_off() => Fight(3, "grenade", g =>
        g.Effects.Any(e => e.Kind == EffectKind.Blast && e.Size is > 1 and < 1.1 && e.Age > 0.12 && e.Age < 0.22 && e.Y > g.Camera + 150 && e.Y < g.ViewTop - 120)
        && g.Players.All(p => p.Tank == null) && g.Soldiers.Count >= 2, seconds: 300);

    [AvaloniaFact]
    public void Across_the_river() => Fight(2, "river", g =>
        g.Players.Any(p => p.Wading && g.Stage.AtPoint(p.X, p.Y - Field.Cell) == Ground.Water && g.Stage.AtPoint(p.X, p.Y + Field.Cell) == Ground.Water)
        && g.Soldiers.Count(s => s.Y < g.ViewTop && s.Y > g.Camera + 40) >= 3 && g.Players.All(p => p.Tank == null), seconds: 300);

    [AvaloniaFact]
    public void The_fortress_gate()
    {
        var (w, c, g) = Open(5);
        g.Go(4, players: 2);
        g.SetLives(0, 30);
        g.SetLives(1, 30);
        g.MoveCamera(g.Stage.CameraEnd - 200);
        Assert.True(PlayUntil(g, c, g => g.AtEnd && g.Message == null && g.GateHp < g.GateMaxHp && g.GateHp > 2 && g.Grenades.Any(n => !n.Hostile && n.T > n.Time * 0.4) && g.Fighting.Count() == 2, 240), "no gate");
        Shot(w, c, "gate");
    }
}

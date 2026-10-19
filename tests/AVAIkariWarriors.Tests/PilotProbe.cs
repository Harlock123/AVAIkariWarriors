using AVAIkariWarriors.Engine;

namespace AVAIkariWarriors.Tests;

/// <summary>
/// How the computer players get on, stage by stage: the march to the end and the fight there (set IKARI_PROBE to run
/// it, for tuning: "1" for one commando, "2" for both, "lazy" for one that just runs up the screen firing ahead).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("IKARI_PROBE");
        if (mode is null) return;
        int players = mode == "2" ? 2 : 1;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "ikari-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame(players);
            for (int i = 0; i < players; i++) g.SetLives(i, 99);
            WarriorPilot[] pilots = [new(), new()];
            var deaths = new Dictionary<string, int>();
            var stages = new List<string>();
            var doing = new Dictionary<string, int>();
            bool[] alive = [true, true];
            int stage = 1, t = 0, end = -1, tankTime = 0;
            double start = 0;
            for (; t < 60 * 1200 && g.Stage.Number <= 5 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Dx: t % 240 < 120 ? 1 : -1, Dy: 1, Fire: true) : pilots[0].Decide(g, 0);
                g.Tick(c, players > 1 ? pilots[1].Decide(g, 1) : default);
                if (g.Phase == Phase.Play) doing[pilots[0].Doing] = doing.GetValueOrDefault(pilots[0].Doing) + 1;
                if (g.Players.Any(p => p.Tank != null)) tankTime++;
                foreach (var p in g.Players.Where(p => p.Joined))
                {
                    if (alive[p.Index] && !p.Alive) deaths[$"S{g.Stage.Number} {p.Cause}"] = deaths.GetValueOrDefault($"S{g.Stage.Number} {p.Cause}") + 1;
                    alive[p.Index] = p.Alive;
                }
                if (g.AtEnd && end < 0) end = t;
                if (g.Stage.Number != stage) { stages.Add($"S{stage}:{(end - start) / 60:0}+{(t - end) / 60:0}s"); stage = g.Stage.Number; start = t; end = -1; }
            }
            output.WriteLine($"seed {seed}: stage {g.Stage.Number} after {t / 60:0}s score {g.Players[0].Score}/{g.Players[1].Score} | {string.Join(" ", stages)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   ammo {g.Players[0].Ammo} grenades {g.Players[0].Grenades} camera {g.Camera:0}/{g.Stage.CameraEnd:0} in a tank {tankTime / 60}s");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}

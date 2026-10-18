using AVAAmidar.Engine;

namespace AVAAmidar.Tests;

/// <summary>How the computer player gets on (set AMIDAR_PROBE to run it; AMIDAR_PROBE=lazy just runs round and round).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_player_does()
    {
        if (Environment.GetEnvironmentVariable("AMIDAR_PROBE") is not { } mode) return;
        int last = int.TryParse(Environment.GetEnvironmentVariable("AMIDAR_LEVELS"), out int n) ? n : 6;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        var p = new Pilot();
        var deaths = new List<string>();
        Sim.StartLevel(w, 1);
        double levelStart = 0;
        int t = 0, jumps = 0;
        for (; t < 60 * 60 * 20 && w.Level <= last && deaths.Count < 12; t++)
        {
            Controls c;
            if (mode == "lazy")
            {
                // Round the outside, then a turn inward now and then.
                var pl = w.Player;
                c = new Controls(pl.E < 0 && w.Dice.Next() < 0.3 ? (Dir)(1 + w.Dice.Next(4)) : pl.Way == Dir.None ? Dir.Right : pl.Way);
            }
            else c = p.Decide(w);
            if (c.Jump && w.Jumping <= 0 && w.Jumps > 0) jumps++;
            Sim.Step(w, c);
            if (w.Dead)
            {
                deaths.Add($"L{w.Level} {w.Cause} at ({w.Player.X:0},{w.Player.Y:0}) painted {w.PaintedCells}/{Board.The.TotalCells} [{p.Doing}]");
                Sim.Restart(w);
                p.Reset();
            }
            if (w.Cleared)
            {
                output.WriteLine($"level {w.Level} cleared in {(w.Clock - levelStart):0}s, score {w.Score}");
                levelStart = w.Clock;
                int next = w.Level + 1;
                double clock = w.Clock;
                Sim.StartLevel(w, next);
                w.Clock = clock;
                p.Reset();
            }
        }
        output.WriteLine($"reached level {w.Level} after {t / 60}s; {deaths.Count} deaths, {jumps} jumps, score {w.Score}, painted {w.PaintedCells}/{Board.The.TotalCells}, boxes {w.FilledBoxes}/{Board.The.Boxes.Count}; {sw.ElapsedMilliseconds} ms");
        foreach (var d in deaths) output.WriteLine("   " + d);
    }
}

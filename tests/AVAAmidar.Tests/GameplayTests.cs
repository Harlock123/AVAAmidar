using Avalonia.Input;
using AVAAmidar.Engine;

namespace AVAAmidar.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaamidar-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    private static Board Bd => Board.The;

    /// <summary>A level with the warriors held off the board.</summary>
    private static World Quiet(int level = 1)
    {
        var w = new World();
        Sim.StartLevel(w, level);
        foreach (var e in w.Enemies) e.Wait = 1e9;
        return w;
    }

    private static void Run(World w, double seconds, Controls c = default)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 60); i++) Sim.Step(w, c);
    }

    /// <summary>
    /// Walks the player from meeting to meeting along a list of ways, pressing each turn as the meeting comes (the
    /// player doesn't stop at a meeting it can go on through, so the last way carries on if it can).
    /// </summary>
    private static void Walk(World w, params Dir[] ways)
    {
        int at = w.Player.N;
        for (int k = 0; k < ways.Length; k++)
        {
            var e = Bd.Edges[Bd.Nodes[at].Out[(int)ways[k]]];
            var to = Bd.Nodes[e.A == at ? e.B : e.A];
            var next = k + 1 < ways.Length ? ways[k + 1] : ways[k];
            for (int i = 0; i < 600; i++)
            {
                bool last = Math.Abs(w.Player.X - to.X) + Math.Abs(w.Player.Y - to.Y) <= Sim.PlayerSpeed(w.Level) * Field.Step + 1e-9;
                Sim.Step(w, new Controls(last ? next : ways[k]));
                if (last) break;
            }
            at = to.Id;
        }
    }

    private static void PlaceAt(World w, int row, int col) => w.Player.Place(Bd.At(row, col)!);

    // ───────────────────────────── the board ─────────────────────────────

    [Fact]
    public void The_board_has_thirty_two_boxes_and_four_corners_and_every_box_is_closed()
    {
        Assert.Equal(32, Bd.Boxes.Count);
        Assert.Equal(4, Bd.Boxes.Count(b => b.Corner));
        foreach (var b in Bd.Boxes)
        {
            Assert.Equal(2, b.Edges.Count(e => !Bd.Edges[e].Horizontal));
            Assert.Equal(2 * (b.X2 - b.X1), b.Edges.Where(e => Bd.Edges[e].Horizontal).Sum(e => Bd.Edges[e].Length), 6);
        }
        // Every meeting can be reached from the start.
        var seen = new HashSet<int> { Sim.Start.Id };
        var q = new Queue<int>(seen);
        while (q.Count > 0)
            foreach (int id in Bd.Nodes[q.Dequeue()].Out)
                if (id >= 0)
                {
                    var e = Bd.Edges[id];
                    foreach (int m in new[] { e.A, e.B }) if (seen.Add(m)) q.Enqueue(m);
                }
        Assert.Equal(Bd.Nodes.Count, seen.Count);
    }

    // ───────────────────────────── the player and the paint ─────────────────────────────

    [Fact]
    public void Going_along_a_line_paints_it_and_painting_all_round_a_box_fills_it()
    {
        var w = Quiet();
        PlaceAt(w, 0, 0);
        Walk(w, Dir.Right, Dir.Down);
        int top = Bd.At(0, 0)!.Out[(int)Dir.Right];
        Assert.True(w.EdgeDone(top));
        Assert.False(w.Filled[0]);
        Assert.Equal(w.PaintedCells * Sim.CellPoints, w.Score);
        Walk(w, Dir.Left, Dir.Left, Dir.Up);
        Assert.Equal((Field.Left, Field.Top), (w.Player.X, w.Player.Y));
        Assert.True(w.Filled[0]);
        Assert.Equal(1, w.FilledBoxes);
        Assert.Contains(Sfx.Fill, w.Heard);
        Assert.Equal(w.PaintedCells * Sim.CellPoints + Sim.BoxPoints, w.Score);
    }

    [Fact]
    public void A_turn_held_is_taken_at_the_next_meeting_and_straight_on_otherwise_and_the_player_can_turn_back_between()
    {
        var w = Quiet();
        PlaceAt(w, 0, 0);
        // Down held from the start of the top line: no way down until column 2.
        Run(w, 0.1, new Controls(Dir.Right));
        Run(w, 2, new Controls(Dir.Down));
        Assert.Equal(Bd.Xs[2], w.Player.X, 3);
        Assert.True(w.Player.Y > Bd.Ys[0]);
        // Turn back between meetings.
        PlaceAt(w, 0, 4);
        Run(w, 0.2, new Controls(Dir.Right));
        double x = w.Player.X;
        Run(w, 0.2, new Controls(Dir.Left));
        Assert.True(w.Player.X < x);
        // Into a corner, and stop there.
        PlaceAt(w, 0, 9);
        Run(w, 5, new Controls(Dir.Right));
        Assert.Equal((Bd.Xs[Board.Cols], Bd.Ys[0]), (w.Player.X, w.Player.Y));
        Assert.Equal(Dir.None, w.Player.Way);
    }

    // ───────────────────────────── the warriors ─────────────────────────────

    [Fact]
    public void A_warrior_goes_the_Amidar_way_along_then_down_then_along_turning_back_at_the_edges()
    {
        var w = Quiet();
        PlaceAt(w, Board.Rows, 7);
        w.Jumping = 1e9; // the player can't be caught
        var e = w.Enemies[1];
        e.Wait = 0;
        e.M.Place(Bd.At(0, 4)!);
        e.H = 1;
        e.V = 1;
        var turns = new List<(Dir Way, int Row, int Col)>();
        var last = Dir.None;
        for (int i = 0; i < 60 * 30; i++)
        {
            Sim.Step(w, default);
            if (e.M.Way != last && e.M.E >= 0)
            {
                var n = Bd.Nodes[e.M.Way is Dir.Right or Dir.Down ? Bd.Edges[e.M.E].A : Bd.Edges[e.M.E].B];
                turns.Add((e.M.Way, n.Row, n.Col));
                last = e.M.Way;
            }
            if (w.Dead) break;
        }
        // Along, then down, alternating, while there's an upright to go down.
        Assert.Equal(Dir.Right, turns[0].Way);
        Assert.Equal(Dir.Down, turns[1].Way);
        Assert.Contains(turns, t => t.Way == Dir.Up);   // turned back at the bottom
        Assert.Contains(turns, t => t.Way == Dir.Left); // and at the side
        for (int k = 1; k < turns.Count; k++)
            if (turns[k].Way is Dir.Up or Dir.Down) Assert.True(turns[k - 1].Way is Dir.Left or Dir.Right);
    }

    [Fact]
    public void The_tracer_goes_round_the_outside()
    {
        var w = Quiet();
        PlaceAt(w, 3, 6);
        var t = w.Enemies[0];
        Assert.True(t.Tracer);
        t.Wait = 0;
        w.Jumping = 1e9;
        bool topRight = false, bottomLeft = false;
        for (int i = 0; i < 60 * 40; i++)
        {
            Sim.Step(w, default);
            Assert.True(Math.Abs(t.M.X - Field.Left) < 0.01 || Math.Abs(t.M.X - Field.Right) < 0.01 || Math.Abs(t.M.Y - Field.Top) < 0.01 || Math.Abs(t.M.Y - Field.Bottom) < 0.01);
            topRight |= Math.Abs(t.M.X - Field.Right) < 3 && Math.Abs(t.M.Y - Field.Top) < 3;
            bottomLeft |= Math.Abs(t.M.X - Field.Left) < 3 && Math.Abs(t.M.Y - Field.Bottom) < 3;
        }
        Assert.True(topRight && bottomLeft);
    }

    [Fact]
    public void A_warrior_touching_the_player_is_the_end_unless_they_jump_over_him()
    {
        var w = Quiet();
        PlaceAt(w, 3, 0);
        var e = w.Enemies[1];
        e.Wait = 0;
        e.M.Place(Bd.At(3, 1)!);
        e.H = -1;
        e.Across = false;
        Run(w, 1);
        Assert.True(w.Dead);
        Assert.Equal("a warrior", w.Cause);
        // Again, jumping.
        w = Quiet();
        PlaceAt(w, 3, 0);
        e = w.Enemies[1];
        e.Wait = 0;
        e.M.Place(Bd.At(3, 1)!);
        e.H = -1;
        e.Across = false;
        Sim.Step(w, new Controls(Jump: true));
        Assert.Equal(Sim.JumpsPerLevel - 1, w.Jumps);
        Run(w, 0.6);
        Assert.False(w.Dead);
        Assert.Contains(Sfx.Jump, w.Heard);
        // No jumps left: no jump.
        w.Jumps = 0;
        Run(w, 2);
        Sim.Step(w, new Controls(Jump: true));
        Assert.Equal(0, w.Jumping);
    }

    [Fact]
    public void Filling_the_four_corners_lets_the_player_catch_the_warriors_for_a_while()
    {
        var w = Quiet();
        foreach (var b in Bd.Boxes.Where(b => b.Corner).Skip(1))
        {
            foreach (int id in b.Edges) Array.Fill(w.Paint[id], true);
            w.Filled[b.Id] = true;
        }
        var last = Bd.Boxes.First(b => b.Corner);
        // Paint all but the last line of the last corner, and walk it.
        foreach (int id in last.Edges) Array.Fill(w.Paint[id], true);
        int top = Bd.At(0, 0)!.Out[(int)Dir.Right];
        Array.Fill(w.Paint[top], false);
        PlaceAt(w, 0, 0);
        Walk(w, Dir.Right);
        Assert.True(w.CornersDone);
        Assert.True(w.Power > 0);
        var e = w.Enemies[1];
        e.Wait = 0;
        e.M.Place(Bd.At(0, 4)!);
        e.H = -1;
        e.Across = false;
        int score = w.Score;
        Run(w, 1);
        Assert.True(e.Caught);
        Assert.True(w.Score >= score + 200); // the catch, and whatever more it painted
        Assert.False(w.Dead);
        Run(w, Sim.PowerSeconds);
        Assert.False(e.Caught);
    }

    [Fact]
    public void Filling_every_box_clears_the_level()
    {
        var w = Quiet();
        foreach (var e in Bd.Edges) Array.Fill(w.Paint[e.Id], true);
        int top = Bd.At(0, 0)!.Out[(int)Dir.Right];
        Array.Fill(w.Paint[top], false);
        foreach (var b in Bd.Boxes) w.Filled[b.Id] = !b.Edges.Contains(top);
        w.FilledBoxes = w.Filled.Count(f => f);
        PlaceAt(w, 0, 0);
        int score = w.Score;
        Walk(w, Dir.Right);
        Assert.True(w.Cleared);
        Assert.True(w.Score >= score + Sim.ClearPoints + Sim.BoxPoints);
        Assert.Contains(Sfx.Clear, w.Heard);
    }

    [Fact]
    public void Each_level_the_warriors_are_quicker_and_more_of_them()
    {
        Assert.True(Sim.EnemySpeed(4) > Sim.EnemySpeed(1));
        Assert.True(Sim.EnemySpeed(1) < Sim.PlayerSpeed(1));
        Assert.Equal(5, Quiet(1).Enemies.Count);
        Assert.Equal(7, Quiet(3).Enemies.Count);
    }

    // ───────────────────────────── the computer player ─────────────────────────────

    [Fact]
    public void The_computer_player_clears_the_first_two_levels_without_being_caught()
    {
        var w = new World();
        Sim.StartLevel(w, 1);
        var p = new Pilot();
        for (int t = 0; t < 60 * 60 * 15 && w.Level <= 2; t++)
        {
            Sim.Step(w, p.Decide(w));
            Assert.False(w.Dead, $"caught on level {w.Level} by {w.Cause}");
            if (w.Cleared) { Sim.StartLevel(w, w.Level + 1); p.Reset(); }
        }
        Assert.Equal(3, w.Level);
    }

    [Fact]
    public void Wandering_about_gets_caught()
    {
        var w = new World();
        Sim.StartLevel(w, 1);
        int deaths = 0;
        for (int t = 0; t < 60 * 120; t++)
        {
            var pl = w.Player;
            Sim.Step(w, new Controls(pl.E < 0 && w.Dice.Next() < 0.3 ? (Dir)(1 + w.Dice.Next(4)) : pl.Way == Dir.None ? Dir.Right : pl.Way));
            if (w.Dead) { deaths++; Sim.Restart(w); }
        }
        Assert.True(deaths >= 3);
        Assert.False(w.Cleared);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Three_lives_an_extra_one_every_twenty_thousand_then_the_end_and_a_high_score()
    {
        var g = Engine();
        g.Go();
        g.World.Score = GameEngine.ExtraLifeEvery - 1;
        g.Run(60, new Controls(Dir.Left));
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
        g.SetLives(1);
        var e = g.World.Enemies[0];
        e.Wait = 0;
        e.M = g.World.Player.Copy();
        g.Run(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "APE") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("APE", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void Odd_levels_are_the_gorilla_and_even_the_paint_roller_and_a_cleared_level_moves_on()
    {
        var g = Engine();
        g.StartGame();
        Assert.Contains("GORILLA", g.Message);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        foreach (var e in Board.The.Edges) Array.Fill(g.World.Paint[e.Id], true);
        int top = Board.The.At(0, 0)!.Out[(int)Dir.Right];
        Array.Fill(g.World.Paint[top], false);
        foreach (var b in Board.The.Boxes) g.World.Filled[b.Id] = !b.Edges.Contains(top);
        g.World.FilledBoxes = g.World.Filled.Count(f => f);
        foreach (var e in g.World.Enemies) e.Wait = 1e9;
        g.World.Player.Place(Board.The.At(0, 0)!);
        for (int i = 0; i < 120 && g.Phase == Phase.Play; i++) g.Tick(new Controls(Dir.Right));
        Assert.Equal(Phase.Cleared, g.Phase);
        g.Run((int)(60 * GameEngine.ClearedSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Contains("PAINT ROLLER", g.Message);
        Assert.Equal(0, g.World.FilledBoxes);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void A_copied_world_goes_on_just_as_the_original()
    {
        var a = new World();
        Sim.StartLevel(a, 2);
        var p = new Pilot();
        for (int i = 0; i < 600; i++) Sim.Step(a, p.Decide(a));
        var b = a.Copy();
        for (int i = 0; i < 600; i++) { Sim.Step(a, new Controls(Dir.Up)); Sim.Step(b, new Controls(Dir.Up)); }
        Assert.Equal(a.Score, b.Score);
        Assert.Equal((a.Player.X, a.Player.Y), (b.Player.X, b.Player.Y));
        Assert.Equal(a.Enemies.Select(e => (e.M.X, e.M.Y)), b.Enemies.Select(e => (e.M.X, e.M.Y)));
    }

    [Fact]
    public void The_keys_the_last_arrow_pressed_counts_and_space_jumps()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Right);
        Assert.Equal(Dir.Right, input.Player.Way);
        input.OnKeyDown(Key.Up);
        Assert.Equal(Dir.Up, input.Player.Way);
        input.OnKeyUp(Key.Up);
        Assert.Equal(Dir.Right, input.Player.Way);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Jump);
    }
}

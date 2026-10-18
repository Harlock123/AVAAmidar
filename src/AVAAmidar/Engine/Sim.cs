namespace AVAAmidar.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double Touch = 20, JumpSeconds = 1.1, PowerSeconds = 6.5;
    public const int JumpsPerLevel = 4, CellPoints = 1, BoxPoints = 100, ClearPoints = 2000;

    public static double PlayerSpeed(int level) => Math.Min(180, 152 + level * 3);
    public static double EnemySpeed(int level) => Math.Min(165, 92 + level * 7);
    /// <summary>How many warriors, besides the tracer: more each level.</summary>
    public static int Warriors(int level) => Math.Min(6, 3 + level);

    public static Node Start => Board.The.At(Board.Rows, 7)!;

    public static void StartLevel(World w, int level)
    {
        w.Level = level;
        w.Paint = Board.The.Edges.Select(e => new bool[e.Cells]).ToArray();
        w.Filled = new bool[Board.The.Boxes.Count];
        w.PaintedCells = w.FilledBoxes = 0;
        w.Jumps = JumpsPerLevel;
        w.CornersDone = false;
        w.Flashes.Clear();
        w.Dice = new Dice(level * 31 + 7);
        Restart(w);
    }

    /// <summary>The player back at the start, and the warriors back at the top to come on one by one.</summary>
    public static void Restart(World w)
    {
        w.Dead = false;
        w.Player = new Mover { Facing = Dir.Right };
        w.Player.Place(Start);
        w.Jumping = w.Power = 0;
        w.Caught = 0;
        w.Enemies.Clear();
        var bd = Board.The;
        var tracer = new Enemy { Tracer = true, Wait = 0.5 };
        tracer.M.Place(bd.At(0, 0)!);
        w.Enemies.Add(tracer);
        int[] cols = [12, 4, 9, 2, 7, 0];
        for (int k = 0; k < Warriors(w.Level); k++)
        {
            var e = new Enemy { H = k % 2 == 0 ? -1 : 1, V = 1, Wait = 1.2 + k * 1.4 };
            e.M.Place(bd.At(0, cols[k])!);
            w.Enemies.Add(e);
        }
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        if (w.Dead || w.Cleared) return;
        w.Clock += dt;
        if (c.Jump && w.Jumping <= 0 && w.Jumps > 0)
        {
            w.Jumps--;
            w.Jumping = JumpSeconds;
            w.Heard.Add(Sfx.Jump);
        }
        w.Jumping = Math.Max(0, w.Jumping - dt);
        if (w.Power > 0 && (w.Power -= dt) <= 0)
        {
            w.Power = 0;
            // The corners' power over: the caught come back at the top.
            foreach (var e in w.Enemies.Where(e => e.Caught))
            {
                e.Caught = false;
                e.Wait = 1.5;
                e.M.Place(Board.The.At(0, 6) ?? Board.The.At(0, 7)!);
            }
        }
        MovePlayer(w, c.Way, dt);
        foreach (var e in w.Enemies) MoveEnemy(w, e, dt);
        Touches(w);
        for (int i = w.Flashes.Count - 1; i >= 0; i--)
        {
            var f = w.Flashes[i];
            if ((f.T += dt) > 0.6) w.Flashes.RemoveAt(i);
            else w.Flashes[i] = f;
        }
    }

    // ───────────────────────────── moving along the lines ─────────────────────────────

    /// <summary>
    /// Moves along the lines a distance, choosing a way at each meeting (None: stop there). Paints as it goes if it's
    /// the player.
    /// </summary>
    private static void Move(World w, Mover m, double dist, Func<Node, Dir, Dir> choose, bool paint)
    {
        var bd = Board.The;
        for (int guard = 0; dist > 1e-9 && guard < 8; guard++)
        {
            if (m.E < 0)
            {
                var n = bd.Nodes[m.N];
                var way = choose(n, m.Way);
                if (way == Dir.None || n.Out[(int)way] < 0) { m.Way = Dir.None; return; }
                m.Way = way;
                m.Facing = way;
                m.E = n.Out[(int)way];
            }
            var e = bd.Edges[m.E];
            var target = m.Way is Dir.Right or Dir.Down ? bd.Nodes[e.B] : bd.Nodes[e.A];
            double d = Math.Abs(target.X - m.X) + Math.Abs(target.Y - m.Y);
            double go = Math.Min(d, dist);
            var (sx, sy) = Board.Step(m.Way);
            double x0 = m.X, y0 = m.Y;
            m.X += sx * go;
            m.Y += sy * go;
            dist -= go;
            if (go >= d - 1e-9) { m.X = target.X; m.Y = target.Y; }
            if (paint) PaintAlong(w, e, x0, y0, m.X, m.Y);
            if (go >= d - 1e-9) { m.E = -1; m.N = target.Id; }
        }
    }

    private static void MovePlayer(World w, Dir want, double dt)
    {
        var p = w.Player;
        // Mid-line, it can turn straight back.
        if (p.E >= 0 && want != Dir.None && want == Board.Opposite(p.Way)) { p.Way = want; p.Facing = want; }
        if (p.E < 0 && p.Way == Dir.None && want == Dir.None) return;
        Move(w, p, PlayerSpeed(w.Level) * dt, (n, cur) =>
        {
            if (want != Dir.None && n.Out[(int)want] >= 0) return want;
            if (cur != Dir.None && n.Out[(int)cur] >= 0) return cur;
            return Dir.None;
        }, paint: true);
    }

    private static void MoveEnemy(World w, Enemy e, double dt)
    {
        if (e.Caught) return;
        if (e.Wait > 0) { e.Wait -= dt; return; }
        Move(w, e.M, EnemySpeed(w.Level) * dt, (n, cur) => e.Tracer ? Round(n) : Amidar(e, n), paint: false);
    }

    /// <summary>The tracer: round the outside of the board, clockwise.</summary>
    private static Dir Round(Node n)
    {
        int r = n.Row, c = n.Col;
        if (r == 0 && c < Board.Cols) return Dir.Right;
        if (c == Board.Cols && r < Board.Rows) return Dir.Down;
        if (r == Board.Rows && c > 0) return Dir.Left;
        return Dir.Up;
    }

    /// <summary>The Amidar way: after a stretch up or down, one along; after one along, one up or down if there's an upright, else along again; turning back at the edges.</summary>
    private static Dir Amidar(Enemy e, Node n)
    {
        Dir Across()
        {
            if (n.Out[(int)(e.H > 0 ? Dir.Right : Dir.Left)] < 0) e.H = -e.H;
            e.Across = true;
            return e.H > 0 ? Dir.Right : Dir.Left;
        }
        if (!e.Across) return Across();
        if (n.Row == 0 && e.V < 0 || n.Row == Board.Rows && e.V > 0) e.V = -e.V;
        var down = e.V > 0 ? Dir.Down : Dir.Up;
        if (n.Out[(int)down] >= 0) { e.Across = false; return down; }
        return Across();
    }

    // ───────────────────────────── painting ─────────────────────────────

    /// <summary>Paints the pieces of an edge between two points on it; edges done fill the boxes they finish.</summary>
    private static void PaintAlong(World w, Edge e, double x0, double y0, double x1, double y1)
    {
        var a = Board.The.Nodes[e.A];
        double s0 = Math.Abs(x0 - a.X) + Math.Abs(y0 - a.Y), s1 = Math.Abs(x1 - a.X) + Math.Abs(y1 - a.Y);
        int k0 = Math.Clamp((int)(Math.Min(s0, s1) / Field.Cell), 0, e.Cells - 1), k1 = Math.Clamp((int)(Math.Max(s0, s1) / Field.Cell), 0, e.Cells - 1);
        var paint = w.Paint[e.Id];
        int fresh = 0;
        for (int k = k0; k <= k1; k++)
            if (!paint[k]) { paint[k] = true; fresh++; }
        if (fresh == 0) return;
        w.PaintedCells += fresh;
        w.Score += fresh * CellPoints;
        if (!paint.All(p => p)) return;
        foreach (int b in e.Boxes)
        {
            if (w.Filled[b] || !Board.The.Boxes[b].Edges.All(w.EdgeDone)) continue;
            w.Filled[b] = true;
            w.FilledBoxes++;
            w.Score += BoxPoints;
            w.Flashes.Add((b, 0));
            w.Heard.Add(Sfx.Fill);
        }
        // All four corners: the warriors can be caught for a while.
        if (!w.CornersDone && Board.The.Boxes.Where(b => b.Corner).All(b => w.Filled[b.Id]))
        {
            w.CornersDone = true;
            w.Power = PowerSeconds;
            w.Caught = 0;
            w.Heard.Add(Sfx.Corners);
        }
        if (w.Cleared)
        {
            w.Score += ClearPoints;
            w.Heard.Add(Sfx.Clear);
        }
    }

    // ───────────────────────────── meeting the warriors ─────────────────────────────

    private static void Touches(World w)
    {
        var p = w.Player;
        foreach (var e in w.Enemies)
        {
            if (e.Caught || e.Wait > 0) continue;
            if (Math.Abs(e.M.X - p.X) + Math.Abs(e.M.Y - p.Y) >= Touch) continue;
            if (w.Power > 0)
            {
                e.Caught = true;
                w.Caught++;
                w.Score += 100 << Math.Min(w.Caught, 4);
                w.Heard.Add(Sfx.Catch);
                continue;
            }
            if (w.Jumping > 0) continue;
            w.Dead = true;
            w.Cause = e.Tracer ? "the tracer" : "a warrior";
            w.Heard.Add(Sfx.Die);
            return;
        }
    }
}

namespace AVAAmidar.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. At every meeting of lines, and every few moments
/// between, it tries each way it could go on a copy of the board: that way to the next meeting, each way from there,
/// then on toward the nearest line not yet painted, keeping off lines the warriors are on, for a couple of seconds,
/// with the warriors going their fixed ways. It takes the way
/// that paints most and ends nearest more to paint without being caught. If every way gets it caught, it jumps.
/// </summary>
public sealed class Pilot
{
    public const int Ahead = 150, Every = 10;
    private int _wait;
    private Dir _way;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _wait = 0;
        _way = Dir.None;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var p = w.Player;
        if (w.Dead || w.Cleared) return default;
        if (p.E >= 0 && --_wait > 0 && _way != Dir.None) return new Controls(_way);
        _wait = Every;
        var bd = Board.The;
        // At a meeting: each way out of it. Between: straight back, or each way out of the meeting ahead.
        List<Dir> options;
        if (p.E < 0) options = Enumerable.Range(1, 4).Select(d => (Dir)d).Where(d => bd.Nodes[p.N].Out[(int)d] >= 0).ToList();
        else
        {
            var e = bd.Edges[p.E];
            var ahead = bd.Nodes[p.Way is Dir.Right or Dir.Down ? e.B : e.A];
            var back = Board.Opposite(p.Way);
            options = [back, .. Enumerable.Range(1, 4).Select(d => (Dir)d).Where(d => d != back && ahead.Out[(int)d] >= 0)];
        }
        var far = Distances(w);
        double best = double.NegativeInfinity, bestSafe = double.NegativeInfinity;
        Dir pick = options[0];
        foreach (var d in options)
        {
            double v = Value(w, d, far, jump: false);
            if (v > best) { best = v; pick = d; }
        }
        bestSafe = best;
        bool jump = false;
        // Caught whichever way: jump, if there's a jump left and it saves us.
        if (bestSafe < -50000 && w.Jumps > 0 && w.Jumping <= 0)
        {
            foreach (var d in options)
            {
                double v = Value(w, d, far, jump: true);
                if (v > best || !jump) { best = v; pick = d; jump = true; }
            }
        }
        _way = pick;
        Doing = jump ? "jumping" : best < -50000 ? "cornered" : w.Power > 0 ? "hunting" : "painting";
        return new Controls(pick, jump);
    }

    /// <summary>How far each meeting is from the nearest line still to paint (along the lines).</summary>
    public static double[] Distances(World w)
    {
        var bd = Board.The;
        var dist = Enumerable.Repeat(double.PositiveInfinity, bd.Nodes.Count).ToArray();
        var q = new PriorityQueue<int, double>();
        foreach (var e in bd.Edges)
            if (!w.EdgeDone(e.Id))
                foreach (int n in new[] { e.A, e.B })
                    if (dist[n] > 0) { dist[n] = 0; q.Enqueue(n, 0); }
        while (q.TryDequeue(out int n, out double d))
        {
            if (d > dist[n]) continue;
            foreach (int id in bd.Nodes[n].Out)
            {
                if (id < 0) continue;
                var e = bd.Edges[id];
                int m = e.A == n ? e.B : e.A;
                double nd = d + e.Length;
                if (nd < dist[m]) { dist[m] = nd; q.Enqueue(m, nd); }
            }
        }
        return dist;
    }

    /// <summary>
    /// On toward more to paint: an unpainted line from here, or the meeting nearest one; but not along a line with a
    /// warrior on it or near its far end (unless they're in the air, or can be caught).
    /// </summary>
    private static Dir Greedy(World w, double[] far)
    {
        var p = w.Player;
        if (p.E >= 0) return p.Way;
        var bd = Board.The;
        var n = bd.Nodes[p.N];
        Dir pick = Dir.None;
        double best = double.PositiveInfinity;
        bool wary = w.Jumping <= 0 && w.Power <= 0;
        for (int d = 1; d <= 4; d++)
        {
            int id = n.Out[d];
            if (id < 0) continue;
            var e = bd.Edges[id];
            var m = bd.Nodes[e.A == n.Id ? e.B : e.A];
            double v = w.EdgeDone(id) ? e.Length + far[m.Id] : -w.Paint[id].Count(x => !x);
            if (wary)
                foreach (var en in w.Enemies)
                    if (!en.Caught && en.Wait <= 0 && (en.M.E == id || Math.Abs(en.M.X - m.X) + Math.Abs(en.M.Y - m.Y) < 90)) v += 5000;
            if (v < best) { best = v; pick = (Dir)d; }
        }
        return pick;
    }

    /// <summary>A way tried on a copy: that way to the next meeting, then each way from there, then on as <see cref="Greedy"/> goes; the best of them.</summary>
    private static double Value(World w, Dir first, double[] far, bool jump)
    {
        var c = w.Copy();
        int score = c.Score;
        int t = 0;
        for (; t < Ahead; t++)
        {
            Sim.Step(c, new Controls(first, jump && t == 0));
            if (c.Dead) return -100000 + t * 100;
            if (c.Cleared) return 1e6 - t;
            if (c.Player.E < 0 && t > 0) break;
        }
        if (t >= Ahead) return End(c, score, far, jump);
        var n = Board.The.Nodes[c.Player.N];
        double best = double.NegativeInfinity;
        for (int d = 1; d <= 4; d++)
        {
            if (n.Out[d] < 0) continue;
            var k = c.Copy();
            double v = double.NaN;
            bool leg = true;
            for (int u = t; u < Ahead; u++)
            {
                Sim.Step(k, new Controls(leg ? (Dir)d : Greedy(k, far)));
                if (leg && k.Player.E < 0 && u > t) leg = false;
                if (k.Dead) { v = -100000 + u * 100; break; }
                if (k.Cleared) { v = 1e6 - u; break; }
            }
            if (double.IsNaN(v)) v = End(k, score, far, jump);
            best = Math.Max(best, v);
        }
        return best;
    }

    private static double End(World c, int score, double[] far, bool jump)
    {
        var p = c.Player;
        double left;
        if (p.E < 0) left = far[p.N];
        else
        {
            var e = Board.The.Edges[p.E];
            Node a = Board.The.Nodes[e.A], b = Board.The.Nodes[e.B];
            double da = Math.Abs(p.X - a.X) + Math.Abs(p.Y - a.Y), db = Math.Abs(p.X - b.X) + Math.Abs(p.Y - b.Y);
            left = c.EdgeDone(e.Id) ? Math.Min(da + far[a.Id], db + far[b.Id]) : 0;
        }
        return c.Score - score - left / 8 - (jump ? 30 : 0);
    }
}

namespace AVAAmidar.Engine;

/// <summary>The screen: the score along the top, the board below it, the jumps and lives along the bottom.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 112, Right = 912, Top = 128, Bottom = 698;
    /// <summary>How long a painted piece of line is.</summary>
    public const double Cell = 8;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The four ways along the lines.</summary>
public enum Dir { None, Left, Right, Up, Down }

/// <summary>The controls: a way to go, and the jump.</summary>
public readonly record struct Controls(Dir Way = Dir.None, bool Jump = false);

/// <summary>A meeting of lines.</summary>
public sealed class Node
{
    public int Id, Row, Col;
    public double X, Y;
    /// <summary>The edge out each way (by <see cref="Dir"/>), or -1.</summary>
    public readonly int[] Out = [-1, -1, -1, -1, -1];
}

/// <summary>A stretch of line between two meetings, painted a piece at a time.</summary>
public sealed class Edge
{
    public int Id, A, B;
    public bool Horizontal;
    public double Length;
    public int Cells;
    /// <summary>The boxes this edge is a side of.</summary>
    public readonly List<int> Boxes = [];
}

/// <summary>A box between the lines: filled in when every edge round it is painted.</summary>
public sealed class Box
{
    public int Id, Band, C1, C2;
    public double X1, Y1, X2, Y2;
    public readonly List<int> Edges = [];
    public bool Corner;
}

/// <summary>
/// The board, the same every level: seven lines across, and between each pair of them a row of uprights, set
/// differently in each row, so the boxes they make are all sizes.
/// </summary>
public sealed class Board
{
    public const int Rows = 6, Cols = 12;
    /// <summary>Where the uprights stand in each band of the board (as well as at either end).</summary>
    public static readonly int[][] Uprights =
    [
        [2, 4, 7, 9],
        [1, 3, 6, 8, 10],
        [2, 5, 7, 10],
        [1, 4, 6, 9, 11],
        [3, 5, 8, 10],
        [2, 4, 7, 9],
    ];

    public static readonly Board The = new();

    public readonly List<Node> Nodes = [];
    public readonly List<Edge> Edges = [];
    public readonly List<Box> Boxes = [];
    private readonly Node?[,] _at = new Node?[Rows + 1, Cols + 1];
    public readonly double[] Xs = new double[Cols + 1], Ys = new double[Rows + 1];

    public int TotalCells { get; }

    public static bool Upright(int band, int col) => band is >= 0 and < Rows && (col == 0 || col == Cols || Uprights[band].Contains(col));

    private Board()
    {
        for (int c = 0; c <= Cols; c++) Xs[c] = Field.Left + (Field.Right - Field.Left) * c / Cols;
        for (int r = 0; r <= Rows; r++) Ys[r] = Field.Top + (Field.Bottom - Field.Top) * r / Rows;
        for (int r = 0; r <= Rows; r++)
        for (int c = 0; c <= Cols; c++)
            if (Upright(r - 1, c) || Upright(r, c))
            {
                var n = new Node { Id = Nodes.Count, Row = r, Col = c, X = Xs[c], Y = Ys[r] };
                Nodes.Add(n);
                _at[r, c] = n;
            }
        Edge Join(Node a, Node b, bool horizontal)
        {
            double len = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            var e = new Edge { Id = Edges.Count, A = a.Id, B = b.Id, Horizontal = horizontal, Length = len, Cells = (int)Math.Ceiling(len / Field.Cell) };
            Edges.Add(e);
            return e;
        }
        // Along each line, meeting to meeting.
        for (int r = 0; r <= Rows; r++)
        {
            Node? prev = null;
            for (int c = 0; c <= Cols; c++)
            {
                if (_at[r, c] is not { } n) continue;
                if (prev != null)
                {
                    var e = Join(prev, n, true);
                    prev.Out[(int)Dir.Right] = e.Id;
                    n.Out[(int)Dir.Left] = e.Id;
                }
                prev = n;
            }
        }
        // The uprights.
        for (int b = 0; b < Rows; b++)
        for (int c = 0; c <= Cols; c++)
            if (Upright(b, c))
            {
                Node a = _at[b, c]!, d = _at[b + 1, c]!;
                var e = Join(a, d, false);
                a.Out[(int)Dir.Down] = e.Id;
                d.Out[(int)Dir.Up] = e.Id;
            }
        // The boxes: between each upright and the next in a band.
        for (int b = 0; b < Rows; b++)
        {
            var cols = Enumerable.Range(0, Cols + 1).Where(c => Upright(b, c)).ToList();
            for (int k = 0; k + 1 < cols.Count; k++)
            {
                int c1 = cols[k], c2 = cols[k + 1];
                var box = new Box { Id = Boxes.Count, Band = b, C1 = c1, C2 = c2, X1 = Xs[c1], Y1 = Ys[b], X2 = Xs[c2], Y2 = Ys[b + 1] };
                box.Corner = (b == 0 || b == Rows - 1) && (c1 == 0 || c2 == Cols);
                foreach (var e in Edges)
                {
                    Node ea = Nodes[e.A], eb = Nodes[e.B];
                    bool side = e.Horizontal
                        ? (ea.Row == b || ea.Row == b + 1) && ea.Col >= c1 && eb.Col <= c2
                        : ea.Row == b && (ea.Col == c1 || ea.Col == c2);
                    if (!side) continue;
                    box.Edges.Add(e.Id);
                    e.Boxes.Add(box.Id);
                }
                Boxes.Add(box);
            }
        }
        TotalCells = Edges.Sum(e => e.Cells);
    }

    public Node? At(int row, int col) => row is >= 0 and <= Rows && col is >= 0 and <= Cols ? _at[row, col] : null;

    public static Dir Opposite(Dir d) => d switch { Dir.Left => Dir.Right, Dir.Right => Dir.Left, Dir.Up => Dir.Down, Dir.Down => Dir.Up, _ => Dir.None };

    public static (double X, double Y) Step(Dir d) => d switch { Dir.Left => (-1, 0), Dir.Right => (1, 0), Dir.Up => (0, -1), Dir.Down => (0, 1), _ => (0, 0) };
}

/// <summary>Someone on the lines: where, which way, and how fast.</summary>
public sealed class Mover
{
    public double X, Y;
    /// <summary>The way it's going (None: standing at a meeting), and the way it last faced.</summary>
    public Dir Way, Facing = Dir.Right;
    /// <summary>The edge it's on, or -1 at a meeting (then <see cref="N"/> is the meeting).</summary>
    public int E = -1, N;
    public Mover Copy() => (Mover)MemberwiseClone();

    public void Place(Node n)
    {
        X = n.X;
        Y = n.Y;
        E = -1;
        N = n.Id;
        Way = Dir.None;
    }
}

/// <summary>
/// One of the warriors. Most go the Amidar way: along a line to the next meeting, then up or down to the next, then
/// along again, so they cross the board in long staircases, turning back at its edges. The tracer goes round the
/// outside.
/// </summary>
public sealed class Enemy
{
    public Mover M = new();
    public bool Tracer;
    public int H = 1, V = 1;
    /// <summary>How the last stretch went: true along a line, false up or down one.</summary>
    public bool Across;
    /// <summary>Seconds until it comes onto the board; down (and gone) after being caught while the corners are lit.</summary>
    public double Wait;
    public bool Caught;
    public Enemy Copy() => new() { M = M.Copy(), Tracer = Tracer, H = H, V = V, Across = Across, Wait = Wait, Caught = Caught };
}

/// <summary>
/// Everything on the board that moves or changes, kept together so it can be copied whole: the painted lines and
/// filled boxes, the player, the warriors, the jumps, and the score.
/// </summary>
public sealed class World
{
    public int Level = 1;
    public Mover Player = new();
    public bool Dead;
    public List<Enemy> Enemies = [];
    /// <summary>Which pieces of each edge are painted.</summary>
    public bool[][] Paint = Board.The.Edges.Select(e => new bool[e.Cells]).ToArray();
    public bool[] Filled = new bool[Board.The.Boxes.Count];
    public int PaintedCells, FilledBoxes;
    public int Jumps;
    /// <summary>The warriors in the air (seconds left) and the corners' power (seconds left).</summary>
    public double Jumping, Power;
    public bool CornersDone;
    public int Caught;
    public int Score;
    public double Clock;
    public Dice Dice = new(1);
    public string Cause = "";
    public List<Sfx> Heard = [];
    /// <summary>Boxes just filled, and when, for the flash.</summary>
    public List<(int Box, double T)> Flashes = [];

    public World Copy() => new()
    {
        Level = Level, Player = Player.Copy(), Dead = Dead, Enemies = Enemies.Select(e => e.Copy()).ToList(),
        Paint = Paint.Select(p => (bool[])p.Clone()).ToArray(), Filled = (bool[])Filled.Clone(), PaintedCells = PaintedCells,
        FilledBoxes = FilledBoxes, Jumps = Jumps, Jumping = Jumping, Power = Power, CornersDone = CornersDone, Caught = Caught,
        Score = Score, Clock = Clock, Dice = Dice, Cause = Cause,
    };

    public bool EdgeDone(int e) => Paint[e].All(p => p);
    public bool Cleared => FilledBoxes == Board.The.Boxes.Count;
}

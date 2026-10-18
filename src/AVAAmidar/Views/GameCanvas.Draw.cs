using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAAmidar.Engine;

namespace AVAAmidar.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAAmidar/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawBoard(dc, g.World, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBoard(dc, g.World, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawBoard(dc, g.World, true); DrawTop(dc); DrawGameOver(dc); break;
            default:
                DrawBoard(dc, g.World, true);
                DrawTop(dc);
                if (g.Message is { } msg)
                {
                    dc.FillRectangle(Brush(0xC0000000), new Rect(0, 372, Field.Width, 64));
                    Text(dc, msg, new Point(512, 404), 46, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 92), 22, Yellow, center: true);
    }

    /// <summary>Odd levels are the jungle (the gorilla and the warriors), even levels the paint shop (the roller and the pigs).</summary>
    private static bool Jungle(int level) => level % 2 == 1;

    private static readonly uint[] Paints = [0xFFE84A5F, 0xFF2EC4B6, 0xFFFFC145, 0xFF6A4CFF, 0xFF4CD964, 0xFFFF7A2E, 0xFF3AA0FF, 0xFFE05AD0];

    private void DrawBoard(DrawingContext dc, World w, bool movers)
    {
        var bd = Board.The;
        bool jungle = Jungle(w.Level);
        uint ground = jungle ? 0xFF0C1A10 : 0xFF14141C, line = jungle ? 0xFFE8C860 : 0xFFF0F0F0, dot = jungle ? 0xFF8A7A50 : 0xFF6A6A80;
        dc.FillRectangle(Brush(ground), new Rect(0, 0, Field.Width, Field.Height));
        if (jungle)
            for (int i = 0; i < 40; i++)
            {
                // Leaves in the dark round the edge of the board.
                double x = Hash(i, 1, 5) % 1024, y = Hash(i, 2, 5) % 768;
                if (x > Field.Left - 30 && x < Field.Right + 30 && y > Field.Top - 30 && y < Field.Bottom + 30) continue;
                Poly(dc, 0xFF16341C, null, 0, new Point(x, y), new Point(x + 14, y - 10), new Point(x + 30, y), new Point(x + 14, y + 8));
            }
        // The filled boxes.
        foreach (var b in bd.Boxes)
        {
            if (!w.Filled[b.Id])
            {
                if (b.Corner && !w.CornersDone)
                {
                    // The corners: a hint of what filling all four does.
                    double pulse = 0.25 + 0.15 * Math.Sin(_clock * 3 + b.Id);
                    dc.FillRectangle(Brush(Alpha(jungle ? 0xFFE8C860 : 0xFF6AE8FF, pulse * 0.4)), new Rect(b.X1 + 6, b.Y1 + 6, b.X2 - b.X1 - 12, b.Y2 - b.Y1 - 12), 6);
                }
                continue;
            }
            var r = new Rect(b.X1 + 3, b.Y1 + 3, b.X2 - b.X1 - 6, b.Y2 - b.Y1 - 6);
            if (jungle)
            {
                dc.FillRectangle(Brush(0xFF2A6A30), r);
                for (int k = 0; k < (int)(r.Width * r.Height / 900); k++)
                {
                    double x = r.X + 6 + Hash(b.Id, k, 3) % (uint)Math.Max(1, r.Width - 12), y = r.Y + 6 + Hash(b.Id, k, 4) % (uint)Math.Max(1, r.Height - 12);
                    Poly(dc, 0xFF3E8A3A, null, 0, new Point(x - 7, y), new Point(x, y - 5), new Point(x + 7, y), new Point(x, y + 4));
                }
            }
            else
            {
                uint c = Paints[b.Id % Paints.Length];
                dc.FillRectangle(Brush(c), r);
                // Roller stripes.
                for (double x = r.X + 10; x < r.Right; x += 18) dc.DrawLine(Pen(Shade(c, 0.85), 5), new Point(x, r.Y + 2), new Point(x, r.Bottom - 2));
            }
            foreach (var (box, t) in w.Flashes)
                if (box == b.Id) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 1 - t / 0.6)), r);
        }
        // The lines: dotted where unpainted, solid where painted.
        foreach (var e in bd.Edges)
        {
            Node a = bd.Nodes[e.A];
            var paint = w.Paint[e.Id];
            double ux = e.Horizontal ? 1 : 0, uy = e.Horizontal ? 0 : 1;
            for (int k = 0; k < e.Cells; k++)
            {
                double s0 = k * Field.Cell, s1 = Math.Min(e.Length, s0 + Field.Cell);
                var p0 = new Point(a.X + ux * s0, a.Y + uy * s0);
                var p1 = new Point(a.X + ux * s1, a.Y + uy * s1);
                if (paint[k]) dc.DrawLine(Pen(line, 6), p0, p1);
                else dc.FillEllipse((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2, 1.8, dot);
            }
        }
        foreach (var n in bd.Nodes) dc.FillEllipse(n.X, n.Y, 3, dot);
        if (!movers) return;
        foreach (var e in w.Enemies)
            if (!e.Caught && e.Wait <= 0) DrawEnemy(dc, w, e);
        if (!(Engine.Phase == Phase.Dying && (int)(Engine.PhaseTime * 8) % 2 == 0)) DrawPlayer(dc, w);
    }

    private static uint Shade(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private void DrawPlayer(DrawingContext dc, World w)
    {
        var p = w.Player;
        double x = p.X, y = p.Y;
        bool moving = p.Way != Dir.None;
        double bob = moving ? Math.Sin(_clock * 18) * 1.5 : 0;
        int face = p.Facing == Dir.Left ? -1 : 1;
        if (Jungle(w.Level))
        {
            // The gorilla.
            uint fur = 0xFF4A3426, skin = 0xFF9A7A5A;
            dc.DrawEllipse(Brush(fur), null, new Point(x, y + bob), 14, 12);
            dc.FillEllipse(x + face * 4, y - 12 + bob, 9, fur);
            dc.DrawEllipse(Brush(skin), null, new Point(x + face * 7, y - 10 + bob), 5, 4);
            dc.FillEllipse(x + face * 5, y - 15 + bob, 1.6, 0xFFFFFFFF);
            dc.FillEllipse(x + face * 9, y - 15 + bob, 1.6, 0xFFFFFFFF);
            double swing = moving ? Math.Sin(_clock * 18) * 6 : 0;
            dc.DrawLine(Pen(fur, 6), new Point(x - 10, y - 4 + bob), new Point(x - 16, y + 10 + swing));
            dc.DrawLine(Pen(fur, 6), new Point(x + 10, y - 4 + bob), new Point(x + 16, y + 10 - swing));
            dc.DrawEllipse(Brush(skin), null, new Point(x, y + 2 + bob), 7, 6);
        }
        else
        {
            // The paint roller.
            uint paint = Paints[(int)(w.Clock / 4) % Paints.Length];
            bool across = p.Facing is Dir.Left or Dir.Right;
            var r = across ? new Rect(x - 6, y - 14, 12, 28) : new Rect(x - 14, y - 6, 28, 12);
            dc.FillRectangle(Brush(paint), r, 4);
            dc.DrawRectangle(null, Pen(0xFFFFFFFF, 2), r, 4, 4);
            var (hx, hy) = Board.Step(p.Facing);
            dc.DrawLine(Pen(0xFFB0B0C0, 3), new Point(x, y), new Point(x - hx * 16, y - hy * 16));
            dc.DrawLine(Pen(0xFFC04030, 5), new Point(x - hx * 16, y - hy * 16), new Point(x - hx * 26, y - hy * 26));
        }
    }

    private void DrawEnemy(DrawingContext dc, World w, Enemy e)
    {
        double x = e.M.X, y = e.M.Y;
        double lift = w.Jumping > 0 ? Math.Sin(Math.PI * (1 - w.Jumping / Sim.JumpSeconds)) * 22 : 0;
        if (lift > 0) dc.DrawEllipse(Brush(0x60000000), null, new Point(x, y + 4), 11, 4);
        y -= lift;
        bool scared = w.Power > 0 && !(w.Power < 1.5 && (int)(_clock * 8) % 2 == 0);
        double step = Math.Sin(_clock * 14 + x * 0.1) * 3;
        int face = e.M.Facing == Dir.Left ? -1 : 1;
        if (Jungle(w.Level))
        {
            // A warrior: a painted shield and a spear; the tracer in gold.
            uint body = scared ? 0xFF3A6AFF : e.Tracer ? 0xFFE0B030 : 0xFFC04A2A;
            dc.DrawLine(Pen(0xFF2A1A10, 3), new Point(x - 3, y + 2), new Point(x - 5 + step, y + 13));
            dc.DrawLine(Pen(0xFF2A1A10, 3), new Point(x + 3, y + 2), new Point(x + 5 - step, y + 13));
            dc.DrawEllipse(Brush(body), null, new Point(x, y - 4), 7, 9);
            dc.FillEllipse(x, y - 16, 5.5, scared ? 0xFF9AB0FF : 0xFF8A5A3A);
            for (int k = 0; k < 3; k++) dc.DrawLine(Pen(scared ? 0xFFFFFFFF : 0xFFF0E0A0, 2), new Point(x - 4 + k * 4, y - 22), new Point(x - 6 + k * 6, y - 28));
            dc.DrawLine(Pen(0xFF8A6A40, 2), new Point(x + face * 10, y + 10), new Point(x + face * 10, y - 26));
            Poly(dc, 0xFFD0D0D8, null, 0, new Point(x + face * 10, y - 32), new Point(x + face * 8, y - 25), new Point(x + face * 12, y - 25));
            dc.DrawEllipse(Brush(scared ? 0xFF6A8AFF : 0xFFF0E0A0), Pen(0xFF2A1A10, 1.5), new Point(x - face * 8, y - 3), 5, 8);
        }
        else
        {
            // A pig; the tracer a black one.
            uint body = scared ? 0xFF3A6AFF : e.Tracer ? 0xFF3A3A44 : 0xFFF0A0B0;
            dc.DrawLine(Pen(Shade(body, 0.7), 3), new Point(x - 6, y + 4), new Point(x - 7 + step, y + 12));
            dc.DrawLine(Pen(Shade(body, 0.7), 3), new Point(x + 6, y + 4), new Point(x + 7 - step, y + 12));
            dc.DrawEllipse(Brush(body), null, new Point(x, y), 13, 9);
            dc.FillEllipse(x + face * 12, y - 4, 7, body);
            dc.DrawEllipse(Brush(Shade(body, 0.8)), null, new Point(x + face * 18, y - 3), 3.5, 3);
            dc.FillEllipse(x + face * 12, y - 7, 1.4, 0xFF000000);
            Poly(dc, Shade(body, 0.85), null, 0, new Point(x + face * 8, y - 9), new Point(x + face * 9, y - 15), new Point(x + face * 12, y - 10));
        }
    }

    private void DrawTop(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        Text(dc, $"SCORE {g.Score}", new Point(24, 40), 34, White);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, g.Score)}", new Point(512, 40), 30, Dim, center: true);
        Text(dc, $"LEVEL {w.Level}", new Point(1000, 40), 34, Yellow, right: true);
        Text(dc, $"BOXES {w.FilledBoxes}/{Board.The.Boxes.Count}", new Point(1000, 76), 24, Cyan, right: true);
        if (w.Power > 0 && (int)(_clock * 4) % 2 == 0) Text(dc, "CATCH THEM!", new Point(512, 80), 30, Red, center: true);
        // Jumps left, and lives.
        Text(dc, "JUMPS", new Point(24, 735), 24, 0xFFE0D0A0);
        for (int i = 0; i < w.Jumps; i++)
        {
            double x = 104 + i * 26;
            Poly(dc, Cyan, null, 0, new Point(x - 8, 744), new Point(x, 726), new Point(x + 8, 744));
        }
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
        {
            double x = 1000 - i * 34, y = 735;
            if (Jungle(w.Level))
            {
                dc.DrawEllipse(Brush(0xFF4A3426), null, new Point(x, y + 3), 11, 9);
                dc.FillEllipse(x + 3, y - 7, 7, 0xFF4A3426);
                dc.DrawEllipse(Brush(0xFF9A7A5A), null, new Point(x + 5, y - 6), 4, 3);
            }
            else
            {
                dc.FillRectangle(Brush(Paints[i]), new Rect(x - 12, y - 6, 24, 12), 4);
                dc.DrawLine(Pen(0xFFB0B0C0, 3), new Point(x, y + 6), new Point(x, y + 16));
            }
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // A board half painted behind the title.
        var demo = new World();
        Sim.StartLevel(demo, 1);
        var bd = Board.The;
        for (int i = 0; i < bd.Edges.Count; i++)
            if (Hash(i, 0, 11) % 3 != 0) Array.Fill(demo.Paint[i], true);
        foreach (var b in bd.Boxes)
            if (b.Edges.All(demo.EdgeDone)) demo.Filled[b.Id] = true;
        DrawBoard(dc, demo, false);
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 420, 120, 840, 330), 12);
        string title = "AVA AMIDAR";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64, ty = 180 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 116, i < 3 ? 0xFFE8C860 : 0xFF4CD964, center: true);
        }
        Text(dc, "AFTER KONAMI'S 1981 ARCADE GAME", new Point(cx, 250), 26, White, center: true);
        Text(dc, "PAINT EVERY LINE OF THE BOARD. PAINT ROUND A BOX AND IT FILLS IN; FILL THEM", new Point(cx, 296), 23, White, center: true);
        Text(dc, "ALL TO CLEAR THE LEVEL. THE WARRIORS GO THEIR FIXED WAYS: JUMP AND THEY LEAP", new Point(cx, 324), 23, White, center: true);
        Text(dc, "OVER YOU. FILL THE FOUR CORNERS, AND FOR A WHILE YOU CAN CATCH THEM.", new Point(cx, 352), 23, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 410), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "ARROWS: MOVE (PRESS A TURN EARLY AND HOLD IT)   SPACE, Z OR X: JUMP   P: PAUSE", new Point(cx, 718), 19, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: IN THE HALL OF THE MOUNTAIN KING (GRIEG, 1875) AND A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 380, 250, 760, 240), 12);
        Text(dc, "GAME OVER", new Point(cx, 320), 74, (int)(_clock * 4) % 2 == 0 ? Yellow : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 396), 44, White, center: true);
        Text(dc, $"LEVEL {g.Level}, {g.World.FilledBoxes} OF {Board.The.Boxes.Count} BOXES FILLED", new Point(cx, 448), 28, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BEST PAINTERS", new Point(cx, 120), 54, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  LEVEL", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}

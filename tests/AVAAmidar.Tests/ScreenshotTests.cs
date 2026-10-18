using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAAmidar.Engine;
using AVAAmidar.Views;
using Controls = AVAAmidar.Engine.Controls;

namespace AVAAmidar.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set AMIDAR_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaamidar-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("AMIDAR_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAAmidar-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 39128, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_jungle() => Moment(1, "jungle", g => g.World.FilledBoxes >= 10 && g.World.Enemies.Count(e => e.Wait <= 0) >= 4);

    [AvaloniaFact]
    public void A_jump() => Moment(1, "jump", g => g.World.Jumping is > 0.4 and < 0.7);

    [AvaloniaFact]
    public void The_corners() => Moment(1, "corners", g => g.World.Power is > 3 and < 5.5);

    [AvaloniaFact]
    public void The_paint_shop() => Moment(2, "paint-shop", g => g.World.FilledBoxes >= 18 && g.World.Flashes.Count > 0);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(1);
        g.SetLives(1);
        g.World.Score = 2140;
        // Run round and round the outside: the tracer gets you.
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++)
        {
            var p = g.World.Player;
            g.Tick(new Controls(p.E < 0 ? Board.The.Nodes[p.N] switch { { Row: 0, Col: < Board.Cols } => Dir.Right, { Col: Board.Cols, Row: < Board.Rows } => Dir.Down, { Row: Board.Rows, Col: > 0 } => Dir.Left, _ => Dir.Up } : p.Way));
            if (i % 3 == 0) c.Advance(0.05);
        }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}

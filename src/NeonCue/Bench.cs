using System.Diagnostics;
using System.Drawing.Imaging;
using NeonCue.Core;
using NeonCue.Rendering;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>
/// Frame benchmark: renders a real full-power break at 2560x1440 the way the game does, and reports
/// frame cost (update + draw) and motion smoothness: how unevenly a moving ball advances between
/// frames on a 279 Hz display. Run with: NeonCue.exe --bench report.txt
/// </summary>
static class Bench
{
    sealed class Host : IHost
    {
        public AppSettings Settings { get; } = new()
        {
            Sound = false, Mode = GameMode.EightBall, Opponent = OpponentType.TwoPlayer, Guide = AimGuide.Full, Style = TableStyle.Classic,
        };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }

    public static void Run(string reportPath)
    {
        Look.Style = TableStyle.Classic;
        var host = new Host();
        var gs = new GameScreen(host);
        var size = new Size(2560, 1440);
        using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        for (int i = 0; i < 5; i++) gs.Draw(g, size); // warm caches

        const double dt = 1 / 279.0;
        GameScreen.Timing.Reset();
        BallRenderer.RenderMs = 0;
        BallRenderer.Renders = 0;
        gs.DebugBreak();
        var cost = new List<double>();
        var steps = new List<double>();
        Vec2? last = null;
        var sw = new Stopwatch();
        for (int frame = 0; frame < 900 && gs.ShotInProgress; frame++)
        {
            sw.Restart();
            gs.Update(dt);
            gs.Draw(g, size);
            cost.Add(sw.Elapsed.TotalMilliseconds);

            // How far the cue ball moved on screen this frame.
            var p = gs.DrawnPos(0);
            if (last is Vec2 l && frame > 5) steps.Add(Vec2.Distance(p, l));
            last = p;
        }

        // Smoothness: frame-to-frame change in step length, relative to the step, while the ball
        // is clearly moving (0 = perfectly even motion).
        var jitter = new List<double>();
        for (int i = 1; i < steps.Count; i++)
            if (steps[i] > 0.5 && steps[i - 1] > 0.5)
                jitter.Add(Math.Abs(steps[i] - steps[i - 1]) / ((steps[i] + steps[i - 1]) / 2));

        cost.Sort();
        double P(double q) => cost.Count == 0 ? 0 : cost[(int)Math.Min(cost.Count - 1, q * cost.Count)];
        var lines = new List<string>
        {
            $"Neon Cue frame benchmark - full-power break, 2560x1440, realistic table, {cost.Count} frames at 279 Hz",
            $"frame cost (update+draw): avg {cost.DefaultIfEmpty().Average():0.00} ms, median {P(0.5):0.00}, p95 {P(0.95):0.00}, max {P(1):0.00}",
            $"achievable frame rate (median): {1000 / Math.Max(0.01, P(0.5)):0} fps",
            $"per frame: background {GameScreen.Timing.Background / Math.Max(1, GameScreen.Timing.Frames):0.00} ms, table+balls {GameScreen.Timing.World / Math.Max(1, GameScreen.Timing.Frames):0.00} ms (sprite renders {BallRenderer.RenderMs / Math.Max(1, GameScreen.Timing.Frames):0.00} ms, {BallRenderer.Renders / (double)Math.Max(1, GameScreen.Timing.Frames):0.0}/frame), hud {GameScreen.Timing.Hud / Math.Max(1, GameScreen.Timing.Frames):0.00} ms",
            $"motion unevenness (cue ball step-to-step): mean {100 * jitter.DefaultIfEmpty().Average():0.0}%, p95 {100 * Pct(jitter, 0.95):0.0}%",
        };
        File.WriteAllLines(reportPath, lines);
        foreach (var l2 in lines) Console.WriteLine(l2);
    }

    static double Pct(List<double> v, double q)
    {
        if (v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        return s[(int)Math.Min(s.Count - 1, q * s.Count)];
    }
}

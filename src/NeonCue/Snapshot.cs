using System.Drawing.Imaging;
using NeonCue.Core;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>Renders a screen offscreen to a PNG (used for README screenshots and visual checks).</summary>
static class Snapshot
{
    sealed class HeadlessHost : IHost
    {
        public AppSettings Settings { get; } = new() { Sound = false, Mode = GameMode.EightBall, Opponent = OpponentType.Cpu, Level = AILevel.Master, Guide = AimGuide.Full };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }

    public static void Render(string path, string what)
    {
        var host = new HeadlessHost();
        if (Enum.TryParse<Rendering.TableStyle>(Environment.GetEnvironmentVariable("NEONCUE_STYLE"), out var st)) Rendering.Look.Style = st;
        using var bmp = new Bitmap(1280, 800, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IScreen screen;
        if (what == "game")
        {
            var gs = new GameScreen(host);
            gs.SetupDemo();
            screen = gs;
        }
        else if (what.StartsWith("watch"))
        {
            // Drives a full CPU-vs-CPU match through the real game screen (smoke test of the turn flow).
            host.Settings.Opponent = OpponentType.CpuVsCpu;
            host.Settings.Mode = what switch { "watch9" => GameMode.NineBall, "watchsnooker" => GameMode.Snooker, _ => GameMode.EightBall };
            host.Settings.RaceTo = 2;
            screen = new GameScreen(host);
            // Snooker: stop mid-frame so the screenshot shows a live position.
            int frames = what == "watchsnooker" ? 60 * 70 : 60 * 240;
            for (int frame = 0; frame < frames; frame++)
            {
                screen.Update(1 / 60.0);
                if (frame % 30 == 0) { screen.Draw(g, bmp.Size); Thread.Sleep(2); }
            }
        }
        else if (what == "shop")
        {
            host.Settings.Coins = 14250;
            host.Settings.OwnedCues = ["house", "starter", "viper", "phantom"];
            host.Settings.EquippedCue = "phantom";
            screen = new ShopScreen(host);
            screen.KeyDown(Keys.Down);
        }
        else if (what == "snooker")
        {
            host.Settings.Mode = GameMode.Snooker;
            screen = new GameScreen(host);
        }
        else screen = new MenuScreen(host);

        for (int i = 0; i < 20; i++)
        {
            screen.Draw(g, bmp.Size);
            screen.Update(1 / 60.0);
        }
        screen.Draw(g, bmp.Size);
        bmp.Save(path, ImageFormat.Png);
    }
}

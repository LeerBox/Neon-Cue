using System.Drawing.Drawing2D;
using NeonCue.Core;
using NeonCue.Rendering;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>Draws every cue, including a signed dress, at many angles. Run: NeonCue.exe --gdi-check</summary>
static class GdiCheck
{
    public static bool Run()
    {
        var failures = new List<string>();
        var dress = new CueDress
        {
            Forearm = "crest", WrapMark = "wave", Sleeve = "star",
            Trim = "gold", Finish = "glow", Charm = "charmstar", Signature = "LEER", WrapKind = "linen",
        };
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        Theme.Setup(g);
        foreach (var cue in CueCatalog.All)
        {
            foreach (var deg in new[] { -8f, 0f, 1f, 15f, 45f, 89f, 90f, 91f, 135f, 180f, 225f, 270f, 315f, 359f })
            {
                try
                {
                    g.ResetTransform();
                    g.Clear(Color.Black);
                    g.TranslateTransform(80, 60);
                    g.ScaleTransform(0.94f, 0.94f);
                    float rad = deg * MathF.PI / 180f;
                    float len = 420;
                    var tip = new PointF(640 + MathF.Cos(rad) * len / 2, 400 + MathF.Sin(rad) * len / 2);
                    var butt = new PointF(640 - MathF.Cos(rad) * len / 2, 400 - MathF.Sin(rad) * len / 2);
                    CueArt.Draw(g, tip, butt, 4, 11, cue, 1.2, 1, dress: dress);
                    CueArt.Draw(g, tip, butt, 2.4f, 6.4f, cue, 0.4, 1, dress: null);
                }
                catch (Exception ex)
                {
                    failures.Add($"{cue.Id} {deg:0}° {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        try
        {
            var host = new Host();
            host.Settings.OwnedCues = CueCatalog.All.Select(c => c.Id).ToList();
            host.Settings.Looks["silk"] = dress;
            var shop = new ShopScreen(host);
            shop.Draw(g, bmp.Size);
            var atelier = new DressScreen(host, "silk");
            atelier.Draw(g, bmp.Size);
            atelier.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, 200, 300, 0));
            atelier.MouseMove(new MouseEventArgs(MouseButtons.Left, 0, 260, 300, 0));
            atelier.Draw(g, new Size(640, 400));
            shop.Draw(g, new Size(640, 400));
        }
        catch (Exception ex)
        {
            failures.Add($"screens {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        Probe(failures);
        if (failures.Count == 0)
        {
            Console.WriteLine("gdi-check: PASS");
            return true;
        }
        foreach (var f in failures.Take(12)) Console.WriteLine("FAIL: " + f);
        Console.WriteLine($"gdi-check: {failures.Count} failures");
        return false;
    }

    /// <summary>
    /// The paths the angle sweep does not cover: the saved look, a live frame, a resize that
    /// changes the ball sprite by one pixel, and the GDI+ calls that reject a bad rectangle.
    /// </summary>
    static void Probe(List<string> failures)
    {
        var settings = AppSettings.Load();
        Look.Style = settings.Style;
        var saved = settings.DressFor(settings.EquippedCue);
        void Check(string name, Action act)
        {
            try { act(); }
            catch (Exception ex)
            {
                failures.Add($"{name} {ex.GetType().Name}: {ex.Message}\n{ex}");
            }
        }

        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        Theme.Setup(g);
        Check("dashed-zero", () =>
        {
            using var pen = new Pen(Color.White, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round };
            g.DrawLine(pen, 40, 40, 40, 40);
        });
        Check("dashed-short", () =>
        {
            using var pen = new Pen(Color.White, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round };
            g.DrawLine(pen, 40, 40, 40.2f, 40.1f);
        });
        Check("lines-duplicate", () =>
        {
            using var pen = new Pen(Color.White, 1.2f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Dash };
            g.DrawLines(pen, [new PointF(80, 80), new PointF(120, 90), new PointF(120, 90), new PointF(160, 80)]);
        });
        Check("brush-negative", () =>
        {
            var bar = new RectangleF(10, 10, -40, 4);
            using var br = new LinearGradientBrush(bar, Color.Gold, Color.White, 0f);
            g.FillRectangle(br, bar);
        });
        Check("clip-negative", () => g.SetClip(new RectangleF(0, 100, 200, -20)));
        g.ResetClip();

        Check("ball-resize", () =>
        {
            // One pixel of window movement changes the on-screen ball radius by about 0.01.
            // 10 -> 10.009 stays inside that cache tolerance, but the sprite grows by a pixel
            // (ceil(20)=20, ceil(20.018)=21). The phase change forces the upload.
            var balls = new BallRenderer();
            balls.Prepare([(0, Mat3.Identity, 0)], 10f);
            balls.Prepare([(0, Mat3.Identity, 1)], 10.009f);
            balls.Dispose();
        });

        foreach (var dpi in new[] { 96f, 120f, 144f, 168f, 192f })
        {
            Check("dpi-" + dpi.ToString("0"), () =>
            {
                using var hi = new Bitmap(900, 500);
                hi.SetResolution(dpi, dpi);
                using var hg = Graphics.FromImage(hi);
                Theme.Setup(hg);
                hg.TranslateTransform(40, 30);
                hg.ScaleTransform(dpi / 96f, dpi / 96f);
                var cue = settings.Equipped;
                var dress = saved.IsBlank ? null : saved;
                float rad = 35f * MathF.PI / 180f;
                var tip = new PointF(300, 200);
                var butt = new PointF(tip.X - MathF.Cos(rad) * 560, tip.Y - MathF.Sin(rad) * 560);
                CueArt.Draw(hg, tip, butt, 2.4f, 6.4f, cue, 1.2, 1f, dress: dress);
                var signed = saved.Clone();
                signed.Signature = "LEER";
                CueArt.Draw(hg, tip, butt, 2.4f, 6.4f, cue, 0.4, 1f, dress: signed);
                var view = new TableView();
                view.Layout(new RectangleF(10, 70, 860, 360));
                hg.ResetTransform();
                view.ApplyWorld(hg);
                CueArt.Draw(hg, tip, butt, 2.4f, 6.4f, cue, 0.8, 1f, dress: dress);
            });
        }

        Check("world-cue", () =>
        {
            var view = new TableView();
            view.Layout(new RectangleF(20, 90, 1240, 620));
            g.ResetTransform();
            Theme.Setup(g);
            view.ApplyWorld(g);
            var cue = settings.Equipped;
            var dress = saved.IsBlank ? null : saved;
            for (int deg = 0; deg < 360; deg += 15)
            {
                float rad = deg * MathF.PI / 180f;
                var tip = new PointF(500 + MathF.Cos(rad) * 40, 250 + MathF.Sin(rad) * 40);
                var butt = new PointF(tip.X - MathF.Cos(rad) * 560, tip.Y - MathF.Sin(rad) * 560);
                CueArt.Draw(g, tip, butt, 2.4f, 6.4f, cue, deg / 30.0, 1f, dress: dress);
                var signed = saved.Clone();
                signed.Signature = "LEER";
                CueArt.Draw(g, tip, butt, 2.4f, 6.4f, cue, 0.2, 1f, dress: signed);
            }
            g.ResetTransform();
        });

        Check("game-frames", () =>
        {
            var host = new Host { Settings = settings };
            var gs = new GameScreen(host);
            var sizes = new[] { new Size(1280, 800), new Size(1920, 1080), new Size(980, 620), new Size(1600, 900), new Size(2560, 1392) };
            foreach (var size in sizes)
            {
                using var frame = new Bitmap(size.Width, size.Height);
                using var fg = Graphics.FromImage(frame);
                gs.Draw(fg, size);
                gs.Update(1 / 60.0);
                if (gs.IsPlacing)
                {
                    var spot = gs.ToScreen(gs.State.Cue.Pos + new Vec2(30, 0));
                    gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, (int)spot.X, (int)spot.Y, 0));
                    gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, (int)spot.X, (int)spot.Y, 0));
                }
                for (int deg = 0; deg < 360; deg += 30)
                {
                    float rad = deg * MathF.PI / 180f;
                    var at = gs.ToScreen(gs.State.Cue.Pos + new Vec2(Math.Cos(rad) * 180, Math.Sin(rad) * 180));
                    gs.MouseMove(new MouseEventArgs(MouseButtons.None, 0, (int)at.X, (int)at.Y, 0));
                    gs.Update(1 / 30.0);
                    gs.Draw(fg, size);
                }
            }
            // Let the guide simulation finish, then draw the path it produced.
            Thread.Sleep(1500);
            gs.Update(1 / 60.0);
            using (var frame = new Bitmap(1280, 800))
            using (var fg = Graphics.FromImage(frame))
                gs.Draw(fg, frame.Size);

            var shop = new ShopScreen(host);
            shop.Draw(g, new Size(1280, 800));
            shop.Draw(g, new Size(980, 640));
            var dress = new DressScreen(host, settings.EquippedCue);
            for (int i = 0; i < 4; i++)
            {
                dress.Draw(g, new Size(1280, 800));
                dress.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, 400, 360, 0));
                dress.MouseMove(new MouseEventArgs(MouseButtons.Left, 0, 460, 300, 0));
                dress.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, 460, 300, 0));
                dress.Draw(g, new Size(980, 640));
            }
            var menu = new MenuScreen(host);
            foreach (var size in new[] { new Size(1280, 800), new Size(980, 640), new Size(2560, 1392) })
            {
                using var frame = new Bitmap(size.Width, size.Height);
                using var fg = Graphics.FromImage(frame);
                menu.Draw(fg, size);
            }
        });

        Check("game-resize", () =>
        {
            // The same window, one pixel taller, where the ball sprite grows but the radius
            // change stays inside the old cache tolerance.
            var host = new Host { Settings = settings };
            var gs = new GameScreen(host);
            Size? from = null, to = null;
            for (int h = 700; h < 1200 && from == null; h++)
            {
                var s0 = new Size(1500, h);
                var s1 = new Size(1500, h + 1);
                float r0 = PixelRadius(s0), r1 = PixelRadius(s1);
                int z0 = Math.Max(1, (int)Math.Ceiling(r0 * 2f) + 3);
                int z1 = Math.Max(1, (int)Math.Ceiling(r1 * 2f) + 3);
                if (z0 != z1 && Math.Abs(r0 - r1) <= 0.01f) { from = s0; to = s1; }
            }
            if (from is not Size a || to is not Size b)
                throw new InvalidOperationException("No one-pixel resize crosses a sprite boundary inside the cache tolerance.");
            using var frame = new Bitmap(b.Width, b.Height);
            using var fg = Graphics.FromImage(frame);
            gs.Draw(fg, a);
            gs.Update(1 / 60.0);
            var at = gs.ToScreen(gs.State.Cue.Pos + new Vec2(80, 40));
            gs.MouseMove(new MouseEventArgs(MouseButtons.None, 0, (int)at.X, (int)at.Y, 0));
            gs.Draw(fg, b);
        });
    }

    static float PixelRadius(Size s)
    {
        float u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.72f, 2.2f);
        float top = 100 * u, bottom = 88 * u;
        float aw = s.Width - 28 * u, ah = s.Height - top - bottom;
        float ww = (float)(Table.Width + 2 * TableView.Margin);
        float wh = (float)(Table.Height + 2 * TableView.Margin);
        float scale = Math.Min(aw / ww, ah / wh);
        return (float)Table.R * scale;
    }

    sealed class Host : IHost
    {
        public AppSettings Settings { get; init; } = new() { Sound = false, Coins = 50000 };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }
}

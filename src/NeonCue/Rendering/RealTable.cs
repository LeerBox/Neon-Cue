using System.Drawing.Drawing2D;
using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>
/// Paints a photographic-style table per pixel: lamp-lit cloth with fibre noise, felt-covered
/// cushions, varnished wood rails with real grain and bevels, an oak floor with the table's
/// shadow, then leather pockets and mother-of-pearl diamonds on top.
/// </summary>
static class RealTable
{
    const double W = Table.Width, H = Table.Height;
    const double CD = Table.CushionDepth, FO = Table.FrameOuter;

    public static void Render(Bitmap target, TableView view)
    {
        int w = target.Width, h = target.Height;
        var canvas = new PixelCanvas(w, h);
        Rgb felt = Look.Felt;
        WoodKind rail = Look.Rail, floor = Look.Floor;
        double inv = 1.0 / view.Scale;

        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                double x = (px + 0.5 - view.OX) * inv, y = (py + 0.5 - view.OY) * inv;
                Rgb c = Pixel(x, y, px, py, felt, rail, floor);
                canvas.Pixels[py * w + px] = c.ToPArgb();
            }
        });
        canvas.CopyTo(target);

        using var g = Graphics.FromImage(target);
        Theme.Setup(g);
        view.ApplyWorld(g);
        DrawPockets(g);
        DrawDiamonds(g);
        DrawSpots(g);
    }

    static Rgb Pixel(double x, double y, int px, int py, Rgb felt, WoodKind rail, WoodKind floor)
    {
        double frame = MathX.RoundRectSdf(x, y, W / 2, H / 2, W / 2 + FO, H / 2 + FO, 30);
        bool inFeltRect = x > -CD && x < W + CD && y > -CD && y < H + CD;
        double lamp = Lamp(x, y);

        if (inFeltRect)
        {
            if ((x < 0 || x > W || y < 0 || y > H) && Cushion(x, y, out double nose))
                return CushionColor(nose, x, y, px, py, felt) * lamp;
            return FeltColor(x, y, px, py, felt) * lamp;
        }
        if (frame < 0) return RailColor(x, y, frame, rail) * (0.35 + 0.72 * lamp);
        return FloorColor(x, y, floor);
    }

    /// <summary>Overhead lamp: bright over the bed, falling off towards the rails.</summary>
    static double Lamp(double x, double y)
    {
        double nx = (x - W / 2) / (W * 0.72), ny = (y - H / 2) / (H * 0.95);
        return 1.1 - 0.34 * (nx * nx + ny * ny);
    }

    static Rgb FeltColor(double x, double y, int px, int py, Rgb felt)
    {
        double edge = Math.Min(Math.Min(x, W - x), Math.Min(y, H - y));
        double shadow = edge > 0 ? 1 - 0.28 * Math.Exp(-edge / 6.5) : 0.7;
        double n = (Noise.Fbm(x * 0.045, y * 0.045, 3, 11) - 0.5) * 0.07
                 + (Noise.Rand(px, py, 3) - 0.5) * 0.05
                 + (Noise.Value(x * 1.6, y * 0.25, 5) - 0.5) * 0.025; // nap direction
        return felt * (shadow * (1 + n));
    }

    static bool Cushion(double x, double y, out double nose)
    {
        nose = 0;
        var p = new Vec2(x, y);
        foreach (var poly in Table.CushionPolygons)
        {
            if (!InConvex(poly, p)) continue;
            nose = Vec2.DistanceToSegment(p, poly[0], poly[1]);
            return true;
        }
        return false;
    }

    static bool InConvex(Vec2[] poly, Vec2 p)
    {
        int sign = 0;
        for (int i = 0; i < poly.Length; i++)
        {
            Vec2 a = poly[i], b = poly[(i + 1) % poly.Length];
            double cr = Vec2.Cross(b - a, p - a);
            int s = cr > 0 ? 1 : cr < 0 ? -1 : 0;
            if (s == 0) continue;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }

    static Rgb CushionColor(double nose, double x, double y, int px, int py, Rgb felt)
    {
        double t = nose / CD;
        // Rounded nose catches the light, then the cushion slopes down into the rail.
        double b = t < 0.1 ? 1.18 - t * 1.2 : 1.02 - (t - 0.1) * 0.42;
        if (t > 0.9) b *= 0.7;
        double n = (Noise.Rand(px, py, 7) - 0.5) * 0.05 + (Noise.Fbm(x * 0.05, y * 0.05, 2, 21) - 0.5) * 0.05;
        return felt * (0.9 * b * (1 + n));
    }

    static Rgb RailColor(double x, double y, double frameSdf, WoodKind wood)
    {
        // Distance out from the cloth edge, and in from the outer edge: s runs 0 (cloth) .. 1 (outside).
        double qx = Math.Max(Math.Max(-CD - x, x - (W + CD)), 0);
        double qy = Math.Max(Math.Max(-CD - y, y - (H + CD)), 0);
        double dIn = Math.Sqrt(qx * qx + qy * qy);
        double dOut = -frameSdf;
        double s = dIn / Math.Max(1e-6, dIn + dOut);

        bool longRail = qy > qx; // mitred corners
        double along = longRail ? x : y, across = longRail ? y : x;
        Rgb col = wood.Sample(along * 1.0 + (longRail ? 0 : 3000), across * 1.6, longRail ? 3 : 7);

        double shade;
        if (s < 0.05) shade = 0.45 + s * 6;                                     // dark lip where rubber meets wood
        else if (s < 0.8) shade = 0.92 + 0.22 * Math.Exp(-Math.Pow((s - 0.3) / 0.09, 2)); // varnished top with lamp reflection
        else shade = 0.92 - (s - 0.8) / 0.2 * 0.6 + 0.25 * Math.Exp(-Math.Pow((s - 0.85) / 0.025, 2)); // rounded outer edge
        double gloss = 0.08 * Math.Exp(-Math.Pow((s - 0.3) / 0.05, 2));
        return col * shade + Rgb.White * gloss;
    }

    static Rgb FloorColor(double x, double y, WoodKind wood)
    {
        const double plank = 64;
        int row = (int)Math.Floor(y / plank);
        double across = y - row * plank;
        double along = x + row * 173.0;
        Rgb col = wood.Sample(along, across + row * 11, 100 + row);
        double tone = 0.82 + 0.3 * Noise.Rand(row, (int)Math.Floor(along / 420), 4);
        if (across < 1.4 || (along / 420 - Math.Floor(along / 420)) < 0.003) tone *= 0.35; // seams

        double dx = (x - W / 2) / 900, dy = (y - H / 2) / 650;
        double light = 0.1 + 0.55 * Math.Exp(-(dx * dx + dy * dy) * 1.8);
        double sh = MathX.RoundRectSdf(x - 10, y - 16, W / 2, H / 2, W / 2 + FO, H / 2 + FO, 30);
        light *= 0.3 + 0.7 * MathX.Smooth(-4, 46, sh);
        return col * (tone * light);
    }

    static void DrawPockets(Graphics g)
    {
        foreach (var p in Table.Pockets)
        {
            var c = p.DrawCenter;
            float r = (float)p.DrawRadius;
            var rect = new RectangleF((float)c.X - r, (float)c.Y - r, 2 * r, 2 * r);
            using var hole = new GraphicsPath();
            hole.AddEllipse(rect);
            using (var hb = new PathGradientBrush(hole)
            {
                CenterPoint = new PointF((float)c.X + r * 0.15f, (float)c.Y + r * 0.2f),
                CenterColor = Color.FromArgb(255, 2, 2, 2),
                SurroundColors = [Color.FromArgb(255, 34, 28, 24)],
                FocusScales = new PointF(0.55f, 0.55f),
            })
                g.FillPath(hb, hole);

            // Leather liner rim.
            using (var rim = new Pen(Color.FromArgb(255, 30, 20, 14), 4.5f)) g.DrawEllipse(rim, rect);
            using (var hi = new Pen(Color.FromArgb(120, 150, 110, 80), 1.2f))
                g.DrawArc(hi, rect.X + 1.5f, rect.Y + 1.5f, rect.Width - 3, rect.Height - 3, 190, 80);
            using (var stitch = new Pen(Color.FromArgb(90, 200, 170, 120), 0.6f) { DashStyle = DashStyle.Dot })
                g.DrawEllipse(stitch, rect.X - 1.6f, rect.Y - 1.6f, rect.Width + 3.2f, rect.Height + 3.2f);
        }
    }

    static void DrawDiamonds(Graphics g)
    {
        float off = (float)(CD + (FO - CD) * 0.42);
        void Diamond(float x, float y, bool horizontal)
        {
            float a = horizontal ? 4.2f : 2.6f, b = horizontal ? 2.6f : 4.2f;
            PointF[] pts = [new(x, y - b), new(x + a, y), new(x, y + b), new(x - a, y)];
            using var path = new GraphicsPath();
            path.AddPolygon(pts);
            using var br = new LinearGradientBrush(new RectangleF(x - a, y - b, 2 * a, 2 * b), Color.FromArgb(255, 250, 248, 240), Color.FromArgb(255, 180, 200, 210), 45f);
            g.FillPath(br, path);
            using var pen = new Pen(Color.FromArgb(140, 40, 30, 20), 0.5f);
            g.DrawPath(pen, path);
        }
        for (int k = 1; k < 8; k++)
        {
            if (k == 4) continue;
            Diamond((float)(W * k / 8), -off, true);
            Diamond((float)(W * k / 8), (float)H + off, true);
        }
        for (int k = 1; k < 4; k++)
        {
            Diamond(-off, (float)(H * k / 4), false);
            Diamond((float)W + off, (float)(H * k / 4), false);
        }
    }

    static void DrawSpots(Graphics g)
    {
        using var spot = new SolidBrush(Color.FromArgb(150, 240, 240, 230));
        foreach (var p in new[] { Table.FootSpot, Table.HeadSpot })
            g.FillEllipse(spot, (float)p.X - 2.4f, (float)p.Y - 2.4f, 4.8f, 4.8f);
    }
}

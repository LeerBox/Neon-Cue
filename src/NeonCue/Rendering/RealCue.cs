using System.Drawing.Drawing2D;
using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>How a cue is built: woods, wrap, inlaid points and fittings.</summary>
sealed record CueBuild(
    string Shaft,          // "maple" or "carbon"
    WoodKind Forearm,
    WoodKind Sleeve,
    string Wrap,           // none | linen | leather | snake
    Rgb WrapColor,
    int Points,
    Rgb PointColor,
    Rgb Fitting,           // joint and rings
    double FittingGloss,
    Rgb Tip,
    bool Sparkle);

/// <summary>Renders cue sticks as shaded, textured cylinders cached per cue.</summary>
static class RealCue
{
    const int LengthPx = 1400, HeightPx = 32;
    const double TipHalf = 6, ButtHalf = 16;
    static readonly Dictionary<string, Bitmap> cache = new();

    static readonly Rgb Gold = Rgb.Hex("#D4AF37"), Silver = Rgb.Hex("#C9CDD4"), Pearl = Rgb.Hex("#EEE9DF");
    static readonly Rgb Ivory = Rgb.Hex("#F1EDE2"), Chalk = Rgb.Hex("#3F7FC2");

    public static CueBuild BuildFor(string id) => id switch
    {
        "house" => new("maple", WoodKind.Walnut, WoodKind.Walnut, "none", Rgb.Hex("#2A1C12"), 0, Rgb.Black, Rgb.Hex("#E8E2D2"), 0.2, Chalk, false),
        "starter" => new("maple", WoodKind.Birdseye, WoodKind.BlackLacquer, "linen", Rgb.Hex("#1A1A1C"), 0, Rgb.Black, Silver, 0.6, Chalk, false),
        "viper" => new("maple", WoodKind.Purpleheart, WoodKind.Purpleheart, "snake", Rgb.Hex("#4B2A6E"), 4, Rgb.Hex("#121012"), Silver, 0.6, Chalk, false),
        "thunder" => new("maple", WoodKind.Gray, WoodKind.BlackLacquer, "linen", Rgb.Hex("#6A6D74"), 0, Rgb.Black, Gold, 0.8, Rgb.Hex("#F2EFE6"), false),
        "phantom" => new("carbon", WoodKind.BlackLacquer, WoodKind.BlackLacquer, "leather", Rgb.Hex("#141416"), 0, Rgb.Black, Silver, 0.7, Chalk, false),
        "silk" => new("maple", WoodKind.Cocobolo, WoodKind.Rosewood, "linen", Rgb.Hex("#1F5E3A"), 4, Pearl, Pearl, 0.5, Chalk, false),
        "dragon" => new("maple", WoodKind.Ebony, WoodKind.Ebony, "leather", Rgb.Hex("#7A1414"), 6, Gold, Gold, 0.9, Chalk, false),
        "royale" => new("carbon", WoodKind.Ebony, WoodKind.Ebony, "leather", Rgb.Hex("#0E0E10"), 6, Silver, Silver, 1.0, Chalk, true),
        _ => new("maple", WoodKind.Walnut, WoodKind.Walnut, "none", Rgb.Hex("#2A1C12"), 0, Rgb.Black, Ivory, 0.2, Chalk, false),
    };

    public static Bitmap Get(CueSpec spec)
    {
        if (cache.TryGetValue(spec.Id, out var bmp)) return bmp;
        bmp = Render(BuildFor(spec.Id), spec.Id.GetHashCode());
        cache[spec.Id] = bmp;
        return bmp;
    }

    /// <summary>Draws the cue from tip to butt; widths are half-thicknesses in the current units.</summary>
    public static void Draw(Graphics g, PointF tip, PointF butt, float buttHalf, CueSpec spec, double time, float unit, bool shadow)
    {
        float dx = butt.X - tip.X, dy = butt.Y - tip.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float angle = MathF.Atan2(dy, dx) * 180 / MathF.PI;
        var bmp = Get(spec);

        var state = g.Save();
        if (shadow)
        {
            var sstate = g.Save();
            g.TranslateTransform(tip.X + 7 * unit, tip.Y + 10 * unit);
            g.RotateTransform(angle);
            using var sp = new GraphicsPath();
            sp.AddPolygon([new PointF(0, -buttHalf * 0.38f), new PointF(len, -buttHalf), new PointF(len, buttHalf), new PointF(0, buttHalf * 0.38f)]);
            using var sb = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillPath(sb, sp);
            g.Restore(sstate);
        }
        g.TranslateTransform(tip.X, tip.Y);
        g.RotateTransform(angle);
        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(bmp, new RectangleF(0, -buttHalf, len, buttHalf * 2));
        g.InterpolationMode = oldInterp;

        var b = BuildFor(spec.Id);
        if (b.Sparkle || spec.Tier >= CueTier.Legendary)
        {
            // A glint of the overhead lamp sliding along the fittings.
            float f = 0.52f + (float)((time * 0.35) % 1.0) * 0.45f;
            float r = buttHalf * 0.9f;
            using var gp = new GraphicsPath();
            gp.AddEllipse(len * f - r * 2, -buttHalf * 0.7f - r * 0.5f, r * 4, r);
            using var gb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(150, 255, 255, 255), SurroundColors = [Color.FromArgb(0, 255, 255, 255)] };
            g.FillPath(gb, gp);
        }
        g.Restore(state);
    }

    static double HalfWidth(double f)
    {
        double w = f < 0.5 ? TipHalf + (ButtHalf - TipHalf) * 0.42 * (f / 0.5) : TipHalf + (ButtHalf - TipHalf) * (0.42 + 0.58 * (f - 0.5) / 0.5);
        if (f > 0.995) w *= 1 - (f - 0.995) / 0.005 * 0.25;
        return w;
    }

    static Bitmap Render(CueBuild b, int seed)
    {
        var c = new PixelCanvas(LengthPx, HeightPx);
        double mid = HeightPx / 2.0;
        var rnd = new Random(seed);
        for (int px = 0; px < LengthPx; px++)
        {
            double f = (px + 0.5) / LengthPx;
            double hw = HalfWidth(f);
            for (int py = 0; py < HeightPx; py++)
            {
                double dy = py + 0.5 - mid;
                double cov = Math.Clamp(hw - Math.Abs(dy) + 0.5, 0, 1);
                if (cov <= 0) continue;
                double t = Math.Clamp(dy / hw, -0.999, 0.999);
                double phi = Math.Asin(t);                 // angle around the cylinder
                double nz = Math.Sqrt(1 - t * t);
                var (col, gloss) = Material(b, f, px, phi, t, seed);
                double light = 0.32 + 0.78 * Math.Max(0, nz * 0.78 - t * 0.38);
                double spec = Math.Pow(Math.Max(0, nz * 0.72 - t * 0.68), 26) * gloss;
                c.Pixels[py * LengthPx + px] = (col * light + Rgb.White * spec).ToPArgb(cov);
            }
        }
        if (b.Sparkle)
        {
            for (int i = 0; i < 40; i++)
            {
                int x = (int)(LengthPx * (0.53 + rnd.NextDouble() * 0.18)), y = (int)(mid + (rnd.NextDouble() - 0.5) * 18);
                if (y >= 0 && y < HeightPx) c.Pixels[y * LengthPx + x] = Rgb.White.ToPArgb();
            }
        }
        return c.ToBitmap();
    }

    static (Rgb col, double gloss) Material(CueBuild b, double f, int px, double phi, double t, int seed)
    {
        double around = phi * 40;
        if (f < 0.007) return (b.Tip * (0.85 + 0.15 * Noise.Value(px * 0.8, around, 1)), 0.05);
        if (f < 0.03) return (Ivory, 0.5);
        if (f < 0.5)
        {
            if (b.Shaft == "carbon")
            {
                bool weave = ((int)Math.Floor(px / 4.0) + (int)Math.Floor((phi + 2) * 9)) % 2 == 0;
                return (Rgb.Hex(weave ? "#1C1C21" : "#26262C"), 0.9);
            }
            return (WoodKind.Maple.Sample(px * 0.7, phi * 18, seed), 0.35);
        }
        if (f < 0.512) return (b.Fitting, b.FittingGloss);

        if (f < 0.72)
        {
            // Inlaid points grow from the wrap end towards the joint.
            if (b.Points > 0)
            {
                const double start = 0.72, end = 0.56;
                if (f > end)
                {
                    double width = (f - end) / (start - end) * Math.PI / b.Points * 0.85;
                    for (int k = 0; k < b.Points; k++)
                    {
                        double center = k * 2 * Math.PI / b.Points + 0.3;
                        double d = Math.Abs(Math.IEEERemainder(phi - center, 2 * Math.PI));
                        if (d < width) return (b.PointColor, b.PointColor.R > 0.5 ? 0.9 : 0.4);
                        if (d < width + 0.06) return (Rgb.Hex("#E8E2D2"), 0.4); // veneer line
                    }
                }
            }
            return (b.Forearm.Sample(px * 0.8, phi * 22, seed + 1), 0.45);
        }
        if (f < 0.73) return (b.Fitting, b.FittingGloss);
        if (f < 0.88)
        {
            double n;
            switch (b.Wrap)
            {
                case "linen":
                    n = 0.85 + 0.15 * Math.Sin(px * 1.4 + phi * 30) * Math.Sin(px * 1.4 - phi * 30);
                    return (b.WrapColor * n, 0.05);
                case "leather":
                    n = 0.9 + (Noise.Fbm(px * 0.3, around * 0.4, 3, 8) - 0.5) * 0.3;
                    return (b.WrapColor * n, 0.25);
                case "snake":
                    double cx = px / 7.0 + Math.Floor((phi + 2) * 6) * 0.5;
                    double cell = cx - Math.Floor(cx);
                    n = 0.7 + 0.5 * Math.Sin(cell * Math.PI);
                    return (b.WrapColor * n, 0.35);
                default:
                    return (b.Sleeve.Sample(px * 0.8, phi * 22, seed + 2) * 0.8, 0.3);
            }
        }
        if (f < 0.885) return (b.Fitting, b.FittingGloss);
        if (f < 0.99)
        {
            bool ring = f > 0.955 && f < 0.96;
            return ring ? (b.Fitting, b.FittingGloss) : (b.Sleeve.Sample(px * 0.8, phi * 22, seed + 3), 0.45);
        }
        return (Rgb.Hex("#121212"), 0.1); // rubber bumper
    }
}

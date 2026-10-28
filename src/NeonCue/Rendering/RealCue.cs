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
        "linen" => new("ash", WoodKind.Maple, WoodKind.Walnut, "linen", Rgb.Hex("#E4D5BC"), 0, Rgb.Black, Ivory, 0.25, Chalk, false),
        "viper" => new("maple", WoodKind.Purpleheart, WoodKind.Purpleheart, "snake", Rgb.Hex("#4B2A6E"), 4, Rgb.Hex("#121012"), Silver, 0.6, Chalk, false),
        "ivory" => new("maple", WoodKind.Cocobolo, WoodKind.Walnut, "linen", Rgb.Hex("#EFE8D8"), 4, Ivory, Ivory, 0.45, Chalk, false),
        "thunder" => new("maple", WoodKind.Gray, WoodKind.BlackLacquer, "linen", Rgb.Hex("#6A6D74"), 0, Rgb.Black, Gold, 0.8, Rgb.Hex("#F2EFE6"), false),
        "phantom" => new("carbon", WoodKind.BlackLacquer, WoodKind.BlackLacquer, "leather", Rgb.Hex("#141416"), 0, Rgb.Black, Silver, 0.7, Chalk, false),
        "helix" => new("carbon", WoodKind.BlackLacquer, WoodKind.Ebony, "leather", Rgb.Hex("#0E1214"), 0, Rgb.Black, Rgb.Hex("#7DFFF0"), 0.95, Chalk, true),
        "silk" => new("maple", WoodKind.Cocobolo, WoodKind.Rosewood, "linen", Rgb.Hex("#1F5E3A"), 4, Pearl, Pearl, 0.5, Chalk, false),
        "aurora" => new("crystal", WoodKind.Birdseye, WoodKind.Gray, "linen", Rgb.Hex("#E7F2FF"), 6, Pearl, Rgb.Hex("#D6ECFF"), 0.85, Chalk, true),
        "dragon" => new("maple", WoodKind.Ebony, WoodKind.Ebony, "leather", Rgb.Hex("#7A1414"), 6, Gold, Gold, 0.9, Chalk, false),
        "royale" => new("carbon", WoodKind.Ebony, WoodKind.Ebony, "leather", Rgb.Hex("#0E0E10"), 6, Silver, Silver, 1.0, Chalk, true),
        _ => new("maple", WoodKind.Walnut, WoodKind.Walnut, "none", Rgb.Hex("#2A1C12"), 0, Rgb.Black, Ivory, 0.2, Chalk, false),
    };

    public static Bitmap Get(CueSpec spec, CueDress? dress = null)
    {
        bool plain = dress == null || dress.IsBlank;
        string key = plain ? spec.Id : spec.Id + "|" + dress!.Key;
        if (cache.TryGetValue(key, out var bmp)) return bmp;
        var build = plain ? BuildFor(spec.Id) : Apply(BuildFor(spec.Id), dress!);
        bmp = Render(build, spec.Id.GetHashCode(), plain ? null : dress);
        cache[key] = bmp;
        return bmp;
    }

    /// <summary>Cosmetic overrides. The tip, the joint, and the cue's woods stay.</summary>
    public static CueBuild Apply(CueBuild b, CueDress d)
    {
        var fitting = d.Trim switch
        {
            "silver" => Silver,
            "gold" => Gold,
            "rose" => Rgb.Hex("#C48A78"),
            _ => b.Fitting,
        };
        string wrap = d.WrapKind switch
        {
            "linen" => "linen",
            "leather" => "leather",
            "plain" => "none",
            _ => b.Wrap,
        };
        var wrapColor = d.WrapKind switch
        {
            "linen" => Rgb.Hex("#D9CBB2"),
            "leather" => Rgb.Hex("#1C1A18"),
            _ => b.WrapColor,
        };
        return b with
        {
            Fitting = fitting,
            Wrap = wrap,
            WrapColor = wrapColor,
            FittingGloss = d.Finish == "gloss" ? Math.Max(b.FittingGloss, 0.9) : b.FittingGloss,
            Sparkle = b.Sparkle || d.Finish == "glow",
        };
    }

    /// <summary>Draws the cue from tip to butt; widths are half-thicknesses in the current units.</summary>
    public static void Draw(Graphics g, PointF tip, PointF butt, float buttHalf, CueSpec spec, double time, float unit, bool shadow, CueDress? dress = null)
    {
        float dx = butt.X - tip.X, dy = butt.Y - tip.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float angle = MathF.Atan2(dy, dx) * 180 / MathF.PI;
        var bmp = Get(spec, dress);

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

        if (dress != null && dress.Signature.Length > 0)
        {
            string s = dress.Signature.Length > 10 ? dress.Signature[..10] : dress.Signature;
            using var font = new Font("Segoe UI", Math.Max(8f, buttHalf * 0.72f), FontStyle.Regular, GraphicsUnit.Pixel);
            using var shade = new SolidBrush(Color.FromArgb(170, 16, 12, 10));
            using var ink = new SolidBrush(Color.FromArgb(235, 244, 236, 214));
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(s, font, shade, len * 0.74f + 1f, -buttHalf * 0.55f + 1f);
            g.DrawString(s, font, ink, len * 0.74f, -buttHalf * 0.55f);
        }

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

    static Bitmap Render(CueBuild b, int seed, CueDress? dress)
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
                if (dress != null) (col, gloss) = Decal(col, gloss, dress, f, phi);
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

    /// <summary>A decal on the face of the cue. The tip (the first 5%) is never touched.</summary>
    static (Rgb col, double gloss) Decal(Rgb col, double gloss, CueDress dress, double f, double phi)
    {
        if (f < 0.05 || Math.Abs(phi) > 0.85) return (col, gloss);
        string? id = f switch
        {
            >= 0.56 and <= 0.68 => dress.Forearm,
            >= 0.755 and <= 0.86 => dress.WrapMark,
            >= 0.90 and <= 0.955 => dress.Sleeve,
            >= 0.968 => dress.Charm,
            _ => null,
        };
        if (string.IsNullOrEmpty(id) || !Glyph(id, ZoneU(f), phi / 0.85)) return (col, gloss);
        return (Rgb.Lerp(col, MarkColor(id), 0.86), Math.Max(gloss, 0.55));
    }

    static double ZoneU(double f) => f switch
    {
        <= 0.68 => (f - 0.56) / 0.12,
        <= 0.86 => (f - 0.755) / 0.105,
        <= 0.955 => (f - 0.90) / 0.055,
        _ => (f - 0.968) / 0.03,
    };

    static bool Glyph(string id, double u, double v)
    {
        u = Math.Clamp(u, 0, 1);
        double dx = (u - 0.5) * 2, r = Math.Sqrt(dx * dx + v * v);
        return id switch
        {
            "stripe" => u is > 0.38 and < 0.62,
            "crest" => r is > 0.28 and < 0.62,
            "dot" => r < 0.34,
            "ring" => Math.Abs(u - 0.42) < 0.06 || Math.Abs(u - 0.62) < 0.06,
            "bolt" => Math.Abs(v - (u < 0.5 ? (0.5 - u) * 1.2 : (u - 0.5) * -1.2)) < 0.22 && u is > 0.18 and < 0.82,
            "diamond" => Math.Abs(dx) + Math.Abs(v) < 0.72,
            "wave" => Math.Abs(v - Math.Sin(u * Math.PI * 2) * 0.4) < 0.16,
            "seal" => r is > 0.34 and < 0.58 || r < 0.16,
            "laurel" => Math.Abs(Math.Abs(v) - (0.25 + 0.35 * Math.Sin(u * Math.PI))) < 0.1 && u is > 0.12 and < 0.88,
            "star" => r < 0.18 + 0.42 * Math.Abs(Math.Cos(Math.Atan2(v, dx) * 2.5)),
            "disc" => r < 0.7,
            "charmring" => r is > 0.35 and < 0.75,
            "charmstar" => r < 0.2 + 0.5 * Math.Abs(Math.Cos(Math.Atan2(v, dx) * 2.5)),
            _ => false,
        };
    }

    static Rgb MarkColor(string id) => id switch
    {
        "crest" or "star" or "charmstar" => Gold,
        "dot" => Rgb.Hex("#E23B3B"),
        "bolt" => Rgb.Hex("#FFC23D"),
        "diamond" => Rgb.Hex("#E7F3FF"),
        "wave" => Rgb.Hex("#3DFFE8"),
        "seal" => Rgb.Hex("#1FA86A"),
        "laurel" => Rgb.Hex("#C4A46A"),
        "disc" or "charmring" or "ring" => Silver,
        _ => Rgb.Hex("#F4F1EA"),
    };

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
                bool thread = b.Fitting.G > 0.85 && b.Fitting.R < 0.6 && (int)Math.Floor(px / 11.0) % 7 == 0;
                var baseC = Rgb.Hex(weave ? "#1C1C21" : "#26262C");
                return (thread ? Rgb.Lerp(baseC, b.Fitting, 0.55) : baseC, 0.9);
            }
            if (b.Shaft == "ash")
                return (Rgb.Lerp(WoodKind.Maple.Sample(px * 0.55, phi * 14, seed), Rgb.Hex("#F4EBD8"), 0.35), 0.28);
            if (b.Shaft == "crystal")
            {
                bool facet = ((int)Math.Floor(phi * 4 + 4) + (int)Math.Floor(px / 22.0)) % 3 == 0;
                return (Rgb.Lerp(Rgb.Hex("#F7F4FF"), Rgb.Hex("#C9DFFF"), facet ? 0.72 : 0.18), 0.96);
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

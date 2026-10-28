using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NeonCue.Rendering;

/// <summary>Linear-ish RGB colour in 0..1 used by the per-pixel renderers.</summary>
readonly struct Rgb
{
    public readonly double R, G, B;
    public Rgb(double r, double g, double b) { R = r; G = g; B = b; }

    public static Rgb Hex(string hex)
    {
        var c = Theme.Hex(hex);
        return new Rgb(c.R / 255.0, c.G / 255.0, c.B / 255.0);
    }

    public static Rgb operator *(Rgb a, double s) => new(a.R * s, a.G * s, a.B * s);
    public static Rgb operator *(Rgb a, Rgb b) => new(a.R * b.R, a.G * b.G, a.B * b.B);
    public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Rgb Lerp(Rgb a, Rgb b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    public static readonly Rgb White = new(1, 1, 1);
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>Premultiplied ARGB pixel.</summary>
    public int ToPArgb(double alpha = 1)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        int a = (int)(alpha * 255 + 0.5);
        int r = (int)(Math.Clamp(R, 0, 1) * alpha * 255 + 0.5);
        int g = (int)(Math.Clamp(G, 0, 1) * alpha * 255 + 0.5);
        int b = (int)(Math.Clamp(B, 0, 1) * alpha * 255 + 0.5);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}

static class Noise
{
    static int Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    public static double Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / (double)0xFFFFFF;

    public static double Value(double x, double y, int seed = 0)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        double a = Rand(xi, yi, seed), b = Rand(xi + 1, yi, seed);
        double c = Rand(xi, yi + 1, seed), d = Rand(xi + 1, yi + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    public static double Fbm(double x, double y, int octaves = 4, int seed = 0)
    {
        double sum = 0, amp = 0.5, f = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value(x * f, y * f, seed + i * 17);
            norm += amp;
            amp *= 0.5;
            f *= 2.03;
        }
        return sum / norm;
    }
}

/// <summary>A wood species: early/late wood colours, ring density and pore strength.</summary>
readonly record struct WoodKind(Rgb Light, Rgb Dark, double Rings, double Pores, double Figure)
{
    public static readonly WoodKind Maple = new(Rgb.Hex("#E9D2A6"), Rgb.Hex("#C9A574"), 0.22, 0.05, 0);
    public static readonly WoodKind Birdseye = new(Rgb.Hex("#EED9AE"), Rgb.Hex("#C49A62"), 0.25, 0.05, 1);
    public static readonly WoodKind Walnut = new(Rgb.Hex("#7A5235"), Rgb.Hex("#3E2817"), 0.3, 0.12, 0);
    public static readonly WoodKind Mahogany = new(Rgb.Hex("#8A3B22"), Rgb.Hex("#4A1A0E"), 0.28, 0.1, 0);
    public static readonly WoodKind Rosewood = new(Rgb.Hex("#6E2E1E"), Rgb.Hex("#2A0F0A"), 0.45, 0.1, 0);
    public static readonly WoodKind Cocobolo = new(Rgb.Hex("#B5562A"), Rgb.Hex("#4A1C0C"), 0.55, 0.08, 0);
    public static readonly WoodKind Ebony = new(Rgb.Hex("#2A2420"), Rgb.Hex("#0C0A09"), 0.5, 0.06, 0);
    public static readonly WoodKind Purpleheart = new(Rgb.Hex("#6A2E6E"), Rgb.Hex("#33103A"), 0.35, 0.08, 0);
    public static readonly WoodKind Oak = new(Rgb.Hex("#6B5034"), Rgb.Hex("#3A2918"), 0.18, 0.18, 0);
    public static readonly WoodKind BlackLacquer = new(Rgb.Hex("#24201E"), Rgb.Hex("#121010"), 0.2, 0.02, 0);
    public static readonly WoodKind Gray = new(Rgb.Hex("#6D7078"), Rgb.Hex("#3C3E44"), 0.3, 0.05, 0);

    /// <summary>Colour of the wood at a point; <paramref name="along"/> runs with the grain.</summary>
    public Rgb Sample(double along, double across, int seed)
    {
        double warp = Noise.Fbm(along * 0.004, across * 0.05, 3, seed) * 7;
        double rings = across * Rings + warp;
        double t = 0.5 + 0.5 * Math.Sin(rings * Math.PI * 2);
        t = t * t * t;
        double pores = (Noise.Value(along * 0.05, across * 2.6, seed + 5) - 0.5) * Pores * 2;
        double fig = 0;
        if (Figure > 0)
        {
            // Bird's-eye figure: small dark dimples.
            double e = Noise.Value(along * 0.35, across * 0.35, seed + 9);
            fig = e > 0.82 ? (e - 0.82) * 2.2 : 0;
        }
        return Rgb.Lerp(Light, Dark, Math.Clamp(t * 0.7 + pores + fig, 0, 1));
    }
}

/// <summary>A CPU-side ARGB buffer turned into a premultiplied bitmap.</summary>
sealed class PixelCanvas
{
    public readonly int Width, Height;
    public readonly int[] Pixels;

    public PixelCanvas(int w, int h)
    {
        Width = Math.Max(1, w);
        Height = Math.Max(1, h);
        Pixels = new int[Width * Height];
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        CopyTo(bmp);
        return bmp;
    }

    public void CopyTo(Bitmap bmp)
    {
        if (bmp.Width != Width || bmp.Height != Height || bmp.PixelFormat != PixelFormat.Format32bppPArgb)
            throw new ArgumentException(
                $"The image is {bmp.Width}x{bmp.Height} {bmp.PixelFormat}. The pixel buffer is {Width}x{Height} 32-bit premultiplied.");
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * Width, data.Scan0 + y * data.Stride, Width);
        }
        finally { bmp.UnlockBits(data); }
    }
}

static class MathX
{
    public static double Smooth(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Signed distance to a rounded rectangle (negative inside).</summary>
    public static double RoundRectSdf(double x, double y, double cx, double cy, double hw, double hh, double r)
    {
        double qx = Math.Abs(x - cx) - (hw - r), qy = Math.Abs(y - cy) - (hh - r);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }
}

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using NeonCue.Core;

namespace NeonCue.Rendering;

readonly struct Vec3
{
    public readonly double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3 Normalized() { double l = Math.Sqrt(X * X + Y * Y + Z * Z); return new(X / l, Y / l, Z / l); }
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

/// <summary>Rotation matrix: maps ball-local directions to world (x right, y down, z towards the viewer).</summary>
struct Mat3
{
    public double M00, M01, M02, M10, M11, M12, M20, M21, M22;

    public static Mat3 Identity => new() { M00 = 1, M11 = 1, M22 = 1 };

    public static Mat3 Rotation(Vec3 k, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;
        return new Mat3
        {
            M00 = c + k.X * k.X * t, M01 = k.X * k.Y * t - k.Z * s, M02 = k.X * k.Z * t + k.Y * s,
            M10 = k.Y * k.X * t + k.Z * s, M11 = c + k.Y * k.Y * t, M12 = k.Y * k.Z * t - k.X * s,
            M20 = k.Z * k.X * t - k.Y * s, M21 = k.Z * k.Y * t + k.X * s, M22 = c + k.Z * k.Z * t,
        };
    }

    public static Mat3 operator *(Mat3 a, Mat3 b) => new()
    {
        M00 = a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        M01 = a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        M02 = a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        M10 = a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        M11 = a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        M12 = a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        M20 = a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        M21 = a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        M22 = a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
    };

    /// <summary>World direction to ball-local (transpose = inverse for rotations).</summary>
    public Vec3 ToLocal(Vec3 w) => new(M00 * w.X + M10 * w.Y + M20 * w.Z, M01 * w.X + M11 * w.Y + M21 * w.Z, M02 * w.X + M12 * w.Y + M22 * w.Z);

    public bool NearlyEquals(Mat3 o, double tolerance = 1e-4) =>
        Math.Abs(M00 - o.M00) + Math.Abs(M01 - o.M01) + Math.Abs(M02 - o.M02) +
        Math.Abs(M10 - o.M10) + Math.Abs(M11 - o.M11) + Math.Abs(M12 - o.M12) +
        Math.Abs(M20 - o.M20) + Math.Abs(M21 - o.M21) + Math.Abs(M22 - o.M22) < tolerance;

    /// <summary>Gram-Schmidt clean-up so accumulated rolling doesn't skew the matrix.</summary>
    public Mat3 Orthonormalized()
    {
        var c0 = new Vec3(M00, M10, M20).Normalized();
        var c1 = new Vec3(M01, M11, M21);
        double d = c0.Dot(c1);
        c1 = new Vec3(c1.X - c0.X * d, c1.Y - c0.Y * d, c1.Z - c0.Z * d).Normalized();
        var c2 = new Vec3(c0.Y * c1.Z - c0.Z * c1.Y, c0.Z * c1.X - c0.X * c1.Z, c0.X * c1.Y - c0.Y * c1.X);
        return new Mat3 { M00 = c0.X, M10 = c0.Y, M20 = c0.Z, M01 = c1.X, M11 = c1.Y, M21 = c1.Z, M02 = c2.X, M12 = c2.Y, M22 = c2.Z };
    }

    /// <summary>A random resting orientation that keeps the number roughly facing up.</summary>
    public static Mat3 RandomResting(Random rng)
    {
        var spin = Rotation(new Vec3(0, 0, 1), rng.NextDouble() * Math.PI * 2);
        double a = rng.NextDouble() * Math.PI * 2;
        var tilt = Rotation(new Vec3(Math.Cos(a), Math.Sin(a), 0), rng.NextDouble() * 0.7);
        return tilt * spin;
    }
}

/// <summary>
/// Ray-traced-style ball sprites: every pixel maps to a point on the sphere, is looked up
/// in the ball's surface (colour, stripe, number discs on both sides) through its current
/// orientation, and lit with diffuse + two specular lobes + cloth bounce. Supersampled 2x2.
/// </summary>
sealed class BallRenderer : IDisposable
{
    const int GlyphSize = 64;
    static float[]?[]? glyphs;

    public const int MaxBalls = 22;
    /// <summary>Time spent re-rendering ball sprites (benchmark).</summary>
    public static double RenderMs;
    public static int Renders;
    readonly Bitmap?[] cache = new Bitmap?[MaxBalls];
    readonly Mat3[] cachedOri = new Mat3[MaxBalls];
    readonly bool[] valid = new bool[MaxBalls];
    bool snooker;
    float radius = -1;
    TableStyle style;

    static readonly Vec3 Light = new Vec3(-0.32, -0.42, 0.85).Normalized();
    static readonly Vec3 Half = (Light + new Vec3(0, 0, 1)).Normalized();
    static readonly Rgb Ivory = Rgb.Hex("#F4F1E6");
    static readonly Rgb Ink = Rgb.Hex("#141414");
    static readonly Rgb CueDot = Rgb.Hex("#C8102E");

    /// <summary>
    /// Re-renders every stale sprite in one go, in parallel across cores (each ball's pixels are
    /// independent), then uploads them; afterwards <see cref="Get"/> is a cache hit.
    /// </summary>
    public void Prepare(IReadOnlyList<(int n, Mat3 ori)> balls, float r)
    {
        if (Math.Abs(r - radius) > 0.01f || style != Look.Style || snooker != Look.Snooker)
        {
            Dispose();
            radius = r;
            style = Look.Style;
            snooker = Look.Snooker;
        }
        // A turn smaller than about half a pixel at the rim is invisible: keep the cached sprite.
        double tol = 1.2 / Math.Max(4, r);
        var stale = new List<(int n, Mat3 ori)>();
        foreach (var (n, ori) in balls)
            if (cache[n] == null || !valid[n] || !cachedOri[n].NearlyEquals(ori, tol)) stale.Add((n, ori));
        if (stale.Count == 0) return;

        int size = (int)Math.Ceiling(r * 2) + 2;
        EnsureGlyphs();
        var canvases = new PixelCanvas[stale.Count];
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Parallel.For(0, stale.Count, i =>
        {
            canvases[i] = new PixelCanvas(size, size);
            Render(canvases[i], stale[i].n, stale[i].ori, r);
        });
        for (int i = 0; i < stale.Count; i++)
        {
            var (n, ori) = stale[i];
            cache[n] ??= new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            canvases[i].CopyTo(cache[n]!);
            cachedOri[n] = ori;
            valid[n] = true;
        }
        RenderMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Renders += stale.Count;
    }

    public Bitmap Get(int n, Mat3 ori, float r)
    {
        if (Math.Abs(r - radius) > 0.01f || style != Look.Style || snooker != Look.Snooker)
        {
            Dispose();
            radius = r;
            style = Look.Style;
            snooker = Look.Snooker;
        }
        var bmp = cache[n];
        if (bmp != null && valid[n] && cachedOri[n].NearlyEquals(ori)) return bmp;

        int size = (int)Math.Ceiling(r * 2) + 2;
        bmp ??= new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        cache[n] = bmp;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var canvas = new PixelCanvas(size, size);
        Render(canvas, n, ori, r);
        canvas.CopyTo(bmp);
        RenderMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Renders++;
        cachedOri[n] = ori;
        valid[n] = true;
        return bmp;
    }

    /// <summary>Draws the ball centred at <paramref name="center"/>; <paramref name="scale"/> converts sprite pixels to the current units.</summary>
    public void Draw(Graphics g, int n, Mat3 ori, float pixelRadius, PointF center, float scale)
    {
        var bmp = Get(n, ori, pixelRadius);
        float w = bmp.Width * scale, h = bmp.Height * scale;
        g.DrawImage(bmp, center.X - w / 2, center.Y - h / 2, w, h);
    }

    /// <summary>
    /// Per-size geometry: for every sub-sample on the disc, its surface normal and the lighting terms
    /// (which don't depend on the ball's rotation). Built once; each frame only the texture lookup
    /// through the current orientation is evaluated.
    /// </summary>
    sealed class Geometry
    {
        public int[] PixelOf = [];
        public Vec3[] Normal = [];
        public float[] Shade = [];
        public float[] AddR = [], AddG = [], AddB = [];
        public int[] Hits = [];
    }

    static readonly Dictionary<(int size, float r, TableStyle style), Geometry> geometries = new();

    static Geometry GeometryFor(int size, float r)
    {
        var key = (size, r, Look.Style);
        lock (geometries)
        {
            if (geometries.TryGetValue(key, out var geo)) return geo;
            const int ss = 2;
            float center = size / 2f;
            Rgb felt = Look.Felt;
            var pix = new List<int>();
            var nrm = new List<Vec3>();
            var shade = new List<float>();
            var ar = new List<float>();
            var ag = new List<float>();
            var ab = new List<float>();
            var hits = new int[size * size];
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            double x = (px + (sx + 0.5) / ss - center) / r, y = (py + (sy + 0.5) / ss - center) / r;
                            double d2 = x * x + y * y;
                            if (d2 >= 1) continue;
                            var n = new Vec3(x, y, Math.Sqrt(1 - d2));
                            double diff = Math.Max(0, n.Dot(Light));
                            double nh = Math.Max(0, n.Dot(Half));
                            double spec = Math.Pow(nh, 60) * 0.7 + Math.Pow(nh, 700) * 1.5;
                            double rim = Math.Pow(1 - n.Z, 3);
                            pix.Add(py * size + px);
                            nrm.Add(n);
                            shade.Add((float)(0.2 + 0.86 * diff));
                            ar.Add((float)(felt.R * 0.16 * rim + spec + rim * 0.06));
                            ag.Add((float)(felt.G * 0.16 * rim + spec + rim * 0.06));
                            ab.Add((float)(felt.B * 0.16 * rim + spec + rim * 0.06));
                            hits[py * size + px]++;
                        }
            geo = new Geometry
            {
                PixelOf = pix.ToArray(), Normal = nrm.ToArray(), Shade = shade.ToArray(),
                AddR = ar.ToArray(), AddG = ag.ToArray(), AddB = ab.ToArray(), Hits = hits,
            };
            geometries[key] = geo;
            return geo;
        }
    }

    static void Render(PixelCanvas c, int n, Mat3 ori, float r)
    {
        EnsureGlyphs();
        var geo = GeometryFor(c.Width, r);
        int count = c.Width * c.Height;
        var accR = new float[count];
        var accG = new float[count];
        var accB = new float[count];
        for (int i = 0; i < geo.PixelOf.Length; i++)
        {
            Rgb tex = Surface(n, ori.ToLocal(geo.Normal[i]));
            float sh = geo.Shade[i];
            int px = geo.PixelOf[i];
            accR[px] += (float)tex.R * sh + geo.AddR[i];
            accG[px] += (float)tex.G * sh + geo.AddG[i];
            accB[px] += (float)tex.B * sh + geo.AddB[i];
        }
        for (int px = 0; px < count; px++)
        {
            int h = geo.Hits[px];
            if (h == 0) continue;
            c.Pixels[px] = new Rgb(accR[px] / h, accG[px] / h, accB[px] / h).ToPArgb(h / 4.0);
        }
    }

    static Rgb Surface(int n, Vec3 p)
    {
        if (Look.Snooker && n > 0) return Look.SnookerColor(n); // plain, unnumbered snooker balls
        if (n == 0)
        {
            // Two small red dots so you can see the cue ball spin.
            return Math.Abs(p.X) > 0.975 ? CueDot : Ivory;
        }
        Rgb baseCol = Look.BallColor(n);
        // Stripe band is wider than the number disc, as on a real set.
        Rgb c = n >= 9 ? (Math.Abs(p.X) < 0.6 ? baseCol : Ivory) : baseCol;
        if (Math.Abs(p.Z) > 0.9)
        {
            double u = (p.Z > 0 ? p.X : -p.X) / 0.436, v = p.Y / 0.436;
            double ink = SampleGlyph(n, u, v);
            c = Rgb.Lerp(Ivory, Ink, ink);
        }
        return c;
    }

    static double SampleGlyph(int n, double u, double v)
    {
        var g = glyphs![n]!;
        double gx = (u * 0.5 + 0.5) * (GlyphSize - 1), gy = (v * 0.5 + 0.5) * (GlyphSize - 1);
        int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
        if (x0 < 0 || y0 < 0 || x0 >= GlyphSize - 1 || y0 >= GlyphSize - 1) return 0;
        double fx = gx - x0, fy = gy - y0;
        double a = g[y0 * GlyphSize + x0], b = g[y0 * GlyphSize + x0 + 1];
        double cc = g[(y0 + 1) * GlyphSize + x0], d = g[(y0 + 1) * GlyphSize + x0 + 1];
        return a + (b - a) * fx + (cc - a) * fy + (a - b - cc + d) * fx * fy;
    }

    static void EnsureGlyphs()
    {
        if (glyphs != null) return;
        var list = new float[]?[16];
        using var bmp = new Bitmap(GlyphSize, GlyphSize, PixelFormat.Format32bppArgb);
        for (int n = 1; n <= 15; n++)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Real sets underline the 6 and 9 so they can't be confused.
                var style = n is 6 or 9 ? FontStyle.Bold | FontStyle.Underline : FontStyle.Bold;
                using var font = new Font("Arial", n >= 10 ? 30 : 38, style, GraphicsUnit.Pixel);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(n.ToString(), font, Brushes.Black, new RectangleF(0, 2, GlyphSize, GlyphSize), sf);
            }
            var arr = new float[GlyphSize * GlyphSize];
            for (int y = 0; y < GlyphSize; y++)
                for (int x = 0; x < GlyphSize; x++)
                    arr[y * GlyphSize + x] = 1f - bmp.GetPixel(x, y).R / 255f;
            list[n] = arr;
        }
        glyphs = list;
    }

    /// <summary>Soft contact shadow, alpha-only.</summary>
    public static Bitmap MakeShadow(float r)
    {
        int size = (int)Math.Ceiling(r * 3.2f) + 2;
        var c = new PixelCanvas(size, size);
        double ctr = size / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt((x + 0.5 - ctr) * (x + 0.5 - ctr) + (y + 0.5 - ctr) * (y + 0.5 - ctr)) / (r * 1.5);
                double a = 0.62 * (1 - MathX.Smooth(0.45, 1.0, d));
                if (a > 0) c.Pixels[y * size + x] = Rgb.Black.ToPArgb(a);
            }
        return c.ToBitmap();
    }

    public void Dispose()
    {
        for (int i = 0; i < cache.Length; i++)
        {
            cache[i]?.Dispose();
            cache[i] = null;
            valid[i] = false;
        }
    }
}

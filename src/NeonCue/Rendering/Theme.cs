using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace NeonCue.Rendering;

static class Theme
{
    public static readonly Color Bg = Hex("#07060F");
    public static readonly Color BgTop = Hex("#0D0A1F");
    public static readonly Color Panel = Hex("#130F26");
    public static readonly Color PanelLight = Hex("#1D1838");
    public static readonly Color Cyan = Hex("#00F0FF");
    public static readonly Color Magenta = Hex("#FF2BD6");
    public static readonly Color Violet = Hex("#8A5CFF");
    public static readonly Color Lime = Hex("#B6FF3B");
    public static readonly Color Amber = Hex("#FFC23D");
    public static readonly Color Red = Hex("#FF3B5C");
    public static readonly Color Text = Hex("#ECE9FF");
    public static readonly Color TextDim = Hex("#8E89BC");
    public static readonly Color Felt = Hex("#0E4A78");
    public static readonly Color FeltEdge = Hex("#061A33");
    public static readonly Color Cushion = Hex("#0A3157");
    public static readonly Color Frame = Hex("#1A1531");
    public static readonly Color FrameDark = Hex("#0C0A18");

    public static Color PlayerColor(int index) => index == 0 ? Cyan : Magenta;

    static readonly Color[] BallColors =
    [
        Hex("#F4F3EE"), // cue
        Hex("#FFC81E"), Hex("#2365FF"), Hex("#F0273A"), Hex("#8B3BE0"),
        Hex("#FF7B1C"), Hex("#16B864"), Hex("#A0202A"), Hex("#121214"),
    ];

    public static Color BallColor(int n)
    {
        if (Look.Snooker || n > 15)
        {
            var c = Look.SnookerColor(n);
            return Color.FromArgb((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
        }
        return n <= 8 ? BallColors[n] : BallColors[n - 8];
    }

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }

    public static Color A(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ------------------------------------------------------------------ fonts

    static FontFamily? family;
    public static FontFamily Family => family ??= PickFamily();

    static FontFamily PickFamily()
    {
        foreach (var name in new[] { "Bahnschrift", "Segoe UI" })
        {
            try { return new FontFamily(name); } catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    static readonly Dictionary<(int, FontStyle), Font> fonts = new();

    /// <summary>Cached font, size in pixels.</summary>
    public static Font F(float px, FontStyle style = FontStyle.Regular)
    {
        int key = (int)Math.Round(px * 2);
        if (!Family.IsStyleAvailable(style)) style = FontStyle.Regular;
        if (!fonts.TryGetValue((key, style), out var f))
        {
            f = new Font(Family, Math.Max(4, key / 2f), style, GraphicsUnit.Pixel);
            fonts[(key, style)] = f;
        }
        return f;
    }

    public static void Setup(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighSpeed;
    }

    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            Setup(g);
            using var ring = new GraphicsPath();
            ring.AddEllipse(6, 6, 52, 52);
            Gfx.GlowPath(g, ring, Magenta, 3, 2, 3, 60);
            using var ball = BallSprites.Make(8, 22);
            g.DrawImage(ball, 32 - ball.Width / 2f, 32 - ball.Height / 2f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

static class Gfx
{
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void GlowPath(Graphics g, GraphicsPath path, Color c, float width, float spread = 3, int layers = 4, int alpha = 32)
    {
        for (int i = layers; i >= 1; i--)
        {
            using var pen = new Pen(Theme.A(c, alpha), width + spread * i) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using var core = new Pen(c, width) { LineJoin = LineJoin.Round };
        g.DrawPath(core, path);
    }

    public static void GlowLine(Graphics g, PointF a, PointF b, Color c, float width, float spread = 3, int layers = 3, int alpha = 40, DashStyle dash = DashStyle.Solid)
    {
        if (Look.Realistic) { layers = Math.Min(layers, 1); alpha /= 2; }
        for (int i = layers; i >= 1; i--)
        {
            using var pen = new Pen(Theme.A(c, alpha), width + spread * i) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = dash };
            g.DrawLine(pen, a, b);
        }
        using var core = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = dash };
        g.DrawLine(core, a, b);
    }

    static readonly Dictionary<(string, float, FontStyle, int, int, float, int, int, StringAlignment), (Bitmap bmp, PointF anchor)> glowCache = new();

    /// <summary>Glowing text, rendered once into a cached bitmap and blitted (fades share 16 alpha levels).</summary>
    public static void GlowText(Graphics g, string text, Font font, Color glow, Color core, PointF pt,
        StringAlignment align = StringAlignment.Center, float spread = 4, int layers = 4, int alpha = 34)
    {
        static int Q(Color c) => (c.A / 16 * 16 << 24) | (c.R << 16) | (c.G << 8) | c.B;
        var key = (text, font.Size, font.Style, Q(glow), Q(core), spread, layers, alpha / 4 * 4, align);
        if (!glowCache.TryGetValue(key, out var entry))
        {
            if (glowCache.Count > 300)
            {
                foreach (var v in glowCache.Values) v.bmp.Dispose();
                glowCache.Clear();
            }
            entry = RenderGlowText(text, font, Color.FromArgb(glow.A / 16 * 16, glow), Color.FromArgb(core.A / 16 * 16, core), align, spread, layers, alpha / 4 * 4);
            glowCache[key] = entry;
        }
        g.DrawImageUnscaled(entry.bmp, (int)Math.Round(pt.X - entry.anchor.X), (int)Math.Round(pt.Y - entry.anchor.Y));
    }

    static (Bitmap, PointF) RenderGlowText(string text, Font font, Color glow, Color core, StringAlignment align, float spread, int layers, int alpha)
    {
        using var measure = new GraphicsPath(FillMode.Winding);
        using var sf0 = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center };
        measure.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, sf0);
        var b = measure.GetBounds();
        float pad = spread * layers + 4;
        int w = Math.Max(1, (int)Math.Ceiling(b.Width + pad * 2)), h = Math.Max(1, (int)Math.Ceiling(b.Height + pad * 2));
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var anchor = new PointF(pad - b.X, pad - b.Y);
        using (var g = Graphics.FromImage(bmp))
        {
            Theme.Setup(g);
            GlowTextDirect(g, text, font, glow, core, anchor, align, spread, layers, alpha);
        }
        return (bmp, anchor);
    }

    static void GlowTextDirect(Graphics g, string text, Font font, Color glow, Color core, PointF pt,
        StringAlignment align, float spread, int layers, int alpha)
    {
        // Winding fill: variable fonts like Bahnschrift have overlapping contours.
        using var path = new GraphicsPath(FillMode.Winding);
        using var sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center };
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, pt, sf);
        for (int i = layers; i >= 1; i--)
        {
            using var pen = new Pen(Theme.A(glow, alpha), spread * i) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        using var br = new SolidBrush(core);
        g.FillPath(br, path);
    }

    static RectangleF PixelRect(RectangleF rect) =>
        new(MathF.Round(rect.X), MathF.Round(rect.Y), MathF.Max(1, MathF.Round(rect.Width)), MathF.Max(1, MathF.Round(rect.Height)));

    public static void Text(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
    {
        using var crisp = new Ui.CrispText(g);
        using var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        using var br = new SolidBrush(color);
        g.DrawString(text, font, br, PixelRect(rect), sf);
    }

    public static void WrappedText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment h = StringAlignment.Near)
    {
        using var crisp = new Ui.CrispText(g);
        using var sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoClip };
        using var br = new SolidBrush(color);
        g.DrawString(text, font, br, PixelRect(rect), sf);
    }

    public static void Triangle(Graphics g, PointF center, float size, bool right, Color c)
    {
        float s = right ? 1 : -1;
        PointF[] pts =
        [
            new(center.X - s * size * 0.5f, center.Y - size * 0.6f),
            new(center.X + s * size * 0.55f, center.Y),
            new(center.X - s * size * 0.5f, center.Y + size * 0.6f),
        ];
        using var br = new SolidBrush(c);
        g.FillPolygon(br, pts);
    }
}

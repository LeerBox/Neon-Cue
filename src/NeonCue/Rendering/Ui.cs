using System.Drawing.Drawing2D;

namespace NeonCue.Rendering;

/// <summary>
/// The interface design system: a restrained broadcast-sports look. Charcoal panels with hairline
/// borders, one accent (champagne gold; steel blue for the second player), DIN-style numerals,
/// letter-spaced headings, and no glows.
/// </summary>
static class Ui
{
    public static readonly Color Bg = Theme.Hex("#0A0C0F");
    public static readonly Color Panel = Theme.Hex("#12151A");
    public static readonly Color Panel2 = Theme.Hex("#1A1E25");
    public static readonly Color Line = Color.FromArgb(34, 255, 255, 255);
    public static readonly Color LineStrong = Color.FromArgb(64, 255, 255, 255);
    public static readonly Color Text = Theme.Hex("#F1EFEA");
    public static readonly Color Text2 = Theme.Hex("#A4A9B1");
    public static readonly Color Text3 = Theme.Hex("#6F757E");
    public static readonly Color Gold = Theme.Hex("#C9A55E");
    public static readonly Color GoldBright = Theme.Hex("#E3C584");
    public static readonly Color Blue = Theme.Hex("#6F9CD6");
    public static readonly Color Good = Theme.Hex("#63B27B");
    public static readonly Color Warn = Theme.Hex("#D8A64B");
    public static readonly Color Bad = Theme.Hex("#D45B53");
    public static readonly Color Ink = Theme.Hex("#15120B");

    public static Color Player(int i) => i == 0 ? Gold : Blue;

    /// <summary>Maps the old vivid accent colours used by game messages onto the design palette.</summary>
    public static Color Accent(Color c)
    {
        if (c.ToArgb() == Theme.Red.ToArgb()) return Bad;
        if (c.ToArgb() == Theme.Lime.ToArgb()) return Good;
        if (c.ToArgb() == Theme.Amber.ToArgb()) return Warn;
        if (c.ToArgb() == Theme.Magenta.ToArgb()) return Blue;
        if (c.ToArgb() == Theme.TextDim.ToArgb()) return Text3;
        return Gold;
    }

    // ------------------------------------------------------------------ fonts

    static readonly Dictionary<(string, int, FontStyle), Font> fonts = new();
    static readonly Dictionary<string, FontFamily> families = new();

    static FontFamily Family(params string[] names)
    {
        string key = string.Join("|", names);
        if (families.TryGetValue(key, out var f)) return f;
        foreach (var n in names)
        {
            try { f = new FontFamily(n); break; } catch (ArgumentException) { }
        }
        f ??= FontFamily.GenericSansSerif;
        families[key] = f;
        return f;
    }

    static Font Make(FontFamily fam, float px, FontStyle style)
    {
        int k = (int)Math.Round(px * 2);
        if (!fam.IsStyleAvailable(style)) style = FontStyle.Regular;
        var key = (fam.Name, k, style);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = new Font(fam, Math.Max(4, k / 2f), style, GraphicsUnit.Pixel);
            fonts[key] = font;
        }
        return font;
    }

    /// <summary>Headings and labels: DIN-style condensed sans.</summary>
    public static Font Head(float px) => Make(Family("Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI"), px, FontStyle.Regular);
    /// <summary>Scores and numbers.</summary>
    public static Font Num(float px) => Make(Family("Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI"), px, FontStyle.Regular);
    public static Font NumLight(float px) => Make(Family("Bahnschrift Light", "Bahnschrift", "Segoe UI"), px, FontStyle.Regular);
    public static Font Body(float px) => Make(Family("Segoe UI", "Arial"), px, FontStyle.Regular);
    public static Font BodyStrong(float px) => Make(Family("Segoe UI Semibold", "Segoe UI"), px, FontStyle.Regular);

    // ------------------------------------------------------------------ text

    static readonly StringFormat Typographic = MakeTypographic();
    static StringFormat MakeTypographic()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    static readonly Dictionary<(char, string, float), float> charWidths = new();

    static float CharWidth(Graphics g, char c, Font f)
    {
        var key = (c, f.Name, f.Size);
        if (!charWidths.TryGetValue(key, out float w))
        {
            w = g.MeasureString(c.ToString(), f, PointF.Empty, Typographic).Width;
            charWidths[key] = w;
        }
        return w;
    }

    /// <summary>Width of letter-spaced text.</summary>
    public static float TrackedWidth(Graphics g, string text, Font f, float tracking)
    {
        float w = 0;
        foreach (char c in text) w += CharWidth(g, c, f) + tracking;
        return Math.Max(0, w - tracking);
    }

    /// <summary>Letter-spaced text (labels, headings). <paramref name="y"/> is the vertical centre.</summary>
    public static void Tracked(Graphics g, string text, Font f, Color color, float x, float y, float tracking,
        StringAlignment align = StringAlignment.Near)
    {
        float w = TrackedWidth(g, text, f, tracking);
        if (align == StringAlignment.Center) x -= w / 2;
        else if (align == StringAlignment.Far) x -= w;
        float top = y - f.Size * 0.62f;
        using var br = new SolidBrush(color);
        foreach (char c in text)
        {
            g.DrawString(c.ToString(), f, br, x, top, Typographic);
            x += CharWidth(g, c, f) + tracking;
        }
    }

    public static void Write(Graphics g, string text, Font f, Color color, RectangleF r,
        StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center) =>
        Gfx.Text(g, text, f, color, r, h, v);

    // ------------------------------------------------------------------ surfaces

    /// <summary>Charcoal panel: subtle vertical gradient, hairline border and a faint top highlight.</summary>
    public static void Card(Graphics g, RectangleF r, float radius, int alpha = 236, bool border = true, Color? borderColor = null)
    {
        if (r.Width <= 1 || r.Height <= 1) return;
        using var path = Gfx.RoundRect(r, radius);
        using (var br = new LinearGradientBrush(r, Color.FromArgb(alpha, Panel2), Color.FromArgb(alpha, Panel), 90f))
            g.FillPath(br, path);
        if (border)
        {
            using var pen = new Pen(borderColor ?? Line, 1f);
            g.DrawPath(pen, path);
        }
        using var hi = new Pen(Color.FromArgb(16, 255, 255, 255), 1f);
        g.DrawLine(hi, r.X + radius, r.Y + 1, r.Right - radius, r.Y + 1);
    }

    /// <summary>Soft drop shadow under a panel.</summary>
    public static void Shadow(Graphics g, RectangleF r, float radius, float spread)
    {
        for (int i = 3; i >= 1; i--)
        {
            var s = RectangleF.Inflate(r, spread * i / 3f, spread * i / 3f);
            s.Offset(0, spread * 0.35f);
            using var path = Gfx.RoundRect(s, radius + spread * i / 3f);
            using var br = new SolidBrush(Color.FromArgb(22, 0, 0, 0));
            g.FillPath(br, path);
        }
    }

    public static void Hairline(Graphics g, float x1, float y1, float x2, float y2, Color? color = null, float width = 1f)
    {
        using var pen = new Pen(color ?? Line, width);
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    /// <summary>Thin chevron arrow.</summary>
    public static void Chevron(Graphics g, PointF c, float s, bool right, Color color, float width = 1.6f)
    {
        float d = right ? 1 : -1;
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(c.X - d * s * 0.35f, c.Y - s * 0.6f), new PointF(c.X + d * s * 0.35f, c.Y), new PointF(c.X - d * s * 0.35f, c.Y + s * 0.6f)]);
    }

    /// <summary>Dims the scene behind a modal panel.</summary>
    public static void Scrim(Graphics g, Size size, int alpha = 170)
    {
        using var br = new SolidBrush(Color.FromArgb(alpha, 4, 5, 7));
        g.FillRectangle(br, new Rectangle(Point.Empty, size));
    }

    /// <summary>Modal heading: letter-spaced title with a short gold rule beneath.</summary>
    public static void Heading(Graphics g, string title, float cx, float y, float u, float size = 28)
    {
        Tracked(g, title.ToUpperInvariant(), Head(size * u), Text, cx, y, 3 * u, StringAlignment.Center);
        using var br = new SolidBrush(Gold);
        g.FillRectangle(br, cx - 26 * u, y + size * u * 0.75f, 52 * u, 2 * u);
    }
}

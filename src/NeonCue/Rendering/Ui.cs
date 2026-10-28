using System.Drawing.Drawing2D;
using System.Drawing.Text;

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
        // Whole-pixel sizes. A 13.5 px face is grid-fit onto two different rows and looks fuzzy.
        int k = Math.Max(6, (int)Math.Round(px));
        if (!fam.IsStyleAvailable(style)) style = FontStyle.Regular;
        var key = (fam.Name, k, style);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = new Font(fam, k, style, GraphicsUnit.Pixel);
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
        // GenericTypographic clips side bearings, which shears the first and last letter.
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
        return f;
    }

    /// <summary>
    /// UI text is grayscale and pixel-aligned. ClearType paints red and blue fringes, and on this
    /// dark screen those fringes read as noise around every letter.
    /// </summary>
    internal readonly struct CrispText : IDisposable
    {
        readonly Graphics g;
        readonly TextRenderingHint hint;
        readonly PixelOffsetMode pixel;
        readonly SmoothingMode smooth;
        readonly CompositingQuality quality;

        public CrispText(Graphics g)
        {
            this.g = g;
            hint = g.TextRenderingHint;
            pixel = g.PixelOffsetMode;
            smooth = g.SmoothingMode;
            quality = g.CompositingQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        public void Dispose()
        {
            g.TextRenderingHint = hint;
            g.PixelOffsetMode = pixel;
            g.SmoothingMode = smooth;
            g.CompositingQuality = quality;
        }
    }

    static float Px(float v) => MathF.Round(v);

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
        float top = Px(y - f.Size * 0.78f);
        x = Px(x);
        using var crisp = new CrispText(g);
        using var path = new GraphicsPath(FillMode.Winding);
        foreach (char c in text)
        {
            if (c != ' ')
                path.AddString(c.ToString(), f.FontFamily, (int)f.Style, f.Size, new PointF(x, top), Typographic);
            x = Px(x + CharWidth(g, c, f) + tracking);
        }
        if (path.PointCount > 0)
        {
            using var br = new SolidBrush(color);
            g.FillPath(br, path);
        }
    }

    /// <summary>
    /// Draws letter-spaced text so the ink itself is centered in <paramref name="box"/>.
    /// The centre comes from the filled outlines, not from the font's em box, so capitals
    /// do not sit low and a round letter does not pull the line off centre.
    /// </summary>
    public static void PlaceCentered(Graphics g, string text, Font f, Color color, RectangleF box, float tracking)
    {
        if (string.IsNullOrEmpty(text) || box.Width < 4 || box.Height < 4) return;
        float inner = Math.Max(8, box.Width - f.Size);
        float width = TrackedWidth(g, text, f, tracking);
        if (width > inner && text.Length > 1)
            tracking *= inner / width;

        using var crisp = new CrispText(g);
        using var path = new GraphicsPath(FillMode.Winding);
        float x = 0;
        foreach (char c in text)
        {
            if (c != ' ')
                path.AddString(c.ToString(), f.FontFamily, (int)f.Style, f.Size, new PointF(x, 0), Typographic);
            x += CharWidth(g, c, f) + tracking;
        }
        if (path.PointCount == 0) return;
        path.Flatten(null, 0.2f);
        RectangleF ink = path.GetBounds();
        float dx = (box.X + box.Width / 2f) - (ink.Left + ink.Width / 2f);
        float dy = (box.Y + box.Height / 2f) - (ink.Top + ink.Height / 2f);
        using (var m = new Matrix())
        {
            m.Translate(dx, dy);
            path.Transform(m);
        }
        using var br = new SolidBrush(color);
        g.FillPath(br, path);
    }

    public static void Write(Graphics g, string text, Font f, Color color, RectangleF r,
        StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center) =>
        Gfx.Text(g, text, f, color, r, h, v);

    /// <summary>Icon, label, and a quiet key. One height, one gap, shared by every screen.</summary>
    public static void Legend(Graphics g, float x, float cy, float maxX, float u, params (Glyph icon, string key, string label)[] items)
    {
        foreach (var item in items)
        {
            float need = HintWidth(g, item.key, item.label, u);
            if (x + need > maxX) break;
            x = Hint(g, item.icon, item.key, item.label, x, cy, u);
        }
    }

    static float Hint(Graphics g, Glyph icon, string key, string label, float x, float cy, float u)
    {
        float box = 15 * u;
        var lf = Body(12.5f * u);
        float lw = g.MeasureString(label, lf).Width;
        Glyphs.Draw(g, icon, x + box / 2f, cy, box, Text2);
        Write(g, label, lf, Text, new RectangleF(x + box + 6 * u, cy - 11 * u, lw + 2, 22 * u));
        float next = x + box + 6 * u + lw;
        if (key.Length > 0)
        {
            var kf = Head(10 * u);
            float kw = g.MeasureString(key, kf).Width;
            Write(g, key, kf, Text3, new RectangleF(next + 7 * u, cy - 10 * u, kw + 2, 20 * u));
            next += 7 * u + kw;
        }
        return next + 18 * u;
    }

    static float HintWidth(Graphics g, string key, string label, float u)
    {
        float w = 15 * u + 6 * u + g.MeasureString(label, Body(12.5f * u)).Width;
        if (key.Length > 0) w += 7 * u + g.MeasureString(key, Head(10 * u)).Width;
        return w + 18 * u;
    }

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

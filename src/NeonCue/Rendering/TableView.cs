using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>Maps table units to the screen and caches the static table artwork.</summary>
sealed class TableView : IDisposable
{
    public const double Margin = 74;

    public float Scale { get; private set; } = 1;
    public float OX { get; private set; }
    public float OY { get; private set; }

    Bitmap? cache;
    (Size size, float scale, float ox, float oy, TableStyle style, TableKind kind) cacheKey;

    public void Layout(RectangleF area)
    {
        double ww = Table.Width + 2 * Margin, wh = Table.Height + 2 * Margin;
        Scale = (float)Math.Min(area.Width / ww, area.Height / wh);
        OX = (float)(area.X + (area.Width - ww * Scale) / 2 + Margin * Scale);
        OY = (float)(area.Y + (area.Height - wh * Scale) / 2 + Margin * Scale);
    }

    public PointF ToScreen(Vec2 w) => new((float)(OX + w.X * Scale), (float)(OY + w.Y * Scale));
    public Vec2 ToWorld(PointF p) => new((p.X - OX) / Scale, (p.Y - OY) / Scale);

    public RectangleF ScreenRect(double x, double y, double w, double h) =>
        new((float)(OX + x * Scale), (float)(OY + y * Scale), (float)(w * Scale), (float)(h * Scale));

    public void ApplyWorld(Graphics g)
    {
        g.TranslateTransform(OX, OY);
        g.ScaleTransform(Scale, Scale);
    }

    public void DrawBackground(Graphics g, Size client)
    {
        var key = (client, Scale, OX, OY, Look.Style, Table.Kind);
        if (cache == null || cacheKey != key)
        {
            cache?.Dispose();
            cache = new Bitmap(Math.Max(1, client.Width), Math.Max(1, client.Height), PixelFormat.Format32bppPArgb);
            if (Look.Realistic) RealTable.Render(cache, this);
            else
            {
                using var cg = Graphics.FromImage(cache);
                Theme.Setup(cg);
                DrawScene(cg, client);
            }
            cacheKey = key;
        }
        // Opaque full-window copy: no blending needed.
        var mode = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(cache, 0, 0);
        g.CompositingMode = mode;
    }

    void DrawScene(Graphics g, Size client)
    {
        var full = new Rectangle(Point.Empty, client);
        using (var bg = new LinearGradientBrush(full, Theme.BgTop, Theme.Bg, 90f)) g.FillRectangle(bg, full);

        // Halo behind the table.
        var halo = ScreenRect(-260, -200, Table.Width + 520, Table.Height + 400);
        using (var hp = new GraphicsPath())
        {
            hp.AddEllipse(halo);
            using var hb = new PathGradientBrush(hp)
            {
                CenterColor = Color.FromArgb(70, 70, 30, 150),
                SurroundColors = [Color.FromArgb(0, 7, 6, 15)],
            };
            g.FillPath(hb, hp);
        }

        // Faint perspective grid on the floor.
        using (var gridPen = new Pen(Color.FromArgb(14, 140, 120, 255), 1))
        {
            for (int x = 0; x < client.Width; x += 48) g.DrawLine(gridPen, x, 0, x, client.Height);
            for (int y = 0; y < client.Height; y += 48) g.DrawLine(gridPen, 0, y, client.Width, y);
        }

        var state = g.Save();
        ApplyWorld(g);
        DrawTable(g);
        g.Restore(state);
    }

    static void DrawTable(Graphics g)
    {
        const float W = (float)Table.Width, H = (float)Table.Height;
        const float FO = (float)Table.FrameOuter, CD = (float)Table.CushionDepth;

        // Drop shadow.
        using (var sh = Gfx.RoundRect(new RectangleF(-FO + 10, -FO + 16, W + 2 * FO, H + 2 * FO), 34))
        using (var sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillPath(sb, sh);

        // Frame.
        var frameRect = new RectangleF(-FO, -FO, W + 2 * FO, H + 2 * FO);
        using (var frame = Gfx.RoundRect(frameRect, 34))
        {
            using var fb = new LinearGradientBrush(frameRect, Theme.Frame, Theme.FrameDark, 90f);
            g.FillPath(fb, frame);
            Gfx.GlowPath(g, frame, Theme.Magenta, 2.2f, 3.5f, 5, 26);
        }
        using (var inner = Gfx.RoundRect(new RectangleF(-FO + 9, -FO + 9, W + 2 * FO - 18, H + 2 * FO - 18), 26))
        using (var ip = new Pen(Color.FromArgb(50, 0, 240, 255), 1f))
            g.DrawPath(ip, inner);

        // Felt (extends under the cushions and into the pocket throats).
        var feltRect = new RectangleF(-CD, -CD, W + 2 * CD, H + 2 * CD);
        using (var fp = new GraphicsPath())
        {
            fp.AddRectangle(feltRect);
            using var fb = new PathGradientBrush(fp)
            {
                CenterPoint = new PointF(W / 2, H / 2),
                CenterColor = Theme.Felt,
                SurroundColors = [Theme.FeltEdge],
                FocusScales = new PointF(0.55f, 0.35f),
            };
            g.FillPath(fb, fp);
        }

        // Cloth texture.
        var rnd = new Random(42);
        using (var speck = new SolidBrush(Color.FromArgb(9, 255, 255, 255)))
            for (int i = 0; i < 2600; i++)
                g.FillRectangle(speck, (float)(rnd.NextDouble() * W), (float)(rnd.NextDouble() * H), 1.4f, 1.4f);

        // Watermark.
        using (var wm = new Font(Theme.Family, 74, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var wb = new SolidBrush(Color.FromArgb(16, 0, 240, 255)))
            g.DrawString("NEON CUE", wm, wb, new RectangleF(0, 0, W, H), sf);

        // Head string and spots (pool), or baulk line, D and colour spots (snooker).
        if (Table.HasD)
        {
            DrawSnookerMarks(g, Color.FromArgb(90, 0, 240, 255), Color.FromArgb(140, 0, 240, 255));
        }
        else
        {
            using (var hs = new Pen(Color.FromArgb(55, 0, 240, 255), 1.2f) { DashStyle = DashStyle.Dash })
                g.DrawLine(hs, (float)Table.HeadStringX, 0, (float)Table.HeadStringX, H);
            using var spot = new SolidBrush(Color.FromArgb(120, 0, 240, 255));
            foreach (var p in new[] { Table.FootSpot, Table.HeadSpot, Table.CenterSpot })
                g.FillEllipse(spot, (float)p.X - 2.2f, (float)p.Y - 2.2f, 4.4f, 4.4f);
        }

        // Cushions.
        foreach (var poly in Table.CushionPolygons)
        {
            var pts = poly.Select(v => new PointF((float)v.X, (float)v.Y)).ToArray();
            using (var cb = new SolidBrush(Theme.Cushion)) g.FillPolygon(cb, pts);
            using (var jaw = new Pen(Color.FromArgb(110, 0, 240, 255), 1.1f))
            {
                g.DrawLine(jaw, pts[1], pts[2]);
                g.DrawLine(jaw, pts[3], pts[0]);
            }
            Gfx.GlowLine(g, pts[0], pts[1], Theme.Cyan, 1.6f, 2.2f, 4, 30);
        }

        // Pockets.
        foreach (var p in Table.Pockets)
        {
            var c = p.DrawCenter;
            float r = (float)p.DrawRadius;
            var rect = new RectangleF((float)c.X - r, (float)c.Y - r, 2 * r, 2 * r);
            using var hole = new GraphicsPath();
            hole.AddEllipse(rect);
            using (var hb = new PathGradientBrush(hole) { CenterColor = Color.Black, SurroundColors = [Color.FromArgb(255, 24, 8, 40)] })
                g.FillPath(hb, hole);
            Gfx.GlowPath(g, hole, Theme.Magenta, 1.4f, 2.2f, 4, 34);
        }

        // Diamonds.
        float dOff = (float)(Table.CushionDepth + (Table.FrameOuter - Table.CushionDepth) / 2);
        using var db = new SolidBrush(Theme.Cyan);
        using var dg = new SolidBrush(Color.FromArgb(50, 0, 240, 255));
        void Diamond(float x, float y)
        {
            g.FillEllipse(dg, x - 6, y - 6, 12, 12);
            g.FillPolygon(db, [new PointF(x, y - 3.4f), new PointF(x + 2.4f, y), new PointF(x, y + 3.4f), new PointF(x - 2.4f, y)]);
        }
        for (int k = 1; k < 8; k++)
        {
            if (k == 4) continue;
            Diamond(W * k / 8f, -dOff);
            Diamond(W * k / 8f, H + dOff);
        }
        for (int k = 1; k < 4; k++)
        {
            Diamond(-dOff, H * k / 4f);
            Diamond(W + dOff, H * k / 4f);
        }
    }


    /// <summary>Snooker markings: baulk line, the D and the six colour spots.</summary>
    public static void DrawSnookerMarks(Graphics g, Color line, Color spot)
    {
        float bx = (float)Table.BaulkX, dr = (float)Table.DRadius, cy = (float)(Table.Height / 2);
        using var pen = new Pen(line, 1.1f);
        g.DrawLine(pen, bx, 0, bx, (float)Table.Height);
        g.DrawArc(pen, bx - dr, cy - dr, dr * 2, dr * 2, 90, 180);
        using var sb = new SolidBrush(spot);
        for (int c = SnookerRules.Yellow; c <= SnookerRules.Black; c++)
        {
            var p = SnookerRules.Spot(c);
            g.FillEllipse(sb, (float)p.X - 1.8f, (float)p.Y - 1.8f, 3.6f, 3.6f);
        }
    }

    public void Dispose() => cache?.Dispose();
}

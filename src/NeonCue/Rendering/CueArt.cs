using System.Drawing.Drawing2D;
using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>Draws a cue stick from its <see cref="CueSpec"/> colours, with tier-based glow.</summary>
static class CueArt
{
    public static Color TierColor(CueTier tier) => tier switch
    {
        CueTier.Rare => Ui.Blue,
        CueTier.Epic => Theme.Hex("#9C84D6"),
        CueTier.Legendary => Ui.Gold,
        CueTier.Mythic => Theme.Hex("#D98AB3"),
        _ => Ui.Text2,
    };

    /// <param name="unit">Size of one "pixel" in the current coordinate system (glow widths scale with it).</param>
    public static void Draw(Graphics g, PointF tip, PointF butt, float tipHalf, float buttHalf, CueSpec spec, double time, float unit, bool shadow = true)
    {
        if (Look.Realistic)
        {
            RealCue.Draw(g, tip, butt, buttHalf, spec, time, unit, shadow);
            return;
        }
        float dx = butt.X - tip.X, dy = butt.Y - tip.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float px = -dy / len, py = dx / len;

        PointF[] Poly(float ox, float oy) =>
        [
            new(tip.X + px * tipHalf + ox, tip.Y + py * tipHalf + oy),
            new(butt.X + px * buttHalf + ox, butt.Y + py * buttHalf + oy),
            new(butt.X - px * buttHalf + ox, butt.Y - py * buttHalf + oy),
            new(tip.X - px * tipHalf + ox, tip.Y - py * tipHalf + oy),
        ];

        if (shadow)
            using (var sb = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillPolygon(sb, Poly(6 * unit, 9 * unit));

        Color shaft = Theme.Hex(spec.Shaft), accent = Theme.Hex(spec.Accent), buttC = Theme.Hex(spec.Butt), glow = Theme.Hex(spec.Glow);
        var pts = Poly(0, 0);
        using (var lg = new LinearGradientBrush(tip, butt, shaft, buttC))
        {
            lg.InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    Theme.Hex("#5AD8FF"), Color.White, Color.White, Theme.Mix(shaft, Color.White, 0.25), shaft,
                    accent, accent, buttC, buttC, accent, buttC, Theme.Mix(buttC, Color.Black, 0.5),
                ],
                Positions = [0f, 0.012f, 0.03f, 0.045f, 0.57f, 0.59f, 0.63f, 0.66f, 0.79f, 0.81f, 0.83f, 1f],
            };
            g.FillPolygon(lg, pts);
        }

        // Gold / diamond rings on the butt of high-tier cues.
        if (spec.Tier >= CueTier.Legendary)
        {
            using var ringPen = new Pen(Theme.A(accent, 230), Math.Max(0.6f, buttHalf * 0.25f));
            foreach (var f in new[] { 0.7f, 0.74f, 0.9f })
            {
                float cx = tip.X + dx * f, cy = tip.Y + dy * f;
                float hw = tipHalf + (buttHalf - tipHalf) * f;
                g.DrawLine(ringPen, cx + px * hw, cy + py * hw, cx - px * hw, cy - py * hw);
            }
        }

        // Tier glow.
        int alpha = spec.Tier switch { CueTier.Common => 10, CueTier.Rare => 18, CueTier.Epic => 26, CueTier.Legendary => 34, _ => 42 };
        using var outline = new GraphicsPath();
        outline.AddPolygon(pts);
        for (int i = 3; i >= 1; i--)
        {
            using var pen = new Pen(Theme.A(glow, alpha), i * 2.2f * unit) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, outline);
        }

        // A spark of light that travels along legendary shafts.
        if (spec.Tier >= CueTier.Legendary)
        {
            float f = 0.06f + (float)((time * 0.45) % 1.0) * 0.9f;
            float sx = tip.X + dx * f, sy = tip.Y + dy * f;
            float r = (tipHalf + (buttHalf - tipHalf) * f) * 2.2f;
            using var sp = new GraphicsPath();
            sp.AddEllipse(sx - r, sy - r, r * 2, r * 2);
            using var sbr = new PathGradientBrush(sp) { CenterColor = Theme.A(Color.White, 220), SurroundColors = [Theme.A(glow, 0)] };
            g.FillPath(sbr, sp);
        }
    }
}

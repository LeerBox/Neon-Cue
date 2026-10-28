using System.Drawing.Drawing2D;

namespace NeonCue.Rendering;

/// <summary>
/// One icon set. Every mark is drawn on a 16-point grid with the same round stroke,
/// then placed on the text's vertical centre. A few glyphs are nudged so they
/// look centred, not merely boxed.
/// </summary>
enum Glyph
{
    UpDown, LeftRight, Play, Cue, Rack, Corners,
    Score, Pause, Restore, Aim, Sound, Coin,
    ChevronLeft, ChevronRight,
}

static class Glyphs
{
    public static void Draw(Graphics g, Glyph id, float cx, float cy, float size, Color color)
    {
        cy += Nudge(id) * (size / 16f);
        float s = size / 16f;
        using var pen = new Pen(color, Math.Max(1.05f, size * 0.092f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        PointF P(float x, float y) => new(cx + (x - 8f) * s, cy + (y - 8f) * s);

        switch (id)
        {
            case Glyph.UpDown:
                g.DrawLine(pen, P(8, 3.6f), P(8, 12.4f));
                Head(g, pen, P(8, 3.6f), -MathF.PI / 2, 2.15f * s);
                Head(g, pen, P(8, 12.4f), MathF.PI / 2, 2.15f * s);
                break;
            case Glyph.LeftRight:
                g.DrawLine(pen, P(3.6f, 8), P(12.4f, 8));
                Head(g, pen, P(3.6f, 8), MathF.PI, 2.15f * s);
                Head(g, pen, P(12.4f, 8), 0, 2.15f * s);
                break;
            case Glyph.Play:
                g.DrawPolygon(pen, [P(6.4f, 4.6f), P(11.6f, 8f), P(6.4f, 11.4f)]);
                break;
            case Glyph.Cue:
                using (var butt = new Pen(color, Math.Max(1.6f, size * 0.16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(butt, P(3.1f, 12.8f), P(6.4f, 9.5f));
                g.DrawLine(pen, P(6.2f, 9.7f), P(11.6f, 4.3f));
                using (var tip = new SolidBrush(color))
                    g.FillEllipse(tip, P(12.6f, 3.6f).X - 1.45f * s, P(12.6f, 3.6f).Y - 1.45f * s, 2.9f * s, 2.9f * s);
                break;
            case Glyph.Rack:
                Dot(g, color, P(8, 4.1f), 1.55f * s);
                Dot(g, color, P(4.7f, 11.3f), 1.55f * s);
                Dot(g, color, P(11.3f, 11.3f), 1.55f * s);
                break;
            case Glyph.Corners:
                Corner(g, pen, P(4.2f, 4.2f), 1, 1, s);
                Corner(g, pen, P(11.8f, 4.2f), -1, 1, s);
                Corner(g, pen, P(4.2f, 11.8f), 1, -1, s);
                Corner(g, pen, P(11.8f, 11.8f), -1, -1, s);
                break;
            case Glyph.Score:
                g.DrawRectangle(pen, Rectangle(P(3.4f, 3.6f), P(12.6f, 12.4f)));
                g.DrawLine(pen, P(8, 3.6f), P(8, 12.4f));
                break;
            case Glyph.Pause:
                float bw = Math.Max(1.15f, size * 0.1f);
                using (var bar = new SolidBrush(color))
                {
                    g.FillRectangle(bar, cx - 2.15f * s - bw / 2, cy - 3.6f * s, bw, 7.2f * s);
                    g.FillRectangle(bar, cx + 2.15f * s - bw / 2, cy - 3.6f * s, bw, 7.2f * s);
                }
                break;
            case Glyph.Restore:
            {
                float r = 4.7f * s;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40, 280);
                float a = (-40 + 280) * MathF.PI / 180f;
                var tip = new PointF(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
                Head(g, pen, tip, a + MathF.PI / 2, 2.4f * s);
                break;
            }
            case Glyph.Aim:
                g.DrawLine(pen, P(3.4f, 12.2f), P(10.2f, 5.4f));
                g.DrawEllipse(pen, P(11.6f, 4.2f).X - 1.7f * s, P(11.6f, 4.2f).Y - 1.7f * s, 3.4f * s, 3.4f * s);
                break;
            case Glyph.Sound:
                g.DrawLines(pen, [P(3.6f, 6.5f), P(6.2f, 6.5f), P(10.1f, 3.8f), P(10.1f, 12.2f), P(6.2f, 9.5f), P(3.6f, 9.5f), P(3.6f, 6.5f)]);
                g.DrawArc(pen, cx + 1.6f * s, cy - 2.3f * s, 3.6f * s, 4.6f * s, -55, 110);
                break;
            case Glyph.Coin:
                g.DrawEllipse(pen, cx - 5.2f * s, cy - 5.2f * s, 10.4f * s, 10.4f * s);
                g.DrawLine(pen, P(5.2f, 8), P(10.8f, 8));
                break;
            case Glyph.ChevronLeft:
                g.DrawLines(pen, [P(10.4f, 4.3f), P(5.5f, 8f), P(10.4f, 11.7f)]);
                break;
            case Glyph.ChevronRight:
                g.DrawLines(pen, [P(5.6f, 4.3f), P(10.5f, 8f), P(5.6f, 11.7f)]);
                break;
        }
    }

    /// <summary>Positive moves the glyph down. Chosen so the mark sits on the type, not in its box.</summary>
    static float Nudge(Glyph id) => id switch
    {
        Glyph.Play => 0.45f,
        Glyph.ChevronLeft or Glyph.ChevronRight => -0.35f,
        Glyph.UpDown => 0.2f,
        Glyph.Rack => 0.35f,
        _ => 0,
    };

    static void Head(Graphics g, Pen pen, PointF tip, float pointing, float len)
    {
        const float wing = 0.48f;
        float back = pointing + MathF.PI;
        g.DrawLine(pen, tip, new PointF(tip.X + len * MathF.Cos(back + wing), tip.Y + len * MathF.Sin(back + wing)));
        g.DrawLine(pen, tip, new PointF(tip.X + len * MathF.Cos(back - wing), tip.Y + len * MathF.Sin(back - wing)));
    }

    static void Dot(Graphics g, Color color, PointF c, float r)
    {
        using var br = new SolidBrush(color);
        g.FillEllipse(br, c.X - r, c.Y - r, r * 2, r * 2);
    }

    static void Corner(Graphics g, Pen pen, PointF o, float dx, float dy, float s)
    {
        float len = 2.6f * s;
        g.DrawLines(pen, [new PointF(o.X + dx * len, o.Y), o, new PointF(o.X, o.Y + dy * len)]);
    }

    static RectangleF Rectangle(PointF a, PointF b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
}

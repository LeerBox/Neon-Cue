using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>Pre-rendered, shaded ball bitmaps for the current screen scale.</summary>
sealed class BallSprites : IDisposable
{
    readonly Bitmap?[] sprites = new Bitmap?[16];
    float radius = -1;

    public float Radius => radius;

    public void Ensure(float pixelRadius)
    {
        if (Math.Abs(pixelRadius - radius) < 0.01f) return;
        Dispose();
        radius = pixelRadius;
        for (int i = 0; i < sprites.Length; i++) sprites[i] = Make(i, pixelRadius);
    }

    public Bitmap this[int n] => sprites[n]!;

    public void Draw(Graphics g, int number, PointF center, float scale = 1f)
    {
        var bmp = sprites[number];
        if (bmp == null) return;
        float w = bmp.Width * scale, h = bmp.Height * scale;
        g.DrawImage(bmp, center.X - w / 2, center.Y - h / 2, w, h);
    }

    public static Bitmap Make(int n, float r)
    {
        int size = (int)Math.Ceiling(r * 2) + 2;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float c = size / 2f;
        var rect = new RectangleF(c - r, c - r, 2 * r, 2 * r);
        using var circle = new GraphicsPath();
        circle.AddEllipse(rect);
        Color col = Theme.BallColor(n);

        if (n >= 9)
        {
            using (var white = new SolidBrush(Theme.Hex("#F3F1EA"))) g.FillEllipse(white, rect);
            g.SetClip(circle);
            using (var band = new SolidBrush(col)) g.FillRectangle(band, c - r, c - r * 0.56f, 2 * r, r * 1.12f);
            g.ResetClip();
        }
        else
        {
            using var b = new SolidBrush(col);
            g.FillEllipse(b, rect);
        }

        if (n > 0)
        {
            float dr = r * 0.5f;
            using (var disc = new SolidBrush(Color.FromArgb(250, 250, 248))) g.FillEllipse(disc, c - dr, c - dr, 2 * dr, 2 * dr);
            using var font = new Font(Theme.Family, Math.Max(4, r * (n >= 10 ? 0.52f : 0.62f)), FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(Color.FromArgb(24, 22, 30));
            g.DrawString(n.ToString(), font, tb, new RectangleF(c - dr, c - dr + r * 0.03f, 2 * dr, 2 * dr), sf);
        }

        // Spherical shading.
        using (var shade = new PathGradientBrush(circle))
        {
            shade.CenterPoint = new PointF(c - r * 0.35f, c - r * 0.4f);
            shade.CenterColor = Color.FromArgb(0, 0, 0, 0);
            shade.SurroundColors = [Color.FromArgb(150, 0, 0, 10)];
            shade.FocusScales = new PointF(0.3f, 0.3f);
            g.FillPath(shade, circle);
        }

        // Specular highlight.
        var hr = new RectangleF(c - r * 0.72f, c - r * 0.8f, r * 0.78f, r * 0.62f);
        using (var hp = new GraphicsPath())
        {
            hp.AddEllipse(hr);
            using var hb = new PathGradientBrush(hp)
            {
                CenterColor = Color.FromArgb(215, 255, 255, 255),
                SurroundColors = [Color.FromArgb(0, 255, 255, 255)],
            };
            g.FillPath(hb, hp);
        }

        using var rim = new Pen(Color.FromArgb(90, 0, 0, 0), Math.Max(1, r * 0.06f));
        g.DrawEllipse(rim, rect);
        return bmp;
    }

    public void Dispose()
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i]?.Dispose();
            sprites[i] = null;
        }
    }
}

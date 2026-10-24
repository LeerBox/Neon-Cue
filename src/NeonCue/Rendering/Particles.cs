using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>Neon sparks and rings, simulated in table units.</summary>
sealed class Particles
{
    struct Spark
    {
        public Vec2 Pos, Vel;
        public double Life, MaxLife;
        public Color Color;
        public float Size;
    }

    struct Ring
    {
        public Vec2 Pos;
        public double Life, MaxLife;
        public Color Color;
        public float Radius;
    }

    readonly List<Spark> sparks = new();
    readonly List<Ring> rings = new();
    readonly Random rng = new();

    public void Burst(Vec2 at, Color color, int count, double speed, float size = 2.4f)
    {
        for (int i = 0; i < count; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double sp = speed * (0.35 + rng.NextDouble() * 0.9);
            double life = 0.35 + rng.NextDouble() * 0.45;
            sparks.Add(new Spark { Pos = at, Vel = Vec2.FromAngle(a) * sp, Life = life, MaxLife = life, Color = color, Size = size * (0.6f + (float)rng.NextDouble() * 0.8f) });
        }
    }

    public void Pulse(Vec2 at, Color color, float radius, double life = 0.45) =>
        rings.Add(new Ring { Pos = at, Life = life, MaxLife = life, Color = color, Radius = radius });

    public void Update(double dt)
    {
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            s.Life -= dt;
            if (s.Life <= 0) { sparks.RemoveAt(i); continue; }
            s.Pos += s.Vel * dt;
            s.Vel *= Math.Max(0, 1 - 3.2 * dt);
            sparks[i] = s;
        }
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i];
            r.Life -= dt;
            if (r.Life <= 0) { rings.RemoveAt(i); continue; }
            rings[i] = r;
        }
    }

    /// <summary>Draws in world coordinates (caller applies the table transform).</summary>
    public void Draw(Graphics g)
    {
        foreach (var r in rings)
        {
            double t = 1 - r.Life / r.MaxLife;
            float rad = r.Radius * (float)(0.4 + t * 1.4);
            int alpha = (int)(200 * (1 - t));
            using var pen = new Pen(Theme.A(r.Color, alpha), 2.5f * (float)(1 - t) + 0.6f);
            g.DrawEllipse(pen, (float)r.Pos.X - rad, (float)r.Pos.Y - rad, rad * 2, rad * 2);
        }
        foreach (var s in sparks)
        {
            double t = s.Life / s.MaxLife;
            float sz = s.Size * (float)(0.5 + t * 0.5);
            using var glow = new SolidBrush(Theme.A(s.Color, (int)(70 * t)));
            g.FillEllipse(glow, (float)s.Pos.X - sz * 2, (float)s.Pos.Y - sz * 2, sz * 4, sz * 4);
            using var core = new SolidBrush(Theme.A(Theme.Mix(s.Color, Color.White, 0.5), (int)(255 * t)));
            g.FillEllipse(core, (float)s.Pos.X - sz / 2, (float)s.Pos.Y - sz / 2, sz, sz);
        }
    }
}

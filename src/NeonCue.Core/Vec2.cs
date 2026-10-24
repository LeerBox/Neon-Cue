namespace NeonCue.Core;

/// <summary>Immutable 2D vector in table units.</summary>
public readonly struct Vec2
{
    public readonly double X;
    public readonly double Y;

    public Vec2(double x, double y) { X = x; Y = y; }

    public static readonly Vec2 Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;
    public double Angle => Math.Atan2(Y, X);

    public Vec2 Normalized()
    {
        double l = Length;
        return l > 1e-12 ? new Vec2(X / l, Y / l) : Zero;
    }

    /// <summary>Rotated 90 degrees counter-clockwise (in math orientation).</summary>
    public Vec2 Perp() => new(-Y, X);

    public Vec2 Rotated(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));
    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static double DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    /// <summary>Distance from point p to segment ab.</summary>
    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        Vec2 ab = b - a;
        double len2 = ab.LengthSq;
        if (len2 < 1e-12) return Distance(p, a);
        double t = Math.Clamp(Dot(p - a, ab) / len2, 0, 1);
        return Distance(p, a + ab * t);
    }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public override string ToString() => $"({X:0.0}, {Y:0.0})";
}

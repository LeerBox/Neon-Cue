namespace NeonCue.Core;

/// <summary>
/// Where an object ball's centre must travel to fall in a pocket without touching a jaw.
/// The point is not the middle of the hole. It is a point inside the opening that the
/// whole ball can reach from where it is now.
/// </summary>
public static class PocketEntry
{
    /// <summary>Ball-centre clearance past each jaw tip. A hair more than the radius, so a true line does not graze.</summary>
    static double Need => Table.R * 1.08;

    public static bool TryGet(Pocket pocket, Vec2 from, out Vec2 aim)
    {
        aim = pocket.Center;
        if (Vec2.DistanceSq(from, pocket.Center) <= pocket.CaptureRadius * pocket.CaptureRadius * 0.81)
            return true;

        Jaws(pocket, out Vec2 jawA, out Vec2 jawB);
        Vec2 toHole = pocket.Center - from;
        if (toHole.LengthSq < 4) return false;
        double basis = toHole.Angle;

        double best = double.NegativeInfinity;
        bool found = false;
        for (int i = -40; i <= 40; i++)
        {
            Vec2 dir = Vec2.FromAngle(basis + i * 0.015);
            if (Vec2.Dot(dir, -pocket.Inward) < 0.2) continue;
            if (!FirstHit(from, dir, pocket.Center, pocket.CaptureRadius * 0.78, out Vec2 hit)) continue;
            double clear = Math.Min(
                Vec2.DistanceToSegment(jawA, from, hit),
                Vec2.DistanceToSegment(jawB, from, hit));
            if (clear < Need) continue;
            // More clearance is safer. A smaller turn from the natural line is easier to aim.
            double score = clear - Math.Abs(i) * 0.02 * Table.R;
            if (score > best)
            {
                best = score;
                aim = hit;
                found = true;
            }
        }
        return found;
    }

    static void Jaws(Pocket pocket, out Vec2 a, out Vec2 b)
    {
        if (!pocket.IsCorner)
        {
            double y = pocket.Inward.Y > 0 ? 0 : Table.Height;
            double x = Table.Width / 2;
            a = new Vec2(x - Table.SideGap, y);
            b = new Vec2(x + Table.SideGap, y);
            return;
        }
        double cx = pocket.Inward.X > 0 ? 0 : Table.Width;
        double cy = pocket.Inward.Y > 0 ? 0 : Table.Height;
        double sx = Math.Sign(pocket.Inward.X);
        double sy = Math.Sign(pocket.Inward.Y);
        a = new Vec2(cx + sx * Table.CornerGap, cy);
        b = new Vec2(cx, cy + sy * Table.CornerGap);
    }

    /// <summary>First point where the ray enters the circle, if it starts outside and is heading toward it.</summary>
    static bool FirstHit(Vec2 from, Vec2 dir, Vec2 center, double radius, out Vec2 hit)
    {
        hit = default;
        Vec2 w = from - center;
        double b = 2 * Vec2.Dot(w, dir);
        double c = w.LengthSq - radius * radius;
        double disc = b * b - 4 * c;
        if (disc < 0) return false;
        double root = Math.Sqrt(disc);
        double t = (-b - root) / 2;
        if (t < Table.R) t = (-b + root) / 2;
        if (t < Table.R) return false;
        hit = from + dir * t;
        return true;
    }
}

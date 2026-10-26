namespace NeonCue.Core;

/// <summary>Receives real-time physics events (sound, particles). AI simulations pass null.</summary>
public interface IPhysicsListener
{
    void BallCollision(Ball a, Ball b, double impactSpeed);
    void CushionHit(Ball ball, double impactSpeed);
    void BallPocketed(Ball ball, Pocket pocket);
}

/// <summary>Everything the rules need to know about one shot.</summary>
public sealed class ShotEvents
{
    public int FirstContact = -1;
    public readonly List<int> Pocketed = new();
    /// <summary>Object balls that touched a cushion during the shot (break rule).</summary>
    public readonly HashSet<int> RailBalls = new();
    /// <summary>Any ball touched a cushion after the cue ball's first contact.</summary>
    public bool RailAfterContact;

    public bool CueScratched => Pocketed.Contains(0);
    public IEnumerable<int> PocketedObjects => Pocketed.Where(n => n != 0);
}

/// <summary>
/// Deterministic fixed-step billiards physics. The same code drives the live table
/// and the AI's look-ahead simulations, so a planned shot plays out exactly as predicted.
/// </summary>
public static class Physics
{
    public const double Dt = 1.0 / 480.0;
    public const double MaxShotSpeed = 2200;
    public const double RollingFriction = 135;   // units/s^2
    public const double LinearDamping = 0.22;    // 1/s
    public const double BallRestitution = 0.95;
    public const double CushionRestitution = 0.76;
    public const double StopSpeed = 2.0;
    public const double MaxSimTime = 45;

    static double R => Table.R;
    static double TwoR => 2 * Table.R;
    static double FourR2 => 4 * Table.R * Table.R;

    public static void Strike(Ball cue, double angle, double power, double spinSide, double spinTop, CueSpec? stick = null, bool isBreak = false)
    {
        stick ??= CueCatalog.Standard;
        power = Math.Clamp(power, 0, 1);
        double speed = power * MaxShotSpeed * stick.Power * (isBreak ? stick.BreakBoost : 1);
        cue.Vel = Vec2.FromAngle(LaunchAngle(angle, spinSide, stick)) * speed;
        cue.SpinSide = Math.Clamp(spinSide * stick.Spin, -1.5, 1.5);
        cue.SpinTop = Math.Clamp(spinTop * stick.Spin, -1.5, 1.5);
        cue.SpinUsed = false;
    }

    /// <summary>Direction the cue ball actually leaves in: side spin squirts it away from the english.</summary>
    public static double LaunchAngle(double aimAngle, double spinSide, CueSpec? stick) =>
        aimAngle - spinSide * (stick ?? CueCatalog.Standard).Squirt * Math.PI / 180;

    /// <summary>Simulates until every ball rests. Mutates the balls.</summary>
    public static ShotEvents Simulate(Ball[] balls)
    {
        var ev = new ShotEvents();
        double t = 0;
        while (Step(balls, ev, null))
        {
            t += Dt;
            if (t > MaxSimTime)
            {
                foreach (var b in balls) b.Vel = Vec2.Zero;
                break;
            }
        }
        return ev;
    }

    /// <summary>Advances one fixed step. Returns false when nothing was moving.</summary>
    public static bool Step(Ball[] balls, ShotEvents ev, IPhysicsListener? listener)
    {
        int n = balls.Length;
        bool any = false;

        // Integrate with rolling friction and a little velocity-proportional damping.
        for (int i = 0; i < n; i++)
        {
            var b = balls[i];
            if (b.Pocketed || (b.Vel.X == 0 && b.Vel.Y == 0)) continue;
            double sp = b.Vel.Length;
            double ns = sp - (RollingFriction + sp * LinearDamping) * Dt;
            if (ns <= StopSpeed)
            {
                b.Vel = Vec2.Zero;
                b.SpinTop = 0;
                b.SpinSide = 0;
                // A ball that dies with most of itself over a pocket opening falls in.
                double lip = 0.6 * R;
                if (b.Pos.X < lip || b.Pos.X > Table.Width - lip || b.Pos.Y < lip || b.Pos.Y > Table.Height - lip)
                    Drop(b, Table.NearestPocket(b.Pos), ev, listener);
                continue;
            }
            b.Vel *= ns / sp;
            b.Pos += b.Vel * Dt;
            if (b.SpinTop != 0) b.SpinTop *= 1 - ns * Dt / 1600;
            if (b.SpinSide != 0) b.SpinSide *= 1 - ns * Dt / 4000;
            any = true;
        }
        if (!any) return false;

        // Ball-ball collisions.
        for (int i = 0; i < n; i++)
        {
            var a = balls[i];
            if (a.Pocketed) continue;
            for (int j = i + 1; j < n; j++)
            {
                var b = balls[j];
                if (b.Pocketed) continue;
                if (!a.IsMoving && !b.IsMoving) continue;
                double dx = b.Pos.X - a.Pos.X;
                if (dx >= TwoR || dx <= -TwoR) continue;
                double dy = b.Pos.Y - a.Pos.Y;
                if (dy >= TwoR || dy <= -TwoR) continue;
                if (dx * dx + dy * dy >= FourR2) continue;
                Collide(a, b, ev, listener);
            }
        }

        // Cushions and pockets: only balls near the edge can touch either.
        for (int i = 0; i < n; i++)
        {
            var b = balls[i];
            if (b.Pocketed || !b.IsMoving) continue;
            double x = b.Pos.X, y = b.Pos.Y;
            if (x > R + 1 && x < Table.Width - R - 1 && y > R + 1 && y < Table.Height - R - 1) continue;

            if (CheckPocket(b, ev, listener)) continue;

            foreach (var seg in Table.Cushions)
            {
                Vec2 cp = seg.Closest(b.Pos);
                Vec2 diff = b.Pos - cp;
                double d2 = diff.LengthSq;
                if (d2 >= R * R || d2 < 1e-12) continue;
                double dist = Math.Sqrt(d2);
                Vec2 nrm = diff / dist;
                b.Pos = cp + nrm * R;
                double vn = Vec2.Dot(b.Vel, nrm);
                if (vn >= 0) continue;
                Vec2 vt = b.Vel - nrm * vn;
                // Side spin grips the rubber and kicks the ball along the cushion.
                Vec2 kick = nrm.Perp() * (b.SpinSide * 0.32 * -vn);
                b.Vel = nrm * (-vn * CushionRestitution) + vt * 0.96 + kick;
                b.SpinSide *= 0.55;
                if (ev.FirstContact >= 0) ev.RailAfterContact = true;
                if (b.Number != 0) ev.RailBalls.Add(b.Number);
                listener?.CushionHit(b, -vn);
            }

            CheckPocket(b, ev, listener);
        }

        return true;
    }

    static bool CheckPocket(Ball b, ShotEvents ev, IPhysicsListener? listener)
    {
        double x = b.Pos.X, y = b.Pos.Y;
        double m = 0.6 * R;
        Pocket? hit = null;
        if (x < -m || x > Table.Width + m || y < -m || y > Table.Height + m)
        {
            hit = Table.NearestPocket(b.Pos);
        }
        else
        {
            foreach (var p in Table.Pockets)
            {
                if (Vec2.DistanceSq(b.Pos, p.Center) < p.CaptureRadius * p.CaptureRadius) { hit = p; break; }
            }
        }
        if (hit == null) return false;
        Drop(b, hit, ev, listener);
        return true;
    }

    static void Drop(Ball b, Pocket pocket, ShotEvents ev, IPhysicsListener? listener)
    {
        b.Pocketed = true;
        b.Vel = Vec2.Zero;
        b.SpinTop = 0;
        b.SpinSide = 0;
        ev.Pocketed.Add(b.Number);
        listener?.BallPocketed(b, pocket);
    }

    static void Collide(Ball a, Ball b, ShotEvents ev, IPhysicsListener? listener)
    {
        Vec2 d = b.Pos - a.Pos;
        Vec2 rv = b.Vel - a.Vel;
        double dDotRv = Vec2.Dot(d, rv);

        if (dDotRv < 0)
        {
            // Rewind both balls to the exact moment of contact for an accurate normal.
            double A = rv.LengthSq;
            double C = d.LengthSq - FourR2;
            double disc = dDotRv * dDotRv - A * C;
            double t = A > 1e-9 ? (dDotRv + Math.Sqrt(Math.Max(0, disc))) / A : 0;
            t = Math.Clamp(t, 0, Dt * 2);
            a.Pos -= a.Vel * t;
            b.Pos -= b.Vel * t;

            Vec2 nrm = (b.Pos - a.Pos).Normalized();
            double vn = Vec2.Dot(a.Vel - b.Vel, nrm);
            if (vn > 0)
            {
                Ball? cue = a.Number == 0 ? a : b.Number == 0 ? b : null;
                Vec2 cueBefore = cue?.Vel ?? Vec2.Zero;
                double j = (1 + BallRestitution) * 0.5 * vn;
                a.Vel -= nrm * j;
                b.Vel += nrm * j;

                if (cue != null)
                {
                    if (ev.FirstContact < 0) ev.FirstContact = (cue == a ? b : a).Number;
                    if (!cue.SpinUsed)
                    {
                        cue.SpinUsed = true;
                        // Follow/draw: the spinning cue ball keeps (or reverses) part of the
                        // momentum it just handed over, along its original line.
                        if (cue.SpinTop != 0)
                            cue.Vel += cueBefore.Normalized() * (cue.SpinTop * 0.72 * j);
                        cue.SpinTop = 0;
                    }
                }
                listener?.BallCollision(a, b, vn);
            }

            a.Pos += a.Vel * t;
            b.Pos += b.Vel * t;
        }

        // Resolve any residual overlap.
        d = b.Pos - a.Pos;
        double dist = d.Length;
        if (dist < TwoR && dist > 1e-9)
        {
            Vec2 push = d * ((TwoR - dist) / (2 * dist));
            a.Pos -= push;
            b.Pos += push;
        }
    }
}

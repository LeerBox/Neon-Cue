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
/// Deterministic billiards physics with real-world constants. Every ball carries a 3-D spin vector;
/// the cloth makes it slide, then roll (at 5/7 of its speed when struck in the centre); follow,
/// draw and stun come out of the spin itself; ball-to-ball friction produces cut- and spin-induced
/// throw; cushions grip the spinning ball, so english changes the rebound. The same code drives the
/// live table, the aim prediction and the AI, so what is predicted is exactly what happens.
/// Table frame: x right, y towards the bottom rail, z down into the cloth; unit mass.
/// </summary>
public static class Physics
{
    public const double Dt = 1.0 / 480.0;
    public const double MaxSimTime = 45;

    // Measured values for phenolic balls on worsted cloth.
    public const double MuSlide = 0.2;       // ball-cloth sliding friction
    public const double MuRoll = 0.01;       // rolling resistance
    public const double MuSpin = 0.03;       // spinning (vertical-axis) friction
    public const double BallRestitution = 0.95;
    public const double BallFriction = 0.06; // ball-ball friction (throw)
    public const double CushionRestitution = 0.75;
    public const double CushionFriction = 0.2;
    public const double StopSpeed = 2.0;     // units/s
    const double SlipEps = 1.0;              // units/s of contact slip treated as rolling

    static double R => Table.R;
    static double TwoR => 2 * Table.R;
    static double FourR2 => 4 * Table.R * Table.R;

    /// <summary>Metres per table unit (1000 units = playing length).</summary>
    public static double MetersPerUnit => Table.Kind == TableKind.Snooker ? 3.569 / 1000 : 2.54 / 1000;
    /// <summary>Gravity in table units.</summary>
    public static double G => 9.81 / MetersPerUnit;
    /// <summary>Cue-ball speed of a full-power stroke (8 m/s).</summary>
    public static double MaxShotSpeed => 8.0 / MetersPerUnit;

    /// <summary>Power (0..1, as on the power bar) to fraction of full speed: fine control on soft shots.</summary>
    public static double SpeedFraction(double power) => 0.35 * power + 0.65 * power * power;
    public static double PowerForSpeedFraction(double f) =>
        Math.Clamp((-0.35 + Math.Sqrt(0.35 * 0.35 + 4 * 0.65 * Math.Clamp(f, 0, 1))) / (2 * 0.65), 0, 1);

    /// <summary>Launch speed that makes a ball struck dead-centre travel <paramref name="distance"/> before stopping.</summary>
    public static double SpeedToTravel(double distance) =>
        Math.Sqrt(2 * G * Math.Max(0, distance) / (24.0 / (49 * MuSlide) + 25.0 / (49 * MuRoll)));

    /// <summary>
    /// Strikes the cue ball. <paramref name="spinSide"/> and <paramref name="spinTop"/> (-1..1) are the
    /// tip offset across the ball (scaled by the cue's spin rating, at most half a radius); the spin
    /// imparted is 5·v·offset / 2R, the textbook result for a level cue.
    /// </summary>
    public static void Strike(Ball cue, double angle, double power, double spinSide, double spinTop, CueSpec? stick = null, bool isBreak = false)
    {
        stick ??= CueCatalog.Standard;
        power = Math.Clamp(power, 0, 1);
        double v = SpeedFraction(power) * MaxShotSpeed * stick.Power * (isBreak ? stick.BreakBoost : 1);
        Vec2 d = Vec2.FromAngle(LaunchAngle(angle, spinSide, stick));
        double a = Math.Clamp(spinSide * stick.Spin, -1, 1) * 0.5;
        double b = Math.Clamp(spinTop * stick.Spin, -1, 1) * 0.5;
        double k = 5 * v / (2 * R);
        cue.Vel = d * v;
        cue.Wx = d.Y * k * b;
        cue.Wy = -d.X * k * b;
        cue.Wz = -k * a; // right-hand english spins the ball anticlockwise seen from above
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
                foreach (var b in balls) b.Stop();
                break;
            }
        }
        return ev;
    }

    /// <summary>Advances one fixed step (sub-stepped when balls are fast). Returns false when nothing moved.</summary>
    public static bool Step(Ball[] balls, ShotEvents ev, IPhysicsListener? listener)
    {
        double maxV = 0;
        bool any = false;
        foreach (var b in balls)
        {
            if (!b.IsMoving) continue;
            any = true;
            maxV = Math.Max(maxV, b.Vel.Length);
        }
        if (!any) return false;

        // Never let a ball travel more than 0.4 R in one sub-step.
        int sub = Math.Max(1, (int)Math.Ceiling(maxV * Dt / (0.4 * R)));
        double h = Dt / sub;
        for (int k = 0; k < sub; k++) SubStep(balls, ev, listener, h);
        return true;
    }

    static void SubStep(Ball[] balls, ShotEvents ev, IPhysicsListener? listener, double h)
    {
        int n = balls.Length;
        for (int i = 0; i < n; i++)
        {
            var b = balls[i];
            if (b.Pocketed) continue;
            if (b.IsMoving) Integrate(b, h, ev, listener);
            else if (b.Wz != 0) SpinDown(b, h);
        }

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
                Collide(a, b, h, ev, listener);
            }
        }

        double r = R;
        for (int i = 0; i < n; i++)
        {
            var b = balls[i];
            if (b.Pocketed || !b.IsMoving) continue;
            double x = b.Pos.X, y = b.Pos.Y;
            if (x > r + 1 && x < Table.Width - r - 1 && y > r + 1 && y < Table.Height - r - 1) continue;
            if (CheckPocket(b, ev, listener)) continue;
            foreach (var seg in Table.Cushions) CushionContact(b, seg, ev, listener);
            CheckPocket(b, ev, listener);
        }
    }

    /// <summary>Cloth friction: sliding (contact point slips) or rolling, plus spin about the vertical.</summary>
    static void Integrate(Ball b, double h, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R, g = G;
        double vx = b.Vel.X, vy = b.Vel.Y;
        // Velocity of the point touching the cloth: v + w x (0,0,R).
        double ux = vx + r * b.Wy, uy = vy - r * b.Wx;
        double us = Math.Sqrt(ux * ux + uy * uy);

        if (us > SlipEps)
        {
            double dv = MuSlide * g * h;
            if (3.5 * dv >= us)
            {
                // Slip ends inside this step: settle exactly into natural roll.
                double f = us / 3.5;
                vx -= ux / us * f;
                vy -= uy / us * f;
                b.Wx = vy / r;
                b.Wy = -vx / r;
            }
            else
            {
                vx -= ux / us * dv;
                vy -= uy / us * dv;
                double dw = 5 * MuSlide * g / (2 * r) * h;
                b.Wx += uy / us * dw;
                b.Wy -= ux / us * dw;
            }
        }
        else
        {
            double sp = Math.Sqrt(vx * vx + vy * vy);
            double dv = MuRoll * g * h;
            if (sp <= Math.Max(dv, StopSpeed * h * 60))
            {
                b.Vel = Vec2.Zero;
                b.Wx = b.Wy = 0;
                // A ball that dies with most of itself over a pocket opening falls in.
                double lip = 0.6 * r;
                if (b.Pos.X < lip || b.Pos.X > Table.Width - lip || b.Pos.Y < lip || b.Pos.Y > Table.Height - lip)
                    Drop(b, Table.NearestPocket(b.Pos), ev, listener);
                return;
            }
            double k = (sp - dv) / sp;
            vx *= k;
            vy *= k;
            b.Wx = vy / r;
            b.Wy = -vx / r;
        }

        SpinDown(b, h);
        b.Vel = new Vec2(vx, vy);
        b.Pos += b.Vel * h;
    }

    static void SpinDown(Ball b, double h)
    {
        if (b.Wz == 0) return;
        double dz = 5 * MuSpin * G / (2 * R) * h;
        b.Wz = Math.Abs(b.Wz) <= dz ? 0 : b.Wz - Math.Sign(b.Wz) * dz;
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
        b.Stop();
        ev.Pocketed.Add(b.Number);
        listener?.BallPocketed(b, pocket);
    }

    /// <summary>Cushion: restitution on the normal, friction at the contact point (english grips the rubber).</summary>
    static void CushionContact(Ball b, Segment seg, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R;
        Vec2 cp = seg.Closest(b.Pos);
        Vec2 diff = b.Pos - cp;
        double d2 = diff.LengthSq;
        if (d2 >= r * r || d2 < 1e-12) return;
        double dist = Math.Sqrt(d2);
        Vec2 n = diff / dist;           // into the table
        b.Pos = cp + n * r;
        double vn = Vec2.Dot(b.Vel, n);
        if (vn >= 0) return;

        double jn = (1 + CushionRestitution) * -vn;
        Vec2 t = new(-n.Y, n.X);
        Vec2 v = b.Vel + n * jn;

        // Tangential slip of the contact point (r = -R n): v.t - R wz.
        double st = Vec2.Dot(v, t) - r * b.Wz;
        double jt = -Math.Sign(st) * Math.Min(CushionFriction * jn, Math.Abs(st) / 3.5);
        v += t * jt;
        b.Wz += -2.5 * jt / r;

        // The rubber also scrubs top/back spin (vertical slip at the contact point).
        double sz = -r * (b.Wx * n.Y - b.Wy * n.X);
        double jz = -Math.Sign(sz) * Math.Min(CushionFriction * jn, Math.Abs(sz) / 2.5);
        b.Wx += -2.5 / r * jz * n.Y;
        b.Wy += 2.5 / r * jz * n.X;

        b.Vel = v;
        if (ev.FirstContact >= 0) ev.RailAfterContact = true;
        if (b.Number != 0) ev.RailBalls.Add(b.Number);
        listener?.CushionHit(b, -vn);
    }

    /// <summary>Ball-ball: exact contact time, normal restitution, then friction at the contact point (throw).</summary>
    static void Collide(Ball a, Ball b, double h, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R;
        Vec2 d = b.Pos - a.Pos;
        Vec2 rv = b.Vel - a.Vel;
        double dDotRv = Vec2.Dot(d, rv);

        if (dDotRv < 0)
        {
            double A = rv.LengthSq;
            double C = d.LengthSq - FourR2;
            double disc = dDotRv * dDotRv - A * C;
            double tb = A > 1e-9 ? (dDotRv + Math.Sqrt(Math.Max(0, disc))) / A : 0;
            tb = Math.Clamp(tb, 0, h * 2);
            a.Pos -= a.Vel * tb;
            b.Pos -= b.Vel * tb;

            Vec2 n = (b.Pos - a.Pos).Normalized();
            double vn = Vec2.Dot(a.Vel - b.Vel, n);
            if (vn > 0)
            {
                double jn = (1 + BallRestitution) * 0.5 * vn;
                a.Vel -= n * jn;
                b.Vel += n * jn;

                // Friction between the balls: horizontal (throw) and vertical (spin transfer) slip.
                Vec2 t = new(-n.Y, n.X);
                double st = Vec2.Dot(a.Vel - b.Vel, t) + r * a.Wz + r * b.Wz;
                double sz = r * (a.Wx * n.Y - a.Wy * n.X) + r * (b.Wx * n.Y - b.Wy * n.X);
                double slip = Math.Sqrt(st * st + sz * sz);
                if (slip > 1e-9)
                {
                    double j = Math.Min(BallFriction * jn, slip / 7);
                    double jt = -st / slip * j, jz = -sz / slip * j;
                    a.Vel += t * jt;
                    b.Vel -= t * jt;
                    double k = 2.5 / r;
                    double dwx = k * jz * n.Y, dwy = -k * jz * n.X, dwz = k * jt;
                    a.Wx += dwx; a.Wy += dwy; a.Wz += dwz;
                    b.Wx += dwx; b.Wy += dwy; b.Wz += dwz;
                }

                if (a.Number == 0 && ev.FirstContact < 0) ev.FirstContact = b.Number;
                else if (b.Number == 0 && ev.FirstContact < 0) ev.FirstContact = a.Number;
                listener?.BallCollision(a, b, vn);
            }

            a.Pos += a.Vel * tb;
            b.Pos += b.Vel * tb;
        }

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

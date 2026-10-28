namespace NeonCue.Core;

/// <summary>Live contact marks for the V overlay. Recording is off unless <see cref="Show"/> is set, so AI searches stay quiet.</summary>
public static class ShotTrace
{
    public static bool Show;
    public readonly struct Mark
    {
        public readonly Vec2 At;
        public readonly Vec2 Dir;
        public readonly bool Cushion;
        public Mark(Vec2 at, Vec2 dir, bool cushion) { At = at; Dir = dir; Cushion = cushion; }
    }
    public static readonly List<Mark> Marks = new();
    public static void Clear() => Marks.Clear();
    public static void Hit(Vec2 at, Vec2 dir, bool cushion)
    {
        if (!Show) return;
        Marks.Add(new Mark(at, dir, cushion));
        if (Marks.Count > 32) Marks.RemoveAt(0);
    }
}

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
/// throw; the cushion nose sits above the equator, so english, follow and draw all change the rebound.
/// The same code drives the live table, the aim prediction and the AI, so what is predicted is exactly what happens.
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
    /// <summary>Ball-ball friction falls with slip speed (Alciatore's fit to measured throw data).</summary>
    public static double BallFriction(double slipUnitsPerSecond) =>
        0.00942 + 0.108 * Math.Exp(-1.088 * slipUnitsPerSecond * MetersPerUnit);
    public const double CushionRestitution = 0.78;
    public const double CushionFriction = 0.28;
    /// <summary>The cushion nose meets the ball this far above the equator. A high nose grips follow and draw.</summary>
    const double CushionFace = 0.14;
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

    /// <summary>
    /// Power is the cue's speed as a fraction of a full stroke. The ball's launch speed is
    /// proportional to that, which is what an instantaneous cue-tip collision produces
    /// (ball speed = (1+e)·M/(M+m)·cue speed, a constant factor absorbed into <see cref="MaxShotSpeed"/>).
    /// </summary>
    public static double SpeedFraction(double power) => Math.Clamp(power, 0, 1);
    public static double PowerForSpeedFraction(double f) => Math.Clamp(f, 0, 1);

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

        // Never let a ball travel more than 0.2 R in one sub-step, so a thin cut cannot
        // pass the contact point between checks.
        int sub = Math.Max(1, (int)Math.Ceiling(maxV * Dt / (0.2 * R)));
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

        // Several passes so A striking B into C resolves both contacts. Each pair is struck at most once.
        Span<byte> seen = n <= 32 ? stackalloc byte[n * n] : new byte[n * n];
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var a = balls[i];
                if (a.Pocketed) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (seen[i * n + j] != 0) continue;
                    var b = balls[j];
                    if (b.Pocketed) continue;
                    if (!a.IsMoving && !b.IsMoving) continue;
                    double dx = b.Pos.X - a.Pos.X;
                    if (dx >= TwoR || dx <= -TwoR) continue;
                    double dy = b.Pos.Y - a.Pos.Y;
                    if (dy >= TwoR || dy <= -TwoR) continue;
                    if (dx * dx + dy * dy >= FourR2) continue;
                    Collide(a, b, h, ev, listener);
                    seen[i * n + j] = 1;
                    any = true;
                }
            }
            if (!any) break;
        }

        double r = R;
        for (int i = 0; i < n; i++)
        {
            var b = balls[i];
            if (b.Pocketed || !b.IsMoving) continue;
            double x = b.Pos.X, y = b.Pos.Y;
            if (x > r + 1 && x < Table.Width - r - 1 && y > r + 1 && y < Table.Height - r - 1) continue;
            if (CheckPocket(b, balls, ev, listener)) continue;
            foreach (var seg in Table.Cushions) CushionContact(b, seg, ev, listener);
            CheckPocket(b, balls, ev, listener);
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

    static bool CheckPocket(Ball b, Ball[] balls, ShotEvents ev, IPhysicsListener? listener)
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
        // A ball cannot fall through another ball. If they still overlap, only the one
        // closer to the hole may drop; the one behind is blocked by it.
        foreach (var o in balls)
        {
            if (o == b || o.Pocketed) continue;
            if (Vec2.DistanceSq(o.Pos, b.Pos) >= FourR2) continue;
            if (Vec2.DistanceSq(o.Pos, hit.Center) < Vec2.DistanceSq(b.Pos, hit.Center))
                return false;
        }
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

    /// <summary>
    /// Cushion nose above the equator. The normal is tilted down into the cloth, so follow and draw
    /// change the rebound angle, and side english still grips along the rail. Rubber gives back less
    /// at higher impact speed.
    /// </summary>
    static void CushionContact(Ball b, Segment seg, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R;
        Vec2 cp = seg.Closest(b.Pos);
        Vec2 diff = b.Pos - cp;
        double d2 = diff.LengthSq;
        if (d2 >= r * r || d2 < 1e-12) return;
        double dist = Math.Sqrt(d2);
        Vec3 n2 = new(diff.X / dist, diff.Y / dist, 0); // horizontal, into the table
        b.Pos = cp + new Vec2(n2.X, n2.Y) * r;

        double cos = Math.Cos(CushionFace), sin = Math.Sin(CushionFace);
        Vec3 n3 = new(n2.X * cos, n2.Y * cos, sin);
        Vec3 tangent = new(-n2.Y, n2.X, 0);
        Vec3 rpos = new(-r * cos * n2.X, -r * cos * n2.Y, -r * sin);
        double inertia = 0.4 * r * r;

        Vec3 v = new(b.Vel.X, b.Vel.Y, 0);
        Vec3 w = new(b.Wx, b.Wy, b.Wz);
        Vec3 vc = v + Vec3.Cross(w, rpos);
        double approach = Vec3.Dot(vc, n3);
        if (approach >= 0) return;

        double impact = -Vec2.Dot(b.Vel, new Vec2(n2.X, n2.Y));
        double e = CushionRestitution / (1 + 0.035 * impact * MetersPerUnit);
        e = Math.Clamp(e, 0.5, 0.84);

        Vec3 rxn = Vec3.Cross(rpos, n3);
        double kn = 1 + Vec3.Dot(rxn, rxn) / inertia;
        double jn = -(1 + e) * approach / kn;
        Apply(ref v, ref w, rpos, n3 * jn, inertia);

        vc = v + Vec3.Cross(w, rpos);
        Vec3 t2 = Vec3.Cross(n3, tangent).Normalized();
        double s1 = Vec3.Dot(vc, tangent);
        double s2 = Vec3.Dot(vc, t2);
        Vec3 rt1 = Vec3.Cross(rpos, tangent);
        Vec3 rt2 = Vec3.Cross(rpos, t2);
        double k11 = 1 + Vec3.Dot(rt1, rt1) / inertia;
        double k22 = 1 + Vec3.Dot(rt2, rt2) / inertia;
        double k12 = Vec3.Dot(rt1, rt2) / inertia;
        double det = k11 * k22 - k12 * k12;
        double j1 = 0, j2 = 0;
        if (Math.Abs(det) > 1e-12)
        {
            j1 = (-k22 * s1 + k12 * s2) / det;
            j2 = (k12 * s1 - k11 * s2) / det;
        }
        double mag = Math.Sqrt(j1 * j1 + j2 * j2);
        double cap = CushionFriction * Math.Abs(jn);
        if (mag > cap && mag > 1e-12)
        {
            double scale = cap / mag;
            j1 *= scale;
            j2 *= scale;
        }
        Apply(ref v, ref w, rpos, tangent * j1 + t2 * j2, inertia);

        b.Vel = new Vec2(v.X, v.Y);
        b.Wx = w.X;
        b.Wy = w.Y;
        b.Wz = w.Z;
        if (ev.FirstContact >= 0) ev.RailAfterContact = true;
        if (b.Number != 0) ev.RailBalls.Add(b.Number);
        listener?.CushionHit(b, impact);
        ShotTrace.Hit(b.Pos - new Vec2(n2.X, n2.Y) * R, new Vec2(n2.X, n2.Y), true);
    }

    /// <summary>Adds an impulse at the cushion contact and drops any velocity into the cloth.</summary>
    static void Apply(ref Vec3 v, ref Vec3 w, Vec3 rpos, Vec3 impulse, double inertia)
    {
        v += impulse;
        v = new Vec3(v.X, v.Y, 0);
        w += Vec3.Cross(rpos, impulse) * (1 / inertia);
    }

    readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(Dot(this, this));
        public Vec3 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? this : this * (1 / l);
        }
    }

    /// <summary>Equal-mass impulse along n (from a toward b): normal restitution, then friction at the contact point.</summary>
    static void ResolveImpact(Ball a, Ball b, Vec2 n, double vn, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R;
        double jn = (1 + BallRestitution) * 0.5 * vn;
        a.Vel -= n * jn;
        b.Vel += n * jn;

        Vec2 t = new(-n.Y, n.X);
        double st = Vec2.Dot(a.Vel - b.Vel, t) + r * a.Wz + r * b.Wz;
        double sz = r * (a.Wx * n.Y - a.Wy * n.X) + r * (b.Wx * n.Y - b.Wy * n.X);
        double slip = Math.Sqrt(st * st + sz * sz);
        if (slip > 1e-9)
        {
            double j = Math.Min(BallFriction(slip) * jn, slip / 7);
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
        ShotTrace.Hit(a.Pos + n * R, n, false);
    }

    /// <summary>Ball-ball: exact contact time, normal restitution, then friction at the contact point (throw).</summary>
    static void Collide(Ball a, Ball b, double h, ShotEvents ev, IPhysicsListener? listener)
    {
        double r = R;
        Vec2 d = b.Pos - a.Pos;
        Vec2 rv = b.Vel - a.Vel;
        double dDotRv = Vec2.Dot(d, rv);
        double dist2 = d.LengthSq;
        // Centres have crossed (deep overlap, already separating). The contact happened
        // earlier in the step and must still be resolved, or the striker keeps its speed.
        bool crossed = dDotRv > 0 && dist2 < FourR2 * 0.90 && (a.IsMoving || b.IsMoving);

        if (crossed)
        {
            Vec2 rel = a.Vel - b.Vel;
            Vec2 n = rel.LengthSq > 1e-6 ? rel.Normalized() : (a.Pos - b.Pos).Normalized();
            double vn = Vec2.Dot(rel, n);
            if (vn > 1e-6) ResolveImpact(a, b, n, vn, ev, listener);
            Vec2 mid = (a.Pos + b.Pos) * 0.5;
            a.Pos = mid - n * r;
            b.Pos = mid + n * r;
            return;
        }

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
            if (vn > 0) ResolveImpact(a, b, n, vn, ev, listener);

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

namespace NeonCue.Core;

/// <summary>
/// Corrects an aim for real-ball effects the ghost-ball picture ignores (cut- and spin-induced
/// throw, squirt). It strikes the cue ball at a lone copy of the target ball in the real physics,
/// measures where the target actually departs, and walks the aim (secant method) until the
/// target leaves exactly towards <c>aimPoint</c>.
/// </summary>
public static class ShotSolver
{
    public static double RefineAim(GameState s, int target, Vec2 aimPoint, double angle, double power,
        double side, double top, CueSpec stick, int iterations = 4)
    {
        var tb = s.Balls[target];
        double want = (aimPoint - tb.Pos).Angle;

        double Err(double a)
        {
            double dir = DepartureAngle(s, target, a, power, side, top, stick);
            return double.IsNaN(dir) ? double.NaN : Math.IEEERemainder(dir - want, 2 * Math.PI);
        }

        double a0 = angle, f0 = Err(a0);
        if (double.IsNaN(f0)) return angle;
        double best = a0, bestErr = Math.Abs(f0);
        double a1 = a0 + (f0 > 0 ? -0.002 : 0.002), f1 = Err(a1);
        for (int i = 0; i < iterations && !double.IsNaN(f1); i++)
        {
            if (Math.Abs(f1) < bestErr) { best = a1; bestErr = Math.Abs(f1); }
            if (bestErr < 1e-5 || Math.Abs(f1 - f0) < 1e-12) break;
            double step = Math.Clamp(-f1 * (a1 - a0) / (f1 - f0), -0.05, 0.05);
            a0 = a1; f0 = f1;
            a1 += step;
            f1 = Err(a1);
        }
        if (!double.IsNaN(f1) && Math.Abs(f1) < bestErr) best = a1;
        return best;
    }

    /// <summary>Direction the target ball leaves in after the cue ball strikes it (NaN on a miss).</summary>
    public static double DepartureAngle(GameState s, int target, double angle, double power, double side, double top, CueSpec stick)
    {
        var cue = s.Cue.Clone();
        cue.Stop();
        var obj = s.Balls[target].Clone();
        obj.Stop();
        var balls = new[] { cue, obj };
        Physics.Strike(cue, angle, power, side, top, stick, false);
        var ev = new ShotEvents();
        for (int k = 0; k < 480 * 6; k++)
        {
            if (!Physics.Step(balls, ev, null)) break;
            if (obj.Vel.LengthSq > 1e-6 || obj.Pocketed) return obj.Vel.LengthSq > 1e-6 ? obj.Vel.Angle : double.NaN;
        }
        return double.NaN;
    }
}

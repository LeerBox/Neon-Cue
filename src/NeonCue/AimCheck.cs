using NeonCue.Core;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>
/// The cue angle is the pointer angle, in double precision, with no dead zone and no assist write.
/// Run: NeonCue.exe --aim-check
/// </summary>
static class AimCheck
{
    sealed class Host : IHost
    {
        public AppSettings Settings { get; } = new()
        {
            Sound = false,
            Mode = GameMode.Practice,
            Guide = AimGuide.Off,
            Assist = AimAssist.Strong,
            StrokeError = false,
        };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }

    static readonly Vec2 Cue = new(420, 250);

    public static bool Run()
    {
        var failures = new List<string>();
        CheckFollow(failures, new Size(1280, 800));
        CheckFollow(failures, new Size(1920, 1080));
        CheckFollow(failures, new Size(800, 600));
        CheckQuadrants(failures);
        CheckPrecision(failures);
        CheckKeysDoNotLock(failures);
        CheckAimDoesNotLatch(failures);
        CheckHeldButtonStillAims(failures);
        CheckOutsideCanvas(failures);
        CheckRepeatedDrags(failures);
        CheckAssistDoesNotSteer(failures);
        CheckFrameRate(failures);
        CheckShotUsesAim(failures);
        CheckAimAfterShot(failures);
        CheckPullback(failures);
        if (failures.Count == 0)
        {
            Console.WriteLine("aim-check: PASS");
            return true;
        }
        foreach (var f in failures) Console.WriteLine("FAIL: " + f);
        return false;
    }

    sealed class Rig : IDisposable
    {
        public GameScreen Gs { get; }
        readonly Bitmap bmp;
        readonly Graphics g;
        public Rig(Size size, AimAssist assist = AimAssist.Strong)
        {
            var host = new Host();
            host.Settings.Assist = assist;
            Gs = new GameScreen(host);
            bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            g = Graphics.FromImage(bmp);
            Gs.State.Cue.Pos = Cue;
            Gs.State.BallInHand = false;
            Gs.State.KitchenOnly = false;
            Gs.DebugBeginTurn();
            Gs.Draw(g, size);
        }

        public Point P(Vec2 w)
        {
            var p = Gs.ToScreen(w);
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }

        public void Move(Point p) => Gs.MouseMove(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));

        public double Pointer(Point p) => (Gs.ToWorldPublic(p) - Gs.State.Cue.Pos).Angle;

        public void Dispose()
        {
            g.Dispose();
            bmp.Dispose();
        }
    }

    static double Gap(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

    static void CheckFollow(List<string> failures, Size size)
    {
        using var rig = new Rig(size, AimAssist.Off);
        var origin = rig.P(Cue + new Vec2(260, 0));
        rig.Move(origin);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(origin)) > 1e-9)
            failures.Add($"{size.Width}x{size.Height}: first aim is not the pointer");

        // One-pixel steps. Every change in the pointer must show up, unchanged, in the cue.
        var cur = origin;
        double prevA = rig.Gs.AimAngle, prevP = rig.Pointer(cur);
        double unwrappedA = 0, unwrappedP = 0;
        for (int i = 0; i < 80; i++)
        {
            cur = new Point(cur.X, cur.Y + 1);
            rig.Move(cur);
            double dA = Math.IEEERemainder(rig.Gs.AimAngle - prevA, 2 * Math.PI);
            double dP = Math.IEEERemainder(rig.Pointer(cur) - prevP, 2 * Math.PI);
            if (Math.Abs(dA - dP) > 1e-9)
                failures.Add($"{size.Width}x{size.Height}: pixel {i} changed the cue by {dA:E3}, pointer by {dP:E3}");
            if (Math.Abs(dP) > 1e-6 && Math.Abs(dA) < 1e-12)
                failures.Add($"{size.Width}x{size.Height}: pixel {i} moved the pointer and the cue stayed still");
            unwrappedA += dA;
            unwrappedP += dP;
            prevA = rig.Gs.AimAngle;
            prevP = rig.Pointer(cur);
        }
        if (Math.Abs(unwrappedA) < 0.01)
            failures.Add($"{size.Width}x{size.Height}: eighty pixels did not turn the cue");

        // A full turn, sampled every degree. The cue tracks the pointer through the branch cut.
        unwrappedA = 0;
        unwrappedP = 0;
        prevA = rig.Gs.AimAngle;
        var first = rig.P(Cue + Vec2.FromAngle(0) * 220);
        rig.Move(first);
        prevP = rig.Pointer(first);
        prevA = rig.Gs.AimAngle;
        for (int deg = 1; deg <= 360; deg++)
        {
            var pt = rig.P(Cue + Vec2.FromAngle(deg * Math.PI / 180) * 220);
            rig.Move(pt);
            double dA = Math.IEEERemainder(rig.Gs.AimAngle - prevA, 2 * Math.PI);
            double dP = Math.IEEERemainder(rig.Pointer(pt) - prevP, 2 * Math.PI);
            if (Math.Abs(dA - dP) > 1e-9)
                failures.Add($"{size.Width}x{size.Height}: degree {deg} cue delta {dA:E3} != pointer {dP:E3}");
            if (Gap(rig.Gs.AimAngle, rig.Pointer(pt)) > 1e-9)
                failures.Add($"{size.Width}x{size.Height}: degree {deg} cue left the pointer");
            unwrappedA += dA;
            unwrappedP += dP;
            prevA = rig.Gs.AimAngle;
            prevP = rig.Pointer(pt);
        }
        if (Math.Abs(unwrappedA - unwrappedP) > 1e-8)
            failures.Add($"{size.Width}x{size.Height}: full turn drifted from the pointer");
        if (Math.Abs(Math.Abs(unwrappedA) - 2 * Math.PI) > 0.2)
            failures.Add($"{size.Width}x{size.Height}: full turn covered {unwrappedA * 180 / Math.PI:0.0}°");

        // Reverse the same arc. The cue must come back to the pointer, not stick on a step.
        for (int i = 79; i >= 0; i--)
        {
            cur = new Point(origin.X, origin.Y + i);
            rig.Move(cur);
        }
        if (Gap(rig.Gs.AimAngle, rig.Pointer(origin)) > 1e-9)
            failures.Add($"{size.Width}x{size.Height}: reversing the sweep did not return to the pointer");
    }

    static void CheckQuadrants(List<string> failures)
    {
        foreach (var size in new[] { new Size(1280, 800), new Size(1920, 1080), new Size(800, 600) })
        {
            using var rig = new Rig(size, AimAssist.Strong);
            foreach (var deg in new[] { 0, 20, 90, 135, 180, 200, 270, 315, 350 })
            {
                var world = Cue + Vec2.FromAngle(deg * Math.PI / 180) * 180;
                var pt = rig.P(world);
                rig.Move(pt);
                double pointer = Math.Atan2(rig.Gs.ToWorldPublic(pt).Y - rig.Gs.State.Cue.Pos.Y, rig.Gs.ToWorldPublic(pt).X - rig.Gs.State.Cue.Pos.X);
                if (Gap(rig.Gs.AimAngle, pointer) > 1e-12)
                    failures.Add($"{size.Width}x{size.Height}: {deg}° does not point at the cursor");
                // The stick, the guide, and a shot all read this same angle. Round-trip the mapped point.
                var back = rig.Gs.ToScreen(rig.Gs.ToWorldPublic(pt));
                if (Math.Abs(back.X - pt.X) > 0.6 || Math.Abs(back.Y - pt.Y) > 0.6)
                    failures.Add($"{size.Width}x{size.Height}: mouse mapping does not round-trip at {deg}°");
            }
        }
    }

    static void CheckPrecision(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        var origin = rig.P(Cue + new Vec2(240, 0));
        rig.Move(origin);
        foreach (var (mods, name) in new[] { (Keys.Control, "Ctrl"), (Keys.Alt, "Alt"), (Keys.Control | Keys.Alt, "Ctrl+Alt") })
        {
            rig.Gs.DebugBeginTurn();
            rig.Move(origin);
            rig.Gs.DebugAimModifiers(mods);
            var end = new Point(origin.X, origin.Y + 36);
            rig.Move(end);
            if (Gap(rig.Gs.AimAngle, rig.Pointer(end)) > 1e-12)
                failures.Add($"{name}: the cue did not point at the cursor");
            var one = new Point(end.X, end.Y + 1);
            rig.Move(one);
            if (Gap(rig.Gs.AimAngle, rig.Pointer(one)) > 1e-12)
                failures.Add($"{name}: one pixel did not point the cue at the cursor");
        }

        // Just outside the ball, a small sideways move must still turn the cue.
        rig.Gs.DebugBeginTurn();
        rig.Gs.DebugAimModifiers(Keys.None);
        var near = rig.P(Cue + new Vec2(2.2 * Table.R, 0));
        rig.Move(near);
        double beforeNear = rig.Gs.AimAngle;
        var nearSide = rig.P(Cue + Vec2.FromAngle(0.12) * (2.2 * Table.R));
        rig.Move(nearSide);
        if (Gap(rig.Gs.AimAngle, beforeNear) < 1e-4)
            failures.Add("a sideways move beside the cue ball did not turn it");
        if (Gap(rig.Gs.AimAngle, rig.Pointer(nearSide)) > 1e-8)
            failures.Add("a sideways move beside the cue ball left the pointer");
    }

    static void CheckKeysDoNotLock(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        var origin = rig.P(Cue + new Vec2(200, 10));
        rig.Move(origin);
        double a0 = rig.Gs.AimAngle;
        rig.Gs.KeyDown(Keys.Right);
        double stepped = rig.Gs.AimAngle;
        if (Gap(stepped, a0) < 1e-8)
            failures.Add("arrow key did not turn the cue");
        // The tick is a double add. It must not land on a whole degree.
        double deg = Math.IEEERemainder(stepped, 2 * Math.PI) * 180 / Math.PI;
        if (Math.Abs(deg - Math.Round(deg)) < 1e-6)
            failures.Add("arrow key rounded the aim to a whole degree");

        var one = new Point(origin.X, origin.Y + 1);
        rig.Move(one);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(one)) > 1e-12)
            failures.Add("after an arrow key the cue did not return to the cursor");

        double beforeWheel = rig.Gs.AimAngle;
        rig.Gs.MouseWheel(new MouseEventArgs(MouseButtons.None, 0, one.X, one.Y, 120));
        if (Gap(rig.Gs.AimAngle, beforeWheel) < 1e-8)
            failures.Add("the wheel did not turn the cue");
        var two = new Point(one.X + 1, one.Y);
        rig.Move(two);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(two)) > 1e-12)
            failures.Add("after the wheel the cue did not return to the cursor");
    }

    /// <summary>Moving closer must not latch the cue. Every later sideways pixel still aims.</summary>
    static void CheckAimDoesNotLatch(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Strong);
        AimDiag.Record = true;
        AimDiag.Clear();
        Vec2 dir = Vec2.FromAngle(0.4);
        var far = rig.P(Cue + dir * 220);
        rig.Move(far);
        double held = rig.Gs.AimAngle;
        for (int i = 1; i <= 8; i++)
            rig.Move(rig.P(Cue + dir * (220 - i * 14)));
        if (Gap(rig.Gs.AimAngle, held) > 0.02)
            failures.Add("a radial walk toward the cue ball turned it");

        var cur = rig.P(Cue + dir * 100);
        rig.Move(cur);
        int missed = 0;
        for (int i = 0; i < 40; i++)
        {
            var prev = cur;
            double prevA = rig.Gs.AimAngle, prevP = rig.Pointer(prev);
            cur = new Point(cur.X + (i % 2 == 0 ? 1 : 0), cur.Y + 1);
            rig.Move(cur);
            double dA = Math.IEEERemainder(rig.Gs.AimAngle - prevA, 2 * Math.PI);
            double dP = Math.IEEERemainder(rig.Pointer(cur) - prevP, 2 * Math.PI);
            if (Math.Abs(dP) > 1e-6 && Math.Abs(dA - dP) > 1e-8) missed++;
        }
        AimDiag.Record = false;
        if (missed > 0)
            failures.Add($"after moving closer, {missed} sideways pixels did not turn the cue");
        int stuck = 0;
        for (int i = 0; i < AimDiag.Count; i++)
        {
            var s = AimDiag.Samples[i % AimDiag.Capacity];
            if (!s.Applied && !s.Stroke) stuck++;
        }
        if (stuck > 2)
            failures.Add($"aim was skipped on {stuck} samples while the pointer was off the ball");
    }

    static void CheckHeldButtonStillAims(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        var start = rig.P(Cue + new Vec2(200, 0));
        rig.Move(start);
        rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
        var cur = start;
        for (int i = 1; i <= 30; i++)
        {
            double prevA = rig.Gs.AimAngle, prevP = rig.Pointer(cur);
            cur = new Point(cur.X, cur.Y + 2);
            rig.Move(cur);
            double dA = Math.IEEERemainder(rig.Gs.AimAngle - prevA, 2 * Math.PI);
            double dP = Math.IEEERemainder(rig.Pointer(cur) - prevP, 2 * Math.PI);
            if (Math.Abs(dP) > 1e-6 && Math.Abs(dA - dP) > 1e-8)
            {
                failures.Add("holding the button stopped the cue");
                break;
            }
        }
        // A straight draw keeps the angle. Swinging off it aims again without a new press.
        double stroked = rig.Gs.AimAngle;
        var behind = rig.P(Cue - Vec2.FromAngle(stroked) * 80);
        rig.Move(behind);
        if (Gap(rig.Gs.AimAngle, stroked) > 0.05)
            failures.Add("drawing the cue back spun it around");
        var side = new Point(cur.X + 80, cur.Y);
        rig.Move(side);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(side)) > 1e-6)
            failures.Add("after the draw, the cue stayed stuck until the button was released");
        rig.Gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, side.X, side.Y, 0));
        var again = new Point(side.X + 2, side.Y);
        double a = rig.Gs.AimAngle, p = rig.Pointer(side);
        rig.Move(again);
        if (Math.Abs(Math.IEEERemainder(rig.Gs.AimAngle - a, 2 * Math.PI) - Math.IEEERemainder(rig.Pointer(again) - p, 2 * Math.PI)) > 1e-8)
            failures.Add("releasing the button left the next move ignored");
    }

    static void CheckOutsideCanvas(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        var origin = rig.P(Cue + new Vec2(180, 0));
        rig.Move(origin);
        var outside = new Point(-40, origin.Y + 30);
        rig.Move(outside);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(outside)) > 1e-8)
            failures.Add("the cue stopped when the pointer left the window");
        var back = new Point(origin.X, origin.Y + 10);
        rig.Move(back);
        if (Gap(rig.Gs.AimAngle, rig.Pointer(back)) > 1e-8)
            failures.Add("the cue stayed stuck after the pointer returned");
    }

    static void CheckRepeatedDrags(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        var origin = rig.P(Cue + new Vec2(190, 20));
        for (int n = 0; n < 6; n++)
        {
            rig.Move(origin);
            rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, origin.X, origin.Y, 0));
            var moved = new Point(origin.X - 4, origin.Y + 8 + n);
            rig.Move(moved);
            if (Gap(rig.Gs.AimAngle, rig.Pointer(moved)) > 1e-8)
            {
                failures.Add($"drag {n} did not aim");
                break;
            }
            rig.Gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, moved.X, moved.Y, 0));
            rig.Gs.PointerCaptureLost();
            var after = new Point(moved.X + 3, moved.Y);
            double a = rig.Gs.AimAngle, p = rig.Pointer(moved);
            rig.Move(after);
            if (Math.Abs(Math.IEEERemainder(rig.Gs.AimAngle - a, 2 * Math.PI) - Math.IEEERemainder(rig.Pointer(after) - p, 2 * Math.PI)) > 1e-8)
            {
                failures.Add($"after drag {n} the cue did not aim");
                break;
            }
        }
    }

    static void CheckAssistDoesNotSteer(List<string> failures)
    {
        Point[] path;
        using (var probe = new Rig(new Size(1280, 800), AimAssist.Off))
        {
            path = new Point[24];
            for (int i = 0; i < path.Length; i++)
                path[i] = probe.P(Cue + Vec2.FromAngle(i * 0.11 - 0.4) * (160 + (i % 5) * 12));
        }

        var off = RunPath(AimAssist.Off, path);
        var strong = RunPath(AimAssist.Strong, path);
        for (int i = 0; i < path.Length; i++)
            if (Gap(off.Samples[i], strong.Samples[i]) > 1e-12)
                failures.Add($"assist changed the cue at sample {i}");
        if (Gap(off.Rested, off.Samples[^1]) > 1e-12 || Gap(strong.Rested, strong.Samples[^1]) > 1e-12)
            failures.Add("resting on a suggestion moved the cue");
    }

    static (double[] Samples, double Rested) RunPath(AimAssist assist, Point[] path)
    {
        using var rig = new Rig(new Size(1280, 800), assist);
        var aims = new double[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            rig.Move(path[i]);
            rig.Gs.Update(1 / 60.0);
            aims[i] = rig.Gs.AimAngle;
        }
        // Long enough for the suggestion to correct for throw. It still must not move the cue.
        for (int i = 0; i < 20; i++) rig.Gs.Update(1 / 60.0);
        return (aims, rig.Gs.AimAngle);
    }

    static void CheckFrameRate(List<string> failures)
    {
        double Run(double dt)
        {
            using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
            for (int i = 0; i <= 20; i++)
            {
                rig.Move(rig.P(Cue + Vec2.FromAngle(i * 0.02) * 180));
                rig.Gs.Update(dt);
            }
            return rig.Gs.AimAngle;
        }
        if (Gap(Run(1 / 30.0), Run(1 / 120.0)) > 1e-12)
            failures.Add("the same pointer path aimed differently at 30fps and 120fps");
    }

    static void CheckShotUsesAim(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Strong);
        var pt = rig.P(Cue + Vec2.FromAngle(0.37) * 210);
        rig.Move(pt);
        rig.Gs.DebugAimModifiers(Keys.Alt);
        rig.Move(new Point(pt.X + 12, pt.Y - 9));
        rig.Gs.DebugAimModifiers(Keys.None);
        for (int i = 0; i < 30; i++) rig.Gs.Update(1 / 60.0);
        double aim = rig.Gs.AimAngle;
        rig.Gs.KeyDown(Keys.Space);
        var cue = rig.Gs.State.Cue;
        double launch = Physics.LaunchAngle(aim, 0, rig.Gs.State.CurrentPlayer.Cue);
        if (cue.Vel.LengthSq < 1) failures.Add("the shot did not leave the cue");
        else if (Gap(cue.Vel.Angle, launch) > 1e-9)
            failures.Add($"shot left at {cue.Vel.Angle:E6}, aim was {launch:E6}");
    }

    static void CheckAimAfterShot(List<string> failures)
    {
        using var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        rig.Gs.State.Cue.Pos = new Vec2(180, 250);
        rig.Gs.State.BallInHand = false;
        for (int i = 0; i < 25; i++) rig.Gs.KeyDown(Keys.Down);
        var pt = rig.P(rig.Gs.State.Cue.Pos + new Vec2(-80, 0));
        rig.Move(pt);
        rig.Gs.KeyDown(Keys.Space);
        for (int i = 0; i < 60 * 15 && !rig.Gs.ReadyForHuman; i++) rig.Gs.Update(1 / 30.0);
        if (!rig.Gs.ReadyForHuman)
        {
            failures.Add("the shot never returned the table to the player");
            return;
        }
        var cue = rig.Gs.State.Cue.Pos;
        var next = rig.P(cue + new Vec2(120, 35));
        rig.Move(next);
        if (Gap(rig.Gs.AimAngle, (rig.Gs.ToWorldPublic(next) - cue).Angle) > 1e-6)
            failures.Add("the cue did not follow the pointer on the next turn");
        var step = new Point(next.X, next.Y + 2);
        double a = rig.Gs.AimAngle, p = rig.Pointer(next);
        rig.Move(step);
        if (Math.Abs(Math.IEEERemainder(rig.Gs.AimAngle - a, 2 * Math.PI) - Math.IEEERemainder(rig.Pointer(step) - p, 2 * Math.PI)) > 1e-8)
            failures.Add("the cue froze on the turn after a shot");
    }

    static void CheckPullback(List<string> failures)
    {
        // Power is the distance the pointer sits behind the ball, along the cue, divided by a full draw.
        const double fullDraw = 260;
        foreach (var deg in new[] { 0.0, 90.0, 180.0, 235.0 })
        {
            double back = deg == 0 ? 40 : deg == 90 ? 12 : deg == 180 ? 260 : 400;
            var shot = Pull(failures, deg, back, release: true);
            if (shot is null) continue;
            var (aim, speed) = shot.Value;
            double power = Math.Min(1, back / fullDraw);
            double expect = power * Physics.MaxShotSpeed * 0.9; // house cue
            if (Math.Abs(speed - expect) / expect > 0.04)
                failures.Add($"{deg:0}° pull {back:0} left at speed {speed:0.0}, expected {expect:0.0}");
            if (Gap(aim, deg * Math.PI / 180) > 0.02)
                failures.Add($"{deg:0}° shot left on {aim * 180 / Math.PI:0.0}°");
        }

        // Returning the cue forward cancels. Releasing does not strike.
        using (var rig = OpenStroke(out double aim))
        {
            var cue = rig.Gs.State.Cue.Pos;
            var dir = Vec2.FromAngle(aim);
            var press = rig.P(cue + dir * 160);
            rig.Move(press);
            aim = rig.Gs.AimAngle;
            rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, press.X, press.Y, 0));
            var behind = rig.P(cue - dir * 80);
            rig.Move(behind);
            if (Gap(rig.Gs.AimAngle, aim) > 0.01)
                failures.Add("the draw rotated the cue");
            rig.Move(press);
            rig.Gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, press.X, press.Y, 0));
            if (rig.Gs.State.Cue.Vel.LengthSq > 1)
                failures.Add("easing the cue forward still played the shot");
            if (Gap(rig.Gs.AimAngle, rig.Pointer(press)) > 1e-6)
                failures.Add("after cancelling, the cue did not point at the cursor");
        }

        // A sideways drag is aiming, not a shot.
        using (var rig = OpenStroke(out _))
        {
            var start = rig.P(rig.Gs.State.Cue.Pos + new Vec2(160, 0));
            rig.Move(start);
            rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
            var side = new Point(start.X, start.Y + 40);
            rig.Move(side);
            rig.Gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, side.X, side.Y, 0));
            if (rig.Gs.State.Cue.Vel.LengthSq > 1)
                failures.Add("aiming with the button held played a shot");
            if (Gap(rig.Gs.AimAngle, rig.Pointer(side)) > 1e-8)
                failures.Add("aiming with the button held left the cursor");
        }

        // Losing the pointer cancels the draw and does not strike.
        using (var rig = OpenStroke(out double aim))
        {
            var cue = rig.Gs.State.Cue.Pos;
            var dir = Vec2.FromAngle(aim);
            var at = rig.P(cue + dir * 150);
            rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
            var behind = rig.P(cue - dir * 100);
            rig.Move(behind);
            rig.Gs.PointerCaptureLost();
            if (rig.Gs.State.Cue.Vel.LengthSq > 1)
                failures.Add("leaving the window played the shot");
            var again = rig.P(cue + dir.Rotated(0.3) * 140);
            rig.Move(again);
            if (Gap(rig.Gs.AimAngle, rig.Pointer(again)) > 1e-8)
                failures.Add("after a cancelled draw the cue did not follow the pointer");
        }
    }

    static (double aim, double speed)? Pull(List<string> failures, double deg, double back, bool release)
    {
        using var rig = OpenStroke(out _);
        var cue = rig.Gs.State.Cue.Pos;
        var dir = Vec2.FromAngle(deg * Math.PI / 180);
        var at = rig.P(cue + dir * 170);
        rig.Move(at);
        double aim = rig.Gs.AimAngle;
        rig.Gs.MouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
        var behind = rig.P(rig.Gs.ToWorldPublic(at) - dir * back);
        rig.Move(behind);
        if (Gap(rig.Gs.AimAngle, aim) > 0.01)
            failures.Add($"{deg:0}° draw of {back:0} changed the aim");
        if (!release) return null;
        rig.Gs.MouseUp(new MouseEventArgs(MouseButtons.Left, 1, behind.X, behind.Y, 0));
        var vel = rig.Gs.State.Cue.Vel;
        if (vel.LengthSq < 1)
        {
            failures.Add($"{deg:0}° draw of {back:0} did not strike");
            return null;
        }
        return (vel.Angle, vel.Length);
    }

    static Rig OpenStroke(out double aim)
    {
        var rig = new Rig(new Size(1280, 800), AimAssist.Off);
        foreach (var b in rig.Gs.State.Balls)
            if (b.Number != 0) b.Pocketed = true;
        rig.Gs.State.Cue.Pos = new Vec2(400, 220);
        rig.Gs.State.BallInHand = false;
        var at = rig.P(rig.Gs.State.Cue.Pos + new Vec2(160, 0));
        rig.Move(at);
        aim = rig.Gs.AimAngle;
        return rig;
    }
}

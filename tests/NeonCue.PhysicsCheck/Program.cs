// Physics validation: checks the engine against textbook billiards results and measured
// behaviour of real balls (Alciatore, "The Illustrated Principles of Pool and Billiards").
//   dotnet run --project tests/NeonCue.PhysicsCheck
using NeonCue.Core;

Table.Use(TableKind.Pool);
double R = Table.R, G = Physics.G;
int failed = 0;
void Check(string name, double value, double lo, double hi, string unit)
{
    bool ok = value >= lo && value <= hi && !double.IsNaN(value);
    if (!ok) failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL"),-5} {name,-58} {value,9:0.###} {unit,-6} (expected {lo:0.###}..{hi:0.###})");
}

Ball Cue(Vec2 p) => new(0, p);
Ball Obj(int n, Vec2 p) => new(n, p);
double Deg(double rad) => rad * 180 / Math.PI;
double AngleBetween(Vec2 a, Vec2 b) => Deg(Math.Acos(Math.Clamp(Vec2.Dot(a.Normalized(), b.Normalized()), -1, 1)));
Vec2 Mid = new(Table.Width / 2, Table.Height / 2);

// 1. Natural roll: a centre-ball stroke slides, then rolls at 5/7 of the launch speed.
{
    var c = Cue(new Vec2(150, 250));
    Physics.Strike(c, 0, 0.5, 0, 0);
    double v0 = c.Vel.Length, rollSpeed = 0, startX = c.Pos.X, slideDist = 0;
    var ev = new ShotEvents();
    for (int i = 0; i < 4800; i++)
    {
        Physics.Step([c], ev, null);
        double ux = c.Vel.X + R * c.Wy, uy = c.Vel.Y - R * c.Wx;
        if (Math.Sqrt(ux * ux + uy * uy) < 1e-6) { rollSpeed = c.Vel.Length; slideDist = c.Pos.X - startX; break; }
    }
    Check("Natural roll speed / launch speed (theory 5/7 = 0.714)", rollSpeed / v0, 0.70, 0.73, "");
    double theory = 12.0 * v0 * v0 / (49 * Physics.MuSlide * G);
    Check("Sliding distance / theory 12v²/(49·μs·g)", slideDist / theory, 0.95, 1.05, "");
}

// 2. Stun: full hit while the cue ball still slides with no spin - the cue ball stops dead.
{
    var c = Cue(new Vec2(300, 250));
    var o = Obj(1, new Vec2(300 + 2 * R + 6, 250));
    Physics.Strike(c, 0, 0.7, 0, 0);
    double v0 = c.Vel.Length;
    var ev = new ShotEvents();
    var balls = new[] { c, o };
    while (o.Vel.LengthSq == 0) Physics.Step(balls, ev, null);
    Check("Stun shot: cue speed after full hit / before", c.Vel.Length / v0, 0, 0.04, "");
}

// 3. Draw and follow on a straight-in shot: how far the cue ball travels back / on after contact.
(double back, double on) AfterContact(double top, double gap = 60)
{
    var c = Cue(new Vec2(300, 250));
    var o = Obj(1, new Vec2(300 + 2 * R + gap, 250));
    Physics.Strike(c, 0, 0.3, 0, top);
    var balls = new[] { c, o };
    var ev = new ShotEvents();
    while (o.Vel.LengthSq == 0 && c.IsMoving) Physics.Step(balls, ev, null);
    double contactX = c.Pos.X, minX = contactX, maxX = contactX;
    // Stop if the object ball comes back off a cushion and hits the cue ball again.
    while (o.Vel.X >= 0 && Physics.Step(balls, ev, null)) { minX = Math.Min(minX, c.Pos.X); maxX = Math.Max(maxX, c.Pos.X); }
    return (contactX - minX, maxX - contactX);
}
var drawn = AfterContact(-1);
var followed = AfterContact(1);
var stunned = AfterContact(0, gap: 8);
Check("Draw (bottom spin): cue ball comes back (units)", drawn.back, 40, 2000, "units");
Check("Follow (top spin): cue ball runs through (units)", followed.on, 40, 2000, "units");
Check("Stun (centre ball, sliding): cue ball stays near contact (units)", Math.Max(stunned.back, stunned.on), 0, 15, "units");

// 4. 90-degree rule: a stunned (sliding, no-spin) cue ball and the object ball separate at 90 degrees.
{
    var c = Cue(new Vec2(300, 250));
    var o = Obj(1, new Vec2(300 + 2 * R + 10, 250 + R)); // half-ball hit
    Physics.Strike(c, 0, 0.7, 0, 0);
    var balls = new[] { c, o };
    var ev = new ShotEvents();
    while (o.Vel.LengthSq == 0) Physics.Step(balls, ev, null);
    // Theory with e = 0.95 and fast-shot friction: slightly under 90 degrees.
    Check("90° rule: separation angle after a stun cut", AngleBetween(c.Vel, o.Vel), 85, 92, "deg");
}

// 5. 30-degree rule: a rolling cue ball deflects about 30 degrees on a half-ball hit.
{
    var c = Cue(new Vec2(100, 250));
    var o = Obj(1, new Vec2(560, 250 + R));
    Physics.Strike(c, 0, 0.35, 0, 0.8); // rolls well before contact
    var balls = new[] { c, o };
    var ev = new ShotEvents();
    while (o.Vel.LengthSq == 0 && c.IsMoving) Physics.Step(balls, ev, null);
    // let the cue ball's spin pull it onto its final rolling line
    for (int i = 0; i < 480 && c.IsMoving; i++)
    {
        Physics.Step(balls, ev, null);
        double ux = c.Vel.X + R * c.Wy, uy = c.Vel.Y - R * c.Wx;
        if (Math.Sqrt(ux * ux + uy * uy) < 1e-6) break;
    }
    Check("30° rule: rolling cue-ball deflection on a half-ball hit", AngleBetween(c.Vel, new Vec2(1, 0)), 27, 36, "deg");
}

// 6. Cut-induced throw: the object ball leaves a few degrees off the line of centres.
{
    var c = Cue(new Vec2(300, 250));
    var o = Obj(1, new Vec2(300 + 2 * R + 40, 250 + R));
    Physics.Strike(c, 0, 0.2, 0, 0);
    var balls = new[] { c, o };
    var ev = new ShotEvents();
    Vec2 centres = Vec2.Zero;
    while (o.Vel.LengthSq == 0 && c.IsMoving)
    {
        centres = o.Pos - c.Pos;
        Physics.Step(balls, ev, null);
    }
    Check("Throw: slow half-ball cut, object off the line of centres", AngleBetween(o.Vel, o.Pos - c.Pos), 1, 6, "deg");
}

// 7. Cushion: angle in about equals angle out without spin; right english widens to the right.
double Rebound(double side)
{
    var c = Cue(new Vec2(500, 300));
    Physics.Strike(c, -Math.PI / 4, 0.45, side, 0); // up-right towards the top cushion at 45°
    var balls = new[] { c };
    var ev = new ShotEvents();
    while (c.Vel.Y < 0 && c.IsMoving) Physics.Step(balls, ev, null);
    return Deg(Math.Atan2(c.Vel.Y, c.Vel.X)); // 45 = mirror reflection
}
double plain = Rebound(0), right = Rebound(0.8), left = Rebound(-0.8);
Check("Cushion rebound angle, no spin (mirror = 45)", plain, 36, 50, "deg");
Check("Right english runs the ball more to the right (smaller angle)", plain - right, 2, 40, "deg");
Check("Left english checks the ball (larger angle)", left - plain, 2, 40, "deg");

// Follow opens the rebound; draw closes it. Same 45° path into the top cushion.
double ReboundTop(double top)
{
    var c = Cue(new Vec2(500, 300));
    Physics.Strike(c, -Math.PI / 4, 0.45, 0, top);
    var balls = new[] { c };
    var ev = new ShotEvents();
    while (c.Vel.Y < 0 && c.IsMoving) Physics.Step(balls, ev, null);
    return Deg(Math.Atan2(c.Vel.Y, c.Vel.X));
}
double followRail = ReboundTop(0.9), drawRail = ReboundTop(-0.9);
Check("Follow opens the cushion rebound more than draw", followRail - drawRail, 1, 40, "deg");

{
    var soft = Cue(new Vec2(200, 250));
    var hard = Cue(new Vec2(200, 250));
    Physics.Strike(soft, 0, 0.5, 0, 0);
    Physics.Strike(hard, 0, 1, 0, 0);
    Check("Half stroke launches at half the full-stroke speed", soft.Vel.Length / hard.Vel.Length, 0.48, 0.52, "");
}

// Combination: ball 2 sits on the pocket line. Ball 1 is struck straight into it.
// Ball 2 must be the one that falls. Ball 1 must stay on the table.
{
    var pocket = Table.Pockets.First(p => p.Name == "bottom-right corner");
    Vec2 away = pocket.Inward; // from the pocket, back onto the bed
    Vec2 target = pocket.Aim;
    Vec2 striker = target + away * (2 * R + 6);
    var a = Obj(1, striker);
    var b = Obj(2, target);
    double speed = Physics.SpeedFraction(0.45) * Physics.MaxShotSpeed;
    a.Vel = away * -speed;
    var ev = Physics.Simulate([a, b]);
    bool strikerIn = ev.Pocketed.Contains(1);
    bool targetIn = ev.Pocketed.Contains(2);
    Check("Combination: the ball at the pocket is the one that falls", targetIn && !strikerIn ? 1 : 0, 1, 1, "");

    double yRail = Table.Height - R;
    double jawX = Table.Width - Table.CornerGap;
    Vec2 railSit = new(jawX - 0.5 * R, yRail);
    Vec2 railHit = new(railSit.X - 2 * R - 10, yRail);
    var railA = Obj(1, railHit);
    var railB = Obj(2, railSit);
    railA.Vel = new Vec2(speed, 0);
    var railEv = Physics.Simulate([railA, railB]);
    Check("Rail combination: the ball on the jaw falls, the striker stays out",
        railEv.Pocketed.Contains(2) && !railEv.Pocketed.Contains(1) ? 1 : 0, 1, 1, "");

    // Centres have already crossed, but the striker is still moving through. The contact was
    // missed; the object ball must leave with the momentum and the striker must not keep it.
    var passed = Obj(1, new Vec2(500 + 2 * R - 3, 250));
    var sat = Obj(2, new Vec2(500, 250));
    passed.Vel = new Vec2(speed, 0);
    double before = passed.Vel.X;
    var evPass = new ShotEvents();
    var pair = new[] { passed, sat };
    for (int i = 0; i < 3; i++) Physics.Step(pair, evPass, null);
    Check("Passed-through contact: object ball takes the speed", sat.Vel.X > before * 0.4 && passed.Vel.X < before * 0.5 ? 1 : 0, 1, 1, "");
}

// A ball sent at the calculated entry point must fall, including from awkward angles.
{
    var rng = new Random(11);
    int tried = 0, made = 0;
    for (int n = 0; n < 36; n++)
    {
        var pocket = Table.Pockets[n % Table.Pockets.Length];
        var from = new Vec2(90 + rng.NextDouble() * (Table.Width - 180), 70 + rng.NextDouble() * (Table.Height - 140));
        if (!PocketEntry.TryGet(pocket, from, out Vec2 aim)) continue;
        Vec2 dir = (aim - from).Normalized();
        double approach = Math.Acos(Math.Clamp(Vec2.Dot(dir, -pocket.Inward), -1, 1));
        if (approach > 1.05) continue;
        tried++;
        var ball = new Ball(1, from);
        ball.Vel = dir * Physics.SpeedToTravel(Vec2.Distance(from, aim) + 80);
        var ev = Physics.Simulate([ball]);
        if (ev.Pocketed.Contains(1)) made++;
    }
    Check("Pocket entry clears the jaws", tried == 0 ? 0 : made / (double)tried, 0.9, 1.01, "");
}

// 8. Energy never increases during a full-power break.
{
    var rules = GameRules.Create(GameMode.EightBall);
    var s = new GameState { Players = [new PlayerState(), new PlayerState()] };
    rules.NewRack(s, new Random(3));
    var apex = s.ObjectBallsOnTable.OrderBy(b => b.Pos.X).First();
    Physics.Strike(s.Cue, (apex.Pos - s.Cue.Pos).Angle, 1.0, 0, 0);
    double E() => s.Balls.Where(b => !b.Pocketed).Sum(b => b.Vel.LengthSq / 2 + 0.2 * R * R * (b.Wx * b.Wx + b.Wy * b.Wy + b.Wz * b.Wz));
    double prev = E(), worstGain = 0;
    var ev = new ShotEvents();
    for (int i = 0; i < 480 * 20 && Physics.Step(s.Balls, ev, null); i++)
    {
        double e = E();
        worstGain = Math.Max(worstGain, (e - prev) / Math.Max(prev, 1));
        prev = e;
    }
    Check("Energy: largest step-to-step gain during a break", worstGain * 100, -1, 0.5, "%");
}

// 9. Determinism: the same shot always ends the same way.
{
    string Run()
    {
        var rules = GameRules.Create(GameMode.NineBall);
        var s = new GameState { Players = [new PlayerState(), new PlayerState()] };
        rules.NewRack(s, new Random(9));
        Physics.Strike(s.Cue, 0.01, 0.9, 0.3, -0.2);
        Physics.Simulate(s.Balls);
        return string.Join(";", s.Balls.Select(b => $"{b.Pos.X:0.000000},{b.Pos.Y:0.000000}"));
    }
    Check("Determinism: identical results for identical shots", Run() == Run() ? 1 : 0, 1, 1, "");
}

Console.WriteLine(failed == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failed})");
return failed == 0 ? 0 : 1;

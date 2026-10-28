using NeonCue.Core;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>
/// Automated human player: drives the real game screen with synthetic mouse and keyboard input on
/// hundreds of set-up pots, behaving like a hand does (imprecise pointing, wobbly paths to the cue
/// ball, arrow-key fine-tuning), and compares the result with a perfect stroke along the guide.
/// Run with: NeonCue.exe --playtest report.txt [trials]
/// </summary>
static class PlayTest
{
    enum Gesture
    {
        PreciseFromTarget,   // pixel-exact pointing, press on the target, drag back
        SloppyToCueBall,     // +-2px pointing, wobbly path to the cue ball, press there, drag back
        KeysThenCueBall,     // rough pointing, arrow-key fine-tune, wobbly path to the cue ball
        BallInHand,          // place by click, check pick-up, then sloppy pull from the cue ball
        QuickSweep,          // fast arcs around the cue ball ending on the target, no pause, then reach and pull
        FreeFollow,          // sweep the cursor around; the cue must follow it closely the whole time
    }

    sealed record Row(string Cue, Gesture Gesture, AimGuide Guide, double Side, bool Close, bool Gated, AimAssist Assist = AimAssist.Light);

    sealed class Host : IHost
    {
        public AppSettings Settings { get; } = new() { Sound = false, Mode = GameMode.Practice, Opponent = OpponentType.Cpu, Guide = AimGuide.Full };
        public SoundFx Sound { get; } = new() { Enabled = false };
        public void ShowMenu() { }
        public void StartGame() { }
        public void ShowShop() { }
        public void Quit() { }
    }

    sealed class Tally
    {
        public int N, IdealPots, UiPots, LostPots, WrongFirst;
        public double AimErrSum, FollowWorst;
        public readonly HashSet<string> Failures = new();
    }

    /// <summary>Returns false when a gated row loses more than 10% of the pots the guide promises.</summary>
    public static bool Run(string reportPath, int trials)
    {
        Row[] rows =
        [
            new("royale", Gesture.FreeFollow, AimGuide.Full, 0, false, true),
            // Precise pointing is the pixel the cursor lands on. Assist must not pull it onto the line,
            // so this row is not required to match a sub-pixel ideal.
            new("royale", Gesture.PreciseFromTarget, AimGuide.Full, 0, false, false),
            new("royale", Gesture.SloppyToCueBall, AimGuide.Full, 0, false, false, AimAssist.Strong),
            new("royale", Gesture.SloppyToCueBall, AimGuide.Full, 0.6, false, false, AimAssist.Strong),
            new("royale", Gesture.SloppyToCueBall, AimGuide.Full, 0, true, false, AimAssist.Strong),
            new("royale", Gesture.SloppyToCueBall, AimGuide.Full, 0, false, false, AimAssist.Light),
            new("royale", Gesture.QuickSweep, AimGuide.Full, 0, false, false),
            new("royale", Gesture.KeysThenCueBall, AimGuide.Off, 0, false, false, AimAssist.Off),
            new("royale", Gesture.KeysThenCueBall, AimGuide.Off, 0, true, false, AimAssist.Off),
            new("royale", Gesture.BallInHand, AimGuide.Full, 0, false, false, AimAssist.Strong),
            new("royale", Gesture.SloppyToCueBall, AimGuide.Off, 0, false, false, AimAssist.Off),
            new("house", Gesture.SloppyToCueBall, AimGuide.Full, 0, false, false, AimAssist.Strong),
            new("house", Gesture.KeysThenCueBall, AimGuide.Off, 0, false, false, AimAssist.Off),
        ];

        bool ok = true;
        var lines = new List<string>
        {
            $"Neon Cue play-test - {trials} set-up pots per row, driven through the real mouse/keyboard handlers",
            "",
            $"{"cue",-7} {"gesture",-18} {"assist",-7} {"spin",4} {"range",-6} {"ideal%",6} {"ui%",5} {"lost",5} {"wrong1st",8} {"aimErr",7}",
        };
        foreach (var row in rows)
        {
            var t = new Tally();
            var rng = new Random(77);
            for (int i = 0; i < trials; i++) Trial(row, rng, t);
            foreach (var f in t.Failures) lines.Add($"  FAIL: {f}");
            if (t.Failures.Count > 0 || (row.Gated && t.UiPots < 0.9 * t.IdealPots)) ok = false;
            if (row.Gesture == Gesture.FreeFollow)
            {
                lines.Add($"{row.Cue,-7} {row.Gesture,-18} {row.Assist,-7} {"",4} {"",-6} cue lag behind cursor while sweeping: mean {t.AimErrSum / t.N:0.00}°, worst {t.FollowWorst:0.00}°");
                if (t.FollowWorst > 0.5) ok = false;
                continue;
            }
            lines.Add($"{row.Cue,-7} {row.Gesture,-18} {row.Assist,-7} {row.Side,4:0.0} {(row.Close ? "close" : "normal"),-6} {100.0 * t.IdealPots / t.N,6:0} {100.0 * t.UiPots / t.N,5:0} {t.LostPots,5} {t.WrongFirst,8} {t.AimErrSum / t.N,6:0.00}°");
        }
        lines.Add("");
        lines.Add("ideal% = pot rate of a perfect stroke along the guide; ui% = pot rate through the controls;");
        lines.Add("lost = pots the perfect stroke makes but the controlled stroke missed; aimErr = mean stick-angle error at release.");
        lines.Add("Ungated rows: the pointer is the aim, including pixel noise beside the cue ball. Assist does not move the stick.");
        lines.Add(ok ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(reportPath, lines);
        return ok;
    }

    static void Trial(Row row, Random rng, Tally t)
    {
        double R = Table.R;
        var host = new Host();
        host.Settings.OwnedCues = [row.Cue];
        host.Settings.EquippedCue = row.Cue;
        host.Settings.Guide = row.Guide;
        host.Settings.Assist = row.Assist;
        var gs = new GameScreen(host);
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        gs.Draw(g, bmp.Size);

        var s = gs.State;
        foreach (var b in s.Balls) if (b.Number > 1) b.Pocketed = true;
        var target = s.Balls[1];

        // A random makeable pot.
        Vec2 cuePos, ghost;
        Pocket pocket;
        while (true)
        {
            pocket = Table.Pockets[rng.Next(6)];
            var tp = new Vec2(80 + rng.NextDouble() * (Table.Width - 160), 60 + rng.NextDouble() * (Table.Height - 120));
            if (!PocketEntry.TryGet(pocket, tp, out Vec2 entry)) continue;
            Vec2 dir = (entry - tp).Normalized();
            double appr = Math.Acos(Math.Clamp(Vec2.Dot(dir, -pocket.Inward), -1, 1));
            if (appr > (pocket.IsCorner ? 0.9 : 0.6)) continue;
            double cut = (rng.NextDouble() * 2 - 1) * 40 * Math.PI / 180;
            double dist = row.Close ? 30 + rng.NextDouble() * 80 : 120 + rng.NextDouble() * 330;
            ghost = tp - dir * (2 * R);
            cuePos = ghost - dir.Rotated(cut) * dist;
            if (cuePos.X < 2 * R || cuePos.X > Table.Width - 2 * R || cuePos.Y < 2 * R || cuePos.Y > Table.Height - 2 * R) continue;
            if (Vec2.Distance(cuePos, tp) < 2.6 * R) continue;
            target.Pos = tp;
            break;
        }
        s.Cue.Pos = cuePos;
        s.BallInHand = row.Gesture == Gesture.BallInHand;
        s.KitchenOnly = false;
        s.IsBreakShot = false;
        gs.DebugBeginTurn();
        gs.DebugSetSpin(row.Side, 0);
        gs.Draw(g, bmp.Size);

        Point P(Vec2 w) { var p = gs.ToScreen(w); return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)); }
        MouseEventArgs M(Point p) => new(MouseButtons.Left, 1, p.X, p.Y, 0);
        void Move(Point p) { gs.MouseMove(M(p)); gs.Update(1 / 120.0); }
        Point Jitter(Point p, int px) => new(p.X + rng.Next(-px, px + 1), p.Y + rng.Next(-px, px + 1));

        if (row.Gesture == Gesture.BallInHand)
        {
            var at = P(cuePos);
            Move(at); gs.MouseDown(M(at)); gs.MouseUp(M(at));
            if (gs.IsPlacing) t.Failures.Add("click did not place the cue ball");
            gs.MouseDown(M(at)); gs.MouseUp(M(at));
            if (!gs.IsPlacing) t.Failures.Add("click on the cue ball did not pick it up");
            gs.MouseDown(M(at)); gs.MouseUp(M(at));
            cuePos = s.Cue.Pos;
        }

        var stick = s.CurrentPlayer.Cue;
        double aimNeeded = (ghost - cuePos).Angle + row.Side * stick.Squirt * Math.PI / 180;
        PocketEntry.TryGet(pocket, target.Pos, out Vec2 potAt);
        double dTotal = Vec2.Distance(cuePos, ghost) + Vec2.Distance(target.Pos, potAt);
        double power = Math.Clamp(0.22 + dTotal / 2600, 0.15, 0.7);

        var refState = s.Clone();
        Physics.Strike(refState.Cue, aimNeeded, power, row.Side, 0, stick, false);
        var refEv = Physics.Simulate(refState.Balls);
        bool idealPot = refEv.Pocketed.Contains(1) && !refEv.CueScratched;

        if (row.Gesture == Gesture.FreeFollow)
        {
            double worst = 0, sum = 0;
            int n = 0;
            double a0 = rng.NextDouble() * Math.PI * 2, radius = 140 + rng.NextDouble() * 200;
            for (int k = 0; k <= 40; k++)
            {
                double a = a0 + k * 0.06 * (k < 20 ? 1 : -1);
                var at = P(cuePos + Vec2.FromAngle(a) * radius);
                Move(at);
                double want = (gs.ToWorldPublic(at) - cuePos).Angle;
                double lag = Math.Abs(Math.IEEERemainder(gs.AimAngle - want, 2 * Math.PI)) * 180 / Math.PI;
                worst = Math.Max(worst, lag);
                sum += lag;
                n++;
            }
            t.N++;
            t.AimErrSum += sum / n;
            t.FollowWorst = Math.Max(t.FollowWorst, worst);
            return;
        }

        // Point at the ghost ball (or a little past it when it is very close to the cue ball).
        Vec2 aimDir = Vec2.FromAngle(aimNeeded);
        double pointDist = Math.Max(Vec2.Distance(cuePos, ghost), 4 * R);
        Vec2 pointAt = cuePos + aimDir * pointDist;
        int sloppiness = row.Gesture switch { Gesture.PreciseFromTarget or Gesture.QuickSweep => 0, Gesture.KeysThenCueBall => 6, _ => 2 };
        var aimPx = Jitter(P(pointAt), sloppiness);
        if (row.Gesture == Gesture.QuickSweep)
        {
            // Swing round the cue ball onto the target at the same radius, no pause anywhere.
            double r0 = Vec2.Distance(cuePos, pointAt), aimA = (pointAt - cuePos).Angle;
            for (int k = 8; k >= 0; k--) Move(P(cuePos + Vec2.FromAngle(aimA + k * 0.05) * r0));
            aimPx = P(pointAt);
        }
        else
        {
            // Arrive at the target from outside, like a hand settling.
            Move(new Point(aimPx.X + 30, aimPx.Y - 20));
            Move(new Point(aimPx.X + 8, aimPx.Y - 4));
            Move(aimPx);
        }
        // Humans look at the guide for a moment; the precise and quick gestures press with no pause.
        if (row.Gesture is not (Gesture.PreciseFromTarget or Gesture.QuickSweep))
            for (int k = 0; k < 12; k++) gs.Update(1 / 60.0);

        if (row.Gesture == Gesture.KeysThenCueBall)
        {
            // Fine-tune with the arrow keys until the stick is on the line, as the guide shows a human.
            double prevErr = double.MaxValue;
            for (int k = 0; k < 2000; k++)
            {
                double err = Math.IEEERemainder(aimNeeded - gs.AimAngle, 2 * Math.PI);
                double abs = Math.Abs(err);
                // Stop when a tick would step over the line. A close shot's tick is wider than 0.0003 rad.
                if (abs < 0.0003 || abs >= prevErr) break;
                prevErr = abs;
                bool coarse = abs > 0.02;
                gs.KeyDown((err > 0 ? Keys.Right : Keys.Left) | (coarse ? Keys.Shift : Keys.None));
            }
        }

        Point pressAt = aimPx;
        Vec2 pullFrom = pointAt;
        if (row.Gesture != Gesture.PreciseFromTarget)
        {
            // Radial walk to the cue ball. The angle stays put because the pointer angle does not change.
            for (int k = 1; k <= 14; k++)
                Move(P(Vec2.Lerp(pointAt, cuePos, k / 14.0)));
            pressAt = Jitter(P(cuePos), 2);
            pullFrom = cuePos;
            for (int k = 0; k < 9; k++) gs.Update(1 / 60.0); // short hesitation on the cue ball
        }
        gs.MouseDown(M(pressAt));
        double pull = power * 260;
        int dragJitter = row.Gesture == Gesture.PreciseFromTarget ? 0 : 3;
        for (int k = 1; k <= 10; k++) Move(Jitter(P(pullFrom - aimDir * (pull * k / 10.0)), dragJitter));
        double aimAtRelease = gs.AimAngle;
        gs.MouseUp(M(P(pullFrom - aimDir * pull)));

        for (int f = 0; f < 60 * 50 && (gs.ShotInProgress || !gs.ReadyForHuman); f++) gs.Update(1 / 60.0);

        var ev = gs.LastEvents;
        bool uiPot = ev.Pocketed.Contains(1) && !ev.CueScratched;
        t.N++;
        if (idealPot) t.IdealPots++;
        if (uiPot) t.UiPots++;
        if (idealPot && !uiPot) t.LostPots++;
        if (ev.FirstContact != 1) t.WrongFirst++;
        t.AimErrSum += Math.Abs(Math.IEEERemainder(aimAtRelease - aimNeeded, 2 * Math.PI)) * 180 / Math.PI;
    }
}

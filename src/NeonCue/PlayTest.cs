using NeonCue.Core;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>
/// Automated human player: drives the real game screen with synthetic mouse input (move, press,
/// drag back, release) on hundreds of set-up pots, and compares what the aim guide promised with
/// what actually happened. Run with: NeonCue.exe --playtest [trials]
/// </summary>
static class PlayTest
{
    enum Gesture { DragFromTarget, DragFromCueBall, BallInHandDrag }

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
        public int N, IdealPots, UiPots, GuideRight, WrongFirst, LostPots;
        public double AimErrSum, AimErrMax;
        public readonly HashSet<string> Failures = new();
    }

    static void Fail(Tally t, string why) => t.Failures.Add(why);

    /// <summary>Returns false when a perfect-cue row falls below 90% of its ideal pot rate.</summary>
    public static bool Run(string reportPath, int trials)
    {
        bool ok = true;
        var lines = new List<string> { $"Neon Cue play-test — {trials} set-up pots per row, real mouse input path", "" };
        lines.Add($"{"cue",-8} {"gesture",-16} {"spin",5} {"ideal%",7} {"ui%",6} {"lost",5} {"wrong1st",9} {"aimErr°",8} {"max°",6}");
        foreach (var cue in new[] { "royale", "house" })
            foreach (var gesture in Enum.GetValues<Gesture>())
                foreach (var side in new[] { 0.0, 0.6 })
                {
                    var t = new Tally();
                    var rng = new Random(77);
                    for (int i = 0; i < trials; i++) Trial(cue, gesture, side, rng, t);
                    foreach (var f in t.Failures) lines.Add($"  FAIL: {f}");
                    if (t.Failures.Count > 0 || (cue == "royale" && t.UiPots < 0.9 * t.IdealPots)) ok = false;
                    lines.Add($"{cue,-8} {gesture,-16} {side,5:0.0} {100.0 * t.IdealPots / t.N,7:0} {100.0 * t.UiPots / t.N,6:0} {t.LostPots,5} {t.WrongFirst,9} {t.AimErrSum / t.N,8:0.000} {t.AimErrMax,6:0.00}");
                }
        lines.Add("");
        lines.Add("ideal% = pot rate with a perfect stroke along the guide; ui% = pot rate through the mouse;");
        lines.Add("lost = pots the perfect stroke makes but the mouse stroke missed; wrong1st = cue ball hit a different first ball than the guide showed.");
        lines.Add(ok ? "RESULT: PASS" : "RESULT: FAIL");
        File.WriteAllLines(reportPath, lines);
        return ok;
    }

    static void Trial(string cueId, Gesture gesture, double side, Random rng, Tally t)
    {
        const double R = Table.R;
        var host = new Host();
        host.Settings.OwnedCues = [cueId];
        host.Settings.EquippedCue = cueId;
        var gs = new GameScreen(host);
        using var bmp = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(bmp);
        gs.Draw(g, bmp.Size);

        var s = gs.State;
        foreach (var b in s.Balls) if (b.Number > 1) b.Pocketed = true;
        var target = s.Balls[1];

        // A random makeable pot: object ball, pocket, cut angle, distance.
        Vec2 cuePos, ghost, aimPoint;
        Pocket pocket;
        while (true)
        {
            pocket = Table.Pockets[rng.Next(6)];
            var tp = new Vec2(80 + rng.NextDouble() * (Table.Width - 160), 60 + rng.NextDouble() * (Table.Height - 120));
            Vec2 dir = (pocket.Aim - tp).Normalized();
            double appr = Math.Acos(Math.Clamp(Vec2.Dot(dir, -pocket.Inward), -1, 1));
            if (appr > (pocket.IsCorner ? 0.9 : 0.6)) continue;
            double cut = (rng.NextDouble() * 2 - 1) * 40 * Math.PI / 180;
            double dist = 120 + rng.NextDouble() * 330;
            ghost = tp - dir * (2 * R);
            cuePos = ghost - dir.Rotated(cut) * dist;
            if (cuePos.X < 2 * R || cuePos.X > Table.Width - 2 * R || cuePos.Y < 2 * R || cuePos.Y > Table.Height - 2 * R) continue;
            target.Pos = tp;
            aimPoint = pocket.Aim;
            break;
        }
        s.Cue.Pos = cuePos;
        s.BallInHand = gesture == Gesture.BallInHandDrag;
        s.KitchenOnly = false;
        s.IsBreakShot = false;
        gs.DebugBeginTurn();
        gs.DebugSetSpin(side, 0);
        gs.Draw(g, bmp.Size);
        if (gesture == Gesture.BallInHandDrag)
        {
            // Place the cue ball with a click, check a plain click picks it back up, then place again.
            var at = gs.ToScreen(cuePos);
            var e = new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(at.X), (int)Math.Round(at.Y), 0);
            gs.MouseMove(e); gs.MouseDown(e); gs.MouseUp(e);
            if (gs.IsPlacing) Fail(t, "click did not place the cue ball");
            gs.MouseDown(e); gs.MouseUp(e);
            if (!gs.IsPlacing) Fail(t, "click on the cue ball did not pick it up");
            gs.MouseDown(e); gs.MouseUp(e);
            cuePos = s.Cue.Pos;
        }

        var stick = s.CurrentPlayer.Cue;
        double ideal = (ghost - cuePos).Angle;                       // launch direction we need
        double aimNeeded = ideal + side * stick.Squirt * Math.PI / 180; // stick angle that launches along it
        double dTotal = Vec2.Distance(cuePos, ghost) + Vec2.Distance(target.Pos, aimPoint);
        double power = Math.Clamp(0.22 + dTotal / 2600, 0.15, 0.7);

        // Perfect stroke for reference.
        var refState = s.Clone();
        Physics.Strike(refState.Cue, aimNeeded, power, side, 0, stick, false);
        var refEv = Physics.Simulate(refState.Balls);
        bool idealPot = refEv.Pocketed.Contains(1) && !refEv.CueScratched;

        // Human-like input: point at the ghost ball along the needed stick line.
        Point P(Vec2 w) { var p = gs.ToScreen(w); return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)); }
        MouseEventArgs M(Point p) => new(MouseButtons.Left, 1, p.X, p.Y, 0);
        double ppu = gs.ToScreen(new Vec2(1, 0)).X - gs.ToScreen(Vec2.Zero).X;
        Vec2 aimDir = Vec2.FromAngle(aimNeeded);
        Vec2 pointAt = cuePos + aimDir * Vec2.Distance(cuePos, ghost);
        var aimPx = P(pointAt);
        gs.MouseMove(M(aimPx));

        Point pressAt = aimPx;
        if (gesture == Gesture.DragFromCueBall)
        {
            // Walk the cursor from the target to the cue ball, as a hand would.
            for (int k = 1; k <= 12; k++) gs.MouseMove(M(P(Vec2.Lerp(pointAt, cuePos, k / 12.0))));
            pressAt = P(cuePos);
        }
        if (gesture == Gesture.BallInHandDrag)
        {
            for (int k = 1; k <= 12; k++) gs.MouseMove(M(P(Vec2.Lerp(pointAt, cuePos, k / 12.0))));
            pressAt = P(cuePos);
        }
        var predicted = gs.PredictedHit();
        double aimAtPress = gs.AimAngle;
        gs.MouseDown(M(pressAt));
        double pull = power * 260;
        Vec2 from = gesture == Gesture.DragFromTarget ? pointAt : cuePos;
        for (int k = 1; k <= 10; k++) gs.MouseMove(M(P(from - aimDir * (pull * k / 10.0))));
        double aimAtRelease = gs.AimAngle;
        gs.MouseUp(M(P(from - aimDir * pull)));

        for (int f = 0; f < 60 * 50 && (gs.ShotInProgress || !gs.ReadyForHuman); f++) gs.Update(1 / 60.0);

        var ev = gs.LastEvents;
        bool uiPot = ev.Pocketed.Contains(1) && !ev.CueScratched;
        double err = Math.Abs(Math.IEEERemainder(aimAtRelease - aimNeeded, 2 * Math.PI)) * 180 / Math.PI;

        t.N++;
        if (idealPot) t.IdealPots++;
        if (uiPot) t.UiPots++;
        if (idealPot && !uiPot) t.LostPots++;
        if (predicted is { } ph && ph.ball == 1) t.GuideRight++;
        if (ev.FirstContact != 1) t.WrongFirst++;
        t.AimErrSum += err;
        t.AimErrMax = Math.Max(t.AimErrMax, err);
    }
}

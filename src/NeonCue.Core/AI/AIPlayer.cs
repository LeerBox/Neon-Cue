namespace NeonCue.Core;

/// <summary>A planned shot. <see cref="Placement"/> is set when the AI had ball in hand.</summary>
public sealed class AIShot
{
    public Vec2? Placement;
    public double Angle;
    public double Power;
    public double SpinSide;
    public double SpinTop;
    public string Plan = "";
    public double Score;
}

/// <summary>How hard a level thinks and how steady its stroke is.</summary>
public sealed record AIProfile(
    int Candidates,
    double[] PowerMuls,
    (double Side, double Top)[] Spins,
    double PositionWeight,
    bool Safety,
    int SafetySweep,
    bool Banks,
    bool Combos,
    int RobustSamples,
    double AngleNoiseDeg,
    double PowerNoise,
    int PickTop,
    int Placements);

/// <summary>
/// Shot-planning AI. It enumerates geometric shots (direct, combination and bank),
/// plays every variation of power and spin through the real physics engine, judges the
/// result with the real rules, and scores the resulting table: winning, keeping the
/// turn with good position, or leaving the opponent nothing. Stronger levels also
/// stress-test their pick against stroke error and fall back to safeties.
/// </summary>
public static class AIPlayer
{
    static double R => Table.R;
    const double WinScore = 12000;
    const double ContinueScore = 3000;
    const double FoulScore = -4000;

    public static AIProfile ProfileFor(AILevel level) => level switch
    {
        AILevel.Rookie => new AIProfile(
            Candidates: 3, PowerMuls: [1.0, 1.4], Spins: [(0, 0)],
            PositionWeight: 0, Safety: false, SafetySweep: 0, Banks: false, Combos: false,
            RobustSamples: 0, AngleNoiseDeg: 1.2, PowerNoise: 0.09, PickTop: 3, Placements: 2),
        AILevel.Amateur => new AIProfile(
            Candidates: 5, PowerMuls: [0.9, 1.15, 1.5], Spins: [(0, 0), (0, 0.5), (0, -0.5)],
            PositionWeight: 0.35, Safety: false, SafetySweep: 0, Banks: false, Combos: false,
            RobustSamples: 0, AngleNoiseDeg: 0.65, PowerNoise: 0.05, PickTop: 2, Placements: 4),
        AILevel.Pro => new AIProfile(
            Candidates: 10, PowerMuls: [0.8, 1.0, 1.25, 1.6], Spins: [(0, 0), (0, 0.6), (0, -0.6), (0.45, 0), (-0.45, 0)],
            PositionWeight: 0.8, Safety: true, SafetySweep: 0, Banks: true, Combos: true,
            RobustSamples: 4, AngleNoiseDeg: 0.25, PowerNoise: 0.025, PickTop: 1, Placements: 6),
        _ => new AIProfile(
            Candidates: 16, PowerMuls: [0.7, 0.85, 1.0, 1.2, 1.45, 1.8],
            Spins: [(0, 0), (0, 0.45), (0, 0.9), (0, -0.45), (0, -0.9), (0.5, 0.3), (-0.5, 0.3), (0.5, -0.4), (-0.5, -0.4)],
            PositionWeight: 1.0, Safety: true, SafetySweep: 120, Banks: true, Combos: true,
            RobustSamples: 8, AngleNoiseDeg: 0.07, PowerNoise: 0.01, PickTop: 1, Placements: 8),
    };

    public static AIShot Plan(GameState s, GameRules rules, AILevel level, int seed)
    {
        var prof = ProfileFor(level);
        var rng = new Random(seed);
        // Snooker break-off is a safety from the D, not a power break.
        if (s.IsBreakShot && rules.Mode == GameMode.Snooker) return PlanBallInHand(s, rules, prof, rng);
        if (s.IsBreakShot) return PlanBreak(s, rules, prof, level, rng);
        if (s.BallInHand) return PlanBallInHand(s, rules, prof, rng);
        return PlanShot(s, rules, prof, rng, allowSafety: true);
    }

    /// <summary>Applies the level's stroke error, plus the cue's own wobble, to a planned shot.</summary>
    public static void ApplyNoise(AIShot shot, AILevel level, CueSpec cue, Random rng)
    {
        var p = ProfileFor(level);
        shot.Angle += Gauss(rng) * StrokeNoiseDeg(p, cue, shot.Power) * Math.PI / 180;
        shot.Power = Math.Clamp(shot.Power * (1 + Gauss(rng) * p.PowerNoise), 0.03, 1);
    }

    static double StrokeNoiseDeg(AIProfile p, CueSpec cue, double power)
    {
        // A poor cue magnifies the player's own error as well as adding its wobble.
        double player = p.AngleNoiseDeg * (1 + 2 * cue.AimError);
        double w = cue.WobbleDeg(power);
        return Math.Sqrt(player * player + w * w);
    }

    static double Gauss(Random rng)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    // ===================================================================== planning

    sealed class Trial
    {
        public Vec2? Placement;
        public double Angle, Power, Side, Top;
        public string Plan = "";
        public double Score;
        public double Robust;
        public bool Success;
    }

    /// <summary>A shot idea; <see cref="Target"/> leaving towards <see cref="AimPoint"/> is the intent (throw is solved later).</summary>
    sealed record Candidate(double Angle, double Power, double Quality, string Plan, int Target = -1, Vec2 AimPoint = default);

    /// <summary>Leave two cores free so the game keeps drawing smoothly while the CPU thinks.</summary>
    static readonly ParallelOptions Workers = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };

    static AIShot ToShot(Trial t) => new()
    {
        Placement = t.Placement,
        Angle = t.Angle,
        Power = t.Power,
        SpinSide = t.Side,
        SpinTop = t.Top,
        Plan = t.Plan,
        Score = t.Robust,
    };

    static AIShot PlanShot(GameState s, GameRules rules, AIProfile prof, Random rng, bool allowSafety)
    {
        var cands = Candidates(s, rules, prof)
            .OrderByDescending(c => c.Quality)
            .Take(prof.Candidates)
            .ToList();

        // Solve throw once per line and spin (at the planned speed) and reuse it across the power
        // variations; the remaining speed dependence of throw is tiny.
        var lines = new List<(Candidate c, double side, double top)>();
        foreach (var c in cands)
            foreach (var (side, top) in prof.Spins)
                lines.Add((c, side, top));
        var solved = new double[lines.Count];
        Parallel.For(0, lines.Count, Workers, i =>
        {
            var (c, side, top) = lines[i];
            solved[i] = c.Target > 0
                ? ShotSolver.RefineAim(s, c.Target, c.AimPoint, c.Angle, c.Power, side, top, s.CurrentPlayer.Cue, iterations: 3)
                : c.Angle;
        });

        var trials = new List<Trial>();
        for (int i = 0; i < lines.Count; i++)
        {
            var (c, side, top) = lines[i];
            foreach (var m in prof.PowerMuls)
                trials.Add(new Trial { Angle = solved[i], Power = Math.Clamp(c.Power * m, 0.04, 1), Side = side, Top = top, Plan = c.Plan });
        }
        RunTrials(s, rules, prof, trials);

        Trial? best = null;
        var successes = trials.Where(t => t.Success).OrderByDescending(t => t.Score).ToList();
        if (successes.Count > 0)
        {
            if (prof.RobustSamples > 0)
            {
                var top = PickDiverse(successes, 4);
                RobustScores(s, rules, prof, top);
                best = top.OrderByDescending(t => t.Robust).First();
            }
            else
            {
                best = successes[rng.Next(Math.Min(prof.PickTop, successes.Count))];
                best.Robust = best.Score;
            }
        }

        bool shaky = best == null || best.Robust < 2600;
        if (shaky && ((allowSafety && prof.Safety) || best == null))
        {
            var safeties = SafetyTrials(s, rules, prof);
            RunTrials(s, rules, prof, safeties);
            var bs = safeties.OrderByDescending(t => t.Score).FirstOrDefault();
            if (bs != null)
            {
                bs.Robust = bs.Score;
                if (string.IsNullOrEmpty(bs.Plan)) bs.Plan = bs.Success ? "Kick or carom pot" : "Safety";
                if (best == null || bs.Robust > best.Robust) best = bs;
            }
        }

        best ??= trials.OrderByDescending(t => t.Score).FirstOrDefault() ?? DefaultTrial(s, rules);
        return ToShot(best);
    }

    /// <summary>Top trials, skipping near-duplicates of the same line so robustness compares real alternatives.</summary>
    static List<Trial> PickDiverse(List<Trial> sorted, int count)
    {
        var picked = new List<Trial>();
        foreach (var t in sorted)
        {
            if (picked.Any(p => Math.Abs(p.Angle - t.Angle) < 1e-4 && Math.Abs(p.Power - t.Power) < 0.02 && p.Side == t.Side && p.Top == t.Top)) continue;
            picked.Add(t);
            if (picked.Count >= count) break;
        }
        return picked;
    }

    static void RobustScores(GameState s, GameRules rules, AIProfile prof, List<Trial> top)
    {
        int n = prof.RobustSamples;
        double na = Math.Max(StrokeNoiseDeg(prof, s.CurrentPlayer.Cue, top.Count > 0 ? top[0].Power : 0.5), 0.12) * Math.PI / 180;
        double np = Math.Max(prof.PowerNoise, 0.02);
        var perturbed = new List<(Trial parent, Trial t)>();
        foreach (var t in top)
        {
            for (int k = 0; k < n; k++)
            {
                double u = (n == 1 ? 0 : k / (n - 1.0) * 2 - 1) * 1.6;
                double pf = 1 + np * (k % 2 == 0 ? 1.3 : -1.3);
                perturbed.Add((t, new Trial
                {
                    Placement = t.Placement,
                    Angle = t.Angle + u * na,
                    Power = Math.Clamp(t.Power * pf, 0.03, 1),
                    Side = t.Side,
                    Top = t.Top,
                }));
            }
        }
        RunTrials(s, rules, prof, perturbed.Select(p => p.t).ToList());
        foreach (var t in top)
        {
            var mine = perturbed.Where(p => p.parent == t).Select(p => p.t.Score).ToList();
            t.Robust = (t.Score + mine.Sum()) / (mine.Count + 1);
        }
    }

    static void RunTrials(GameState s, GameRules rules, AIProfile prof, List<Trial> trials)
    {
        Parallel.For(0, trials.Count, Workers, i =>
        {
            var t = trials[i];
            t.Score = Evaluate(s, rules, prof, t, out t.Success);
        });
    }

    /// <summary>Plays the shot on a copy of the table and scores the result from the shooter's view.</summary>
    static double Evaluate(GameState s, GameRules rules, AIProfile prof, Trial t, out bool success)
    {
        int me = s.Current;
        var c = s.Clone();
        if (t.Placement is Vec2 pl) c.Cue.Pos = pl;
        c.BallInHand = false;
        int scoreBefore = c.Players[me].Score;
        int oppBefore = c.Players[c.PlayerCount > 1 ? 1 - me : me].Score;
        Physics.Strike(c.Cue, t.Angle, t.Power, t.Side, t.Top, c.Players[me].Cue, c.IsBreakShot);
        var ev = Physics.Simulate(c.Balls);
        var o = rules.ApplyShot(c, ev, new Random(7));
        bool potted = ev.PocketedObjects.Any();
        // Points games: value the swing (my points minus what a foul hands the opponent).
        int oppGain = c.PlayerCount > 1 ? c.Players[1 - me].Score - oppBefore : 0;
        double pts = rules.IsPointsGame ? (c.Players[me].Score - scoreBefore - oppGain) * 60 : 0;

        if (o.RackOver)
        {
            success = o.Winner == me;
            return success ? WinScore : -WinScore;
        }
        if (o.Foul)
        {
            success = false;
            return FoulScore + pts - (c.PlayerCount > 1 ? 800 : 0);
        }
        if (c.Current == me && potted)
        {
            success = true;
            return ContinueScore + pts + prof.PositionWeight * 2000 * BestShotQuality(c, rules);
        }
        success = false;
        if (c.PlayerCount == 1) return -1500 + 1000 * BestShotQuality(c, rules);
        return pts - 2500 * BestShotQuality(c, rules);
    }

    /// <summary>How good the best direct pot is for whoever is to shoot (0 = nothing, ~1 = hanger).</summary>
    public static double BestShotQuality(GameState st, GameRules rules)
    {
        if (st.BallInHand) return 1.0;
        double best = 0;
        Vec2 cue = st.Cue.Pos;
        foreach (var t in st.Balls)
        {
            if (t.Number == 0 || t.Pocketed) continue;
            if (!rules.IsLegalFirstContact(st, t.Number) || !rules.IsGoodPot(st, t.Number)) continue;
            foreach (var p in Table.Pockets)
            {
                double q = DirectQuality(st, cue, 0, t, p, out _, out _, out _, out _);
                if (q > best) best = q;
            }
        }
        return best;
    }

    // ===================================================================== geometry

    static bool PathClear(GameState s, Vec2 a, Vec2 b, int ex1, int ex2)
    {
        double clearance = 2 * R * 0.98;
        foreach (var o in s.Balls)
        {
            if (o.Pocketed || o.Number == ex1 || o.Number == ex2) continue;
            if (Vec2.DistanceToSegment(o.Pos, a, b) < clearance) return false;
        }
        return true;
    }

    /// <summary>
    /// Quality of sending <paramref name="t"/> into <paramref name="p"/> with a ball travelling
    /// from <paramref name="from"/>. Returns 0 when blocked or impossible.
    /// </summary>
    static double DirectQuality(GameState s, Vec2 from, int fromNumber, Ball t, Pocket p,
        out Vec2 ghost, out double cosCut, out double d1, out double d2)
    {
        ghost = Vec2.Zero; cosCut = 0; d1 = 0; d2 = 0;
        // A ball already hanging in the jaws is pushed into the hole, not at the mouth.
        Vec2 aim = Vec2.Distance(t.Pos, p.Aim) < 3 * R ? p.Center : p.Aim;
        Vec2 tp = aim - t.Pos;
        d2 = tp.Length;
        if (d2 < 1) return 0;
        Vec2 dir = tp / d2;

        double approach = Math.Acos(Math.Clamp(Vec2.Dot(dir, -p.Inward), -1, 1));
        if (approach > (p.IsCorner ? 1.15 : 0.95)) return 0;

        ghost = t.Pos - dir * (2 * R);
        Vec2 toGhost = ghost - from;
        d1 = toGhost.Length;
        if (d1 < 1e-6) return 0;
        cosCut = Vec2.Dot(toGhost / d1, dir);
        if (cosCut < 0.2) return 0;

        if (!PathClear(s, from, ghost, fromNumber, t.Number)) return 0;
        if (!PathClear(s, t.Pos, aim, t.Number, fromNumber)) return 0;

        double q = Math.Pow(cosCut, 1.6) * Math.Exp(-(d1 * 0.6 + d2) / 1100.0);
        q *= p.IsCorner ? Math.Cos(approach * 0.5) : 0.7 + 0.3 * Math.Cos(approach);
        return q;
    }

    /// <summary>Power for the object ball to reach the pocket with a little to spare (real cloth friction).</summary>
    static double EstimatePower(double d1, double d2, double cosCut)
    {
        double vObj = Physics.SpeedToTravel(d2 + 6 * R) * 1.15;
        double vImp = vObj / (Math.Max(cosCut, 0.3) * 0.975);
        double vTravel = Physics.SpeedToTravel(d1);
        double v0 = Math.Sqrt(vImp * vImp + vTravel * vTravel);
        return Math.Clamp(Physics.PowerForSpeedFraction(v0 / Physics.MaxShotSpeed), 0.04, 1.0);
    }

    static List<Candidate> Candidates(GameState s, GameRules rules, AIProfile prof)
    {
        var list = new List<Candidate>();
        Vec2 cue = s.Cue.Pos;
        var legal = s.Balls.Where(b => b.Number != 0 && !b.Pocketed && rules.IsLegalFirstContact(s, b.Number)).ToList();

        foreach (var t in legal)
        {
            bool good = rules.IsGoodPot(s, t.Number);
            if (good)
            {
                foreach (var p in Table.Pockets)
                {
                    double q = DirectQuality(s, cue, 0, t, p, out var g, out var cc, out var d1, out var d2);
                    if (q > 0)
                    {
                        Vec2 aim = Vec2.Distance(t.Pos, p.Aim) < 3 * R ? p.Center : p.Aim;
                        list.Add(new Candidate((g - cue).Angle, EstimatePower(d1, d2, cc), q, $"{t.Number} in the {p.Name}", t.Number, aim));
                    }
                    if (prof.Banks) AddBanks(s, cue, t, p, list);
                }
            }

            if (prof.Combos)
            {
                foreach (var o in s.Balls)
                {
                    if (o.Number == 0 || o.Pocketed || o == t || !rules.IsGoodPot(s, o.Number)) continue;
                    if (Vec2.Distance(o.Pos, t.Pos) > 450) continue;
                    foreach (var p in Table.Pockets)
                        AddCombo(s, cue, t, o, p, list);
                }
            }
        }
        return list;
    }

    static void AddCombo(GameState s, Vec2 cue, Ball t, Ball o, Pocket p, List<Candidate> list)
    {
        double qo = DirectQuality(s, t.Pos, t.Number, o, p, out var g2, out var cc2, out var dT, out var dO);
        if (qo <= 0 || cc2 < 0.5) return;
        Vec2 dirT = (g2 - t.Pos).Normalized();
        Vec2 g1 = t.Pos - dirT * (2 * R);
        Vec2 cg = g1 - cue;
        double l1 = cg.Length;
        if (l1 < 1) return;
        double cc1 = Vec2.Dot(cg / l1, dirT);
        if (cc1 < 0.5) return;
        if (!PathClear(s, cue, g1, 0, t.Number)) return;

        double q = qo * cc1 * cc1 * Math.Exp(-l1 / 1500) * 0.6;
        double vO = Physics.SpeedToTravel(dO + 6 * R) * 1.15;
        double vT = Math.Sqrt(Math.Pow(vO / (cc2 * 0.975), 2) + Math.Pow(Physics.SpeedToTravel(dT), 2));
        double v0 = Math.Sqrt(Math.Pow(vT / (cc1 * 0.975), 2) + Math.Pow(Physics.SpeedToTravel(l1), 2));
        double pw = Math.Clamp(Physics.PowerForSpeedFraction(v0 / Physics.MaxShotSpeed), 0.04, 1);
        list.Add(new Candidate(cg.Angle, pw, q, $"Combo {t.Number} → {o.Number} in the {p.Name}", t.Number, g2));
    }

    static void AddBanks(GameState s, Vec2 cue, Ball t, Pocket p, List<Candidate> list)
    {
        const double W = Table.Width, H = Table.Height;
        for (int rail = 0; rail < 4; rail++)
        {
            Vec2 m;
            switch (rail)
            {
                case 0: if (p.Aim.Y < H / 2) continue; m = new Vec2(p.Aim.X, 2 * R - p.Aim.Y); break;
                case 1: if (p.Aim.Y > H / 2) continue; m = new Vec2(p.Aim.X, 2 * (H - R) - p.Aim.Y); break;
                case 2: if (p.Aim.X < W * 0.1) continue; m = new Vec2(2 * R - p.Aim.X, p.Aim.Y); break;
                default: if (p.Aim.X > W * 0.9) continue; m = new Vec2(2 * (W - R) - p.Aim.X, p.Aim.Y); break;
            }

            Vec2 dir = (m - t.Pos).Normalized();
            // Where the object ball meets the cushion line.
            double k = rail switch
            {
                0 => (R - t.Pos.Y) / dir.Y,
                1 => (H - R - t.Pos.Y) / dir.Y,
                2 => (R - t.Pos.X) / dir.X,
                _ => (W - R - t.Pos.X) / dir.X,
            };
            if (double.IsNaN(k) || k <= 0) continue;
            Vec2 bounce = t.Pos + dir * k;
            if (rail < 2)
            {
                double x = bounce.X;
                bool onRail = (x > Table.CornerGap + R && x < W / 2 - Table.SideGap - R) || (x > W / 2 + Table.SideGap + R && x < W - Table.CornerGap - R);
                if (!onRail) continue;
            }
            else if (bounce.Y < Table.CornerGap + R || bounce.Y > H - Table.CornerGap - R) continue;

            Vec2 g = t.Pos - dir * (2 * R);
            Vec2 cg = g - cue;
            double d1 = cg.Length;
            if (d1 < 1) continue;
            double cc = Vec2.Dot(cg / d1, dir);
            if (cc < 0.45) continue;
            if (!PathClear(s, cue, g, 0, t.Number) || !PathClear(s, t.Pos, bounce, t.Number, 0) || !PathClear(s, bounce, p.Aim, t.Number, 0)) continue;

            double dist = k + Vec2.Distance(bounce, p.Aim);
            double q = Math.Pow(cc, 1.6) * Math.Exp(-(d1 * 0.6 + dist) / 1100.0) * 0.45;
            list.Add(new Candidate(cg.Angle, EstimatePower(d1, dist * 1.3 + 60, cc), q, $"Bank {t.Number} in the {p.Name}", t.Number, m));
        }
    }

    /// <summary>Shots aimed across the face of every legal ball (plus a full sweep for Master) to find safeties.</summary>
    static List<Trial> SafetyTrials(GameState s, GameRules rules, AIProfile prof)
    {
        var list = new List<Trial>();
        Vec2 cue = s.Cue.Pos;
        double[] powers = prof.Safety ? [0.12, 0.2, 0.32, 0.5] : [0.25, 0.45];
        double[] offsets = prof.Safety ? [-0.9, -0.6, -0.3, 0, 0.3, 0.6, 0.9] : [-0.5, 0, 0.5];

        // The eight nearest legal balls are where realistic safeties come from (15 reds would
        // multiply the search without adding options).
        var legal = s.Balls.Where(t => t.Number != 0 && !t.Pocketed && rules.IsLegalFirstContact(s, t.Number))
            .OrderBy(t => Vec2.DistanceSq(t.Pos, cue)).Take(8);
        foreach (var t in legal)
        {
            Vec2 v = t.Pos - cue;
            double dist = v.Length;
            if (dist < 2 * R) continue;
            double half = Math.Asin(Math.Min(1, 2 * R / dist));
            foreach (var off in offsets)
                foreach (var pw in powers)
                    list.Add(new Trial { Angle = v.Angle + off * half, Power = pw });
        }

        for (int i = 0; i < prof.SafetySweep; i++)
            foreach (var pw in new[] { 0.2, 0.4 })
                list.Add(new Trial { Angle = i * 2 * Math.PI / prof.SafetySweep, Power = pw });

        return list;
    }

    static Trial DefaultTrial(GameState s, GameRules rules)
    {
        var target = s.Balls.Where(b => b.Number != 0 && !b.Pocketed && rules.IsLegalFirstContact(s, b.Number))
            .OrderBy(b => Vec2.Distance(b.Pos, s.Cue.Pos)).FirstOrDefault()
            ?? s.Balls.FirstOrDefault(b => b.Number != 0 && !b.Pocketed);
        double angle = target != null ? (target.Pos - s.Cue.Pos).Angle : 0;
        return new Trial { Angle = angle, Power = 0.4, Plan = "Hit and hope" };
    }

    // ===================================================================== ball in hand & break

    static AIShot PlanBallInHand(GameState s, GameRules rules, AIProfile prof, Random rng)
    {
        double[] offs = prof.Placements >= 6 ? [0, 12, -12, 25, -25, 38, -38] : [0, 15, -15];
        double[] dists = [60, 120, 200];
        var spots = new List<(Vec2 pos, double q)>();

        foreach (var t in s.Balls)
        {
            if (t.Number == 0 || t.Pocketed || !rules.IsLegalFirstContact(s, t.Number) || !rules.IsGoodPot(s, t.Number)) continue;
            foreach (var p in Table.Pockets)
            {
                Vec2 dir = (p.Aim - t.Pos).Normalized();
                Vec2 g = t.Pos - dir * (2 * R);
                foreach (var off in offs)
                    foreach (var dist in dists)
                    {
                        Vec2 pos = g - dir.Rotated(off * Math.PI / 180) * dist;
                        if (!s.IsValidCuePlacement(pos)) continue;
                        double q = DirectQuality(s, pos, 0, t, p, out _, out _, out _, out _);
                        if (q > 0) spots.Add((pos, off == 0 ? q * 0.95 : q));
                    }
            }
        }

        var chosen = new List<Vec2>();
        foreach (var sp in spots.OrderByDescending(x => x.q))
        {
            if (chosen.Any(c => Vec2.Distance(c, sp.pos) < 25)) continue;
            chosen.Add(sp.pos);
            if (chosen.Count >= prof.Placements) break;
        }

        if (chosen.Count == 0)
        {
            // Nothing pottable: pick the spot that gives the best safety chances later.
            for (int i = 0; i < 400 && chosen.Count < 4; i++)
            {
                Vec2 pos;
                if (s.KitchenOnly && Table.HasD)
                {
                    double a = Math.PI / 2 + rng.NextDouble() * Math.PI, rad = Math.Sqrt(rng.NextDouble()) * Table.DRadius;
                    pos = Table.DCenter + Vec2.FromAngle(a) * rad;
                }
                else
                {
                    double maxX = s.KitchenOnly ? Table.HeadStringX : Table.Width - R;
                    pos = new Vec2(R + rng.NextDouble() * (maxX - R), R + rng.NextDouble() * (Table.Height - 2 * R));
                }
                if (s.IsValidCuePlacement(pos)) chosen.Add(pos);
            }
            if (chosen.Count == 0) chosen.Add(s.FindFreeSpot(Table.HeadSpot, 0));
        }

        var reduced = prof with { Candidates = Math.Min(prof.Candidates, 3) };
        AIShot? best = null;
        for (int i = 0; i < chosen.Count; i++)
        {
            var c = s.Clone();
            c.Cue.Pos = chosen[i];
            c.BallInHand = false;
            var shot = PlanShot(c, rules, i == 0 ? prof : reduced, rng, allowSafety: i == 0);
            shot.Placement = chosen[i];
            if (best == null || shot.Score > best.Score) best = shot;
        }
        return best!;
    }

    static AIShot PlanBreak(GameState s, GameRules rules, AIProfile prof, AILevel level, Random rng)
    {
        const double H = Table.Height;
        var apex = s.ObjectBallsOnTable.OrderBy(b => b.Pos.X).First();
        double x = Table.HeadStringX - 25;
        double power = level switch { AILevel.Rookie => 0.82, AILevel.Amateur => 0.9, _ => 1.0 };

        double[] ys = level switch
        {
            AILevel.Master => [H / 2, H / 2 - 55, H / 2 + 55, H / 2 - 120, H / 2 + 120],
            AILevel.Pro => [H / 2, H / 2 - 70, H / 2 + 70],
            _ => [H / 2 + (rng.NextDouble() - 0.5) * 80],
        };
        double[] offsets = level >= AILevel.Pro ? [0, 0.2, -0.2] : [0];

        var trials = new List<Trial>();
        foreach (var y in ys)
        {
            var pos = new Vec2(x, y);
            if (!s.IsValidCuePlacement(pos)) pos = s.FindFreeSpot(pos, 0);
            Vec2 dir = (apex.Pos - pos).Normalized();
            foreach (var off in offsets)
            {
                Vec2 target = apex.Pos + dir.Perp() * (off * R);
                trials.Add(new Trial { Placement = pos, Angle = (target - pos).Angle, Power = power, Plan = "Break" });
            }
        }

        if (level < AILevel.Pro) return ToShot(trials[0]);

        RunTrials(s, rules, prof, trials);
        var best = trials.OrderByDescending(t => t.Score).First();
        best.Robust = best.Score;
        return ToShot(best);
    }
}

// Headless CPU-vs-CPU simulator: plays racks for every mode/level and checks
// rules, AI and physics invariants. Usage:
//   dotnet run --project tests/NeonCue.Sim -- [racks=3] [level|all] [mode|all]
//   dotnet run --project tests/NeonCue.Sim -- duel [racks=10]
using System.Diagnostics;
using NeonCue.Core;

if (args.Length > 0 && args[0] == "cues")
{
    // Same AI level, different cue: does equipment matter?
    int n = args.Length > 1 ? int.Parse(args[1]) : 12;
    CueDuel(AILevel.Pro, "house", "royale", GameMode.EightBall, n);
    CueDuel(AILevel.Pro, "starter", "phantom", GameMode.NineBall, n);
    CueDuel(AILevel.Pro, "house", "viper", GameMode.EightBall, n);
    return 0;
}

if (args.Length > 0 && args[0] == "duel")
{
    int n = args.Length > 1 ? int.Parse(args[1]) : 10;
    Duel(AILevel.Master, AILevel.Amateur, GameMode.EightBall, n);
    Duel(AILevel.Pro, AILevel.Rookie, GameMode.NineBall, n);
    return 0;
}

int racks = args.Length > 0 ? int.Parse(args[0]) : 3;
AILevel[] levels = args.Length > 1 && args[1] != "all" ? [Enum.Parse<AILevel>(args[1], true)] : Enum.GetValues<AILevel>();
GameMode[] modes = args.Length > 2 && args[2] != "all" ? [Enum.Parse<GameMode>(args[2], true)] : Enum.GetValues<GameMode>();

int failures = 0;
Console.WriteLine($"{"mode",-13} {"level",-8} {"racks",5} {"shots/r",8} {"pot%",6} {"fouls/r",8} {"avg ms",7} {"max ms",7} {"unfin",6} {"anom",5}");
foreach (var mode in modes)
{
    foreach (var level in levels)
    {
        var st = new Stats();
        for (int r = 0; r < racks; r++) PlayRack(mode, level, level, 1000 + r * 37, st);
        Console.WriteLine($"{mode,-13} {level,-8} {racks,5} {st.Shots / (double)racks,8:0.0} {100.0 * st.Pots / Math.Max(1, st.Attempts),6:0} {st.Fouls / (double)racks,8:0.0} {st.PlanMs / Math.Max(1, st.Shots),7:0} {st.MaxMs,7:0} {st.Unfinished,6} {st.Anomalies,5}");
        failures += st.Anomalies;
    }
}
Console.WriteLine(failures == 0 ? "OK: no physics/rules anomalies" : $"FAIL: {failures} anomalies");
return failures == 0 ? 0 : 1;

static void Duel(AILevel a, AILevel b, GameMode mode, int racks)
{
    int winsA = 0;
    for (int r = 0; r < racks; r++)
    {
        var st = new Stats();
        int w = PlayRack(mode, a, b, 5000 + r * 11, st, breaker: r % 2);
        if (w == 0) winsA++;
    }
    Console.WriteLine($"{mode}: {a} beat {b} in {winsA}/{racks} racks");
}

static void CueDuel(AILevel level, string cueA, string cueB, GameMode mode, int racks)
{
    int winsB = 0;
    for (int r = 0; r < racks; r++)
    {
        int w = PlayRack(mode, level, level, 9000 + r * 13, new Stats(), breaker: r % 2,
            cues: [CueCatalog.Get(cueA), CueCatalog.Get(cueB)]);
        if (w == 1) winsB++;
    }
    Console.WriteLine($"{mode} ({level} vs {level}): {CueCatalog.Get(cueB).Name} beat {CueCatalog.Get(cueA).Name} in {winsB}/{racks} racks");
}

static int PlayRack(GameMode mode, AILevel l0, AILevel l1, int seed, Stats st, int breaker = 0, CueSpec[]? cues = null)
{
    var rng = new Random(seed);
    var rules = GameRules.Create(mode);
    var s = new GameState
    {
        Mode = mode,
        RaceTo = 1,
        TargetScore = mode == GameMode.Rotation ? 61 : 25,
        Breaker = breaker,
        Players = rules.IsSolo
            ? [new PlayerState { Name = "Solo", IsAI = true, Level = l0, Cue = CueCatalog.ForAI(l0) }]
            : [new PlayerState { Name = "A", IsAI = true, Level = l0, Cue = CueCatalog.ForAI(l0) }, new PlayerState { Name = "B", IsAI = true, Level = l1, Cue = CueCatalog.ForAI(l1) }],
    };
    if (cues != null)
        for (int i = 0; i < s.PlayerCount; i++) s.Players[i].Cue = cues[i];
    rules.NewRack(s, rng);

    int maxShots = rules.IsSolo ? 40 : 250;
    int shots = 0;
    while (!s.RackOver && shots < maxShots)
    {
        if (rules.ShouldConcede(s, s.Current))
        {
            if (Environment.GetEnvironmentVariable("SIM_VERBOSE") == "1")
                Console.WriteLine($"  [{mode}] {s.CurrentPlayer.Name} concedes at {s.Players[0].Score}-{s.Players[1].Score}");
            rules.ConcedeFrame(s, s.Current);
            break;
        }
        var sw = Stopwatch.StartNew();
        var lvl = s.CurrentPlayer.Level;
        var shot = AIPlayer.Plan(s, rules, lvl, rng.Next());
        double ms = sw.Elapsed.TotalMilliseconds;
        st.PlanMs += ms;
        st.MaxMs = Math.Max(st.MaxMs, ms);
        AIPlayer.ApplyNoise(shot, lvl, s.CurrentPlayer.Cue, rng);

        if (shot.Placement is Vec2 p)
        {
            if (!s.BallInHand || !s.IsValidCuePlacement(p))
            {
                Console.WriteLine($"  anomaly: invalid placement {p} (ballInHand={s.BallInHand}, kitchen={s.KitchenOnly})");
                st.Anomalies++;
            }
            s.Cue.Pos = p;
        }
        s.BallInHand = false;
        bool wasBreak = s.IsBreakShot;
        int shooter = s.Current;

        Physics.Strike(s.Cue, shot.Angle, shot.Power, shot.SpinSide, shot.SpinTop, s.CurrentPlayer.Cue, wasBreak);
        var ev = Physics.Simulate(s.Balls);
        var o = rules.ApplyShot(s, ev, rng);
        shots++;
        st.Shots++;
        if (!wasBreak) st.Attempts++;
        if (!wasBreak && !o.Foul && ev.PocketedObjects.Any()) st.Pots++;
        if (o.Foul) st.Fouls++;

        foreach (var b in s.Balls)
        {
            if (b.Pocketed) continue;
            // Balls may hang in a pocket's jaws, but never more than 0.4R past the cushion nose.
            double lip = 0.6 * Table.R - 0.01;
            if (b.Pos.X < lip || b.Pos.X > Table.Width - lip || b.Pos.Y < lip || b.Pos.Y > Table.Height - lip)
            {
                Console.WriteLine($"  anomaly: ball {b.Number} resting off the playfield at {b.Pos}");
                st.Anomalies++;
            }
            if (b.IsMoving) { Console.WriteLine($"  anomaly: ball {b.Number} still moving"); st.Anomalies++; }
        }
        for (int i = 0; i < s.Balls.Length; i++)
            for (int j = i + 1; j < s.Balls.Length; j++)
            {
                var a = s.Balls[i]; var b = s.Balls[j];
                if (!a.Pocketed && !b.Pocketed && Vec2.Distance(a.Pos, b.Pos) < 2 * Table.R - 0.5)
                {
                    Console.WriteLine($"  anomaly: balls {a.Number} & {b.Number} overlap");
                    st.Anomalies++;
                }
            }
        if (Environment.GetEnvironmentVariable("SIM_VERBOSE") == "1")
            Console.WriteLine($"  [{mode}] {s.Players[shooter].Name}: {shot.Plan} p={shot.Power:0.00} -> pots[{string.Join(",", ev.PocketedObjects)}] {(o.Foul ? "FOUL " + o.FoulReason : "")} score {s.Players[0].Score}-{(s.PlayerCount > 1 ? s.Players[1].Score : 0)}");
    }
    if (!s.RackOver && !rules.IsSolo) st.Unfinished++;
    return s.RackWinner;
}

sealed class Stats
{
    public int Shots, Attempts, Pots, Fouls, Unfinished, Anomalies;
    public double PlanMs, MaxMs;
}

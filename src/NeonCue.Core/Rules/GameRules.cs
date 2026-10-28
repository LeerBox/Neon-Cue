namespace NeonCue.Core;

public sealed class ShotOutcome
{
    public bool Foul;
    public string? FoulReason;
    public bool TurnContinues;
    public bool RackOver;
    public int Winner = -1;
    public int PointsScored;
    public bool Reracked;
    /// <summary>Grant ball in hand even without a foul (e.g. practice re-rack).</summary>
    public bool ForceBallInHand;
    public bool ForceKitchen;
    public readonly List<string> Messages = new();
}

/// <summary>Base class for every game mode: racking, legality, judging a shot and passing the turn.</summary>
public abstract class GameRules
{
    protected static double R => Table.R;

    public abstract GameMode Mode { get; }
    public abstract string Name { get; }
    public abstract string Tagline { get; }
    public virtual int BallCount => 15;
    public virtual bool UsesRaceTo => true;
    public virtual bool IsPointsGame => false;
    public virtual bool BreakScratchToKitchen => false;
    public virtual bool ThreeFoulRule => false;
    public virtual bool RequireLegalFirstContactOnBreak => false;
    public virtual bool IsSolo => false;
    public virtual TableKind TableKind => TableKind.Pool;
    /// <summary>Whether a foul hands the opponent ball in hand (snooker: only after an in-off).</summary>
    protected virtual bool FoulGivesBallInHand => true;

    public static GameRules Create(GameMode mode) => mode switch
    {
        GameMode.EightBall => new EightBallRules(),
        GameMode.NineBall => new NineBallRules(),
        GameMode.TenBall => new TenBallRules(),
        GameMode.StraightPool => new StraightPoolRules(),
        GameMode.Rotation => new Rotation61Rules(),
        GameMode.Practice => new PracticeRules(),
        GameMode.Snooker => new SnookerRules(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // ---------------------------------------------------------------- racking

    public void NewRack(GameState s, Random rng)
    {
        Table.Use(TableKind);
        s.Balls = new Ball[BallCount + 1];
        for (int i = 0; i <= BallCount; i++) s.Balls[i] = new Ball(i, Vec2.Zero) { Pocketed = i != 0 };
        s.Cue.Pos = Table.HeadSpot;
        PlaceRack(s, rng);
        s.IsBreakShot = true;
        s.BallInHand = true;
        s.KitchenOnly = true;
        s.TableOpen = true;
        s.RackOver = false;
        s.RackWinner = -1;
        s.Current = s.Breaker;
        foreach (var p in s.Players)
        {
            p.Group = Group.None;
            p.ConsecutiveFouls = 0;
        }
        OnNewRack(s);
    }

    /// <summary>Per-mode reset at the start of every rack/frame.</summary>
    protected virtual void OnNewRack(GameState s) { }

    /// <summary>Whether a player should give up the current frame (snooker: needs impossible snookers).</summary>
    public virtual bool ShouldConcede(GameState s, int player) => false;

    /// <summary>Ends the rack with <paramref name="loser"/> conceding it.</summary>
    public void ConcedeFrame(GameState s, int loser)
    {
        int winner = s.PlayerCount > 1 ? 1 - loser : loser;
        s.RackOver = true;
        s.RackWinner = winner;
        s.Players[winner].RacksWon++;
        s.MatchOver = !UsesRaceTo || s.Players[winner].RacksWon >= s.RaceTo;
        s.BallInHand = false;
    }

    public void StartNextRack(GameState s, Random rng)
    {
        s.Breaker = s.PlayerCount > 1 ? 1 - s.Breaker : 0;
        s.RackNumber++;
        NewRack(s, rng);
    }

    protected abstract void PlaceRack(GameState s, Random rng);

    protected static List<Vec2> TrianglePositions(int rows, Random rng)
    {
        var list = new List<Vec2>();
        double gap = 2 * R + 0.08;
        double dx = gap * Math.Sqrt(3) / 2;
        for (int r = 0; r < rows; r++)
            for (int i = 0; i <= r; i++)
                list.Add(Jitter(new Vec2(Table.FootSpot.X + r * dx, Table.FootSpot.Y + (i - r / 2.0) * gap), rng));
        return list;
    }

    protected static List<Vec2> DiamondPositions(Random rng)
    {
        var list = new List<Vec2>();
        double gap = 2 * R + 0.08;
        double dx = gap * Math.Sqrt(3) / 2;
        int[] counts = [1, 2, 3, 2, 1];
        for (int r = 0; r < counts.Length; r++)
            for (int i = 0; i < counts[r]; i++)
                list.Add(Jitter(new Vec2(Table.FootSpot.X + r * dx, Table.FootSpot.Y + (i - (counts[r] - 1) / 2.0) * gap), rng));
        return list;
    }

    static Vec2 Jitter(Vec2 p, Random rng) =>
        new(p.X + (rng.NextDouble() - 0.5) * 0.04, p.Y + (rng.NextDouble() - 0.5) * 0.04);

    protected static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    protected static void Put(GameState s, int number, Vec2 pos)
    {
        var b = s.Balls[number];
        b.Pos = pos;
        b.Vel = Vec2.Zero;
        b.Pocketed = false;
    }

    protected virtual void Respot(GameState s, int number)
    {
        var b = s.Balls[number];
        b.Pocketed = false;
        b.Vel = Vec2.Zero;
        b.Pos = s.FindFreeSpot(Table.FootSpot, number);
    }

    // ---------------------------------------------------------------- legality

    /// <summary>May the current player's cue ball contact this ball first?</summary>
    public abstract bool IsLegalFirstContact(GameState s, int number, Func<int, bool> onTable);

    public bool IsLegalFirstContact(GameState s, int number) =>
        IsLegalFirstContact(s, number, n => !s.Balls[n].Pocketed);

    /// <summary>Is pocketing this ball good for the current player (AI desire)?</summary>
    public virtual bool IsGoodPot(GameState s, int number) => number != 0;

    /// <summary>Ball the HUD should highlight as "next" (rotation games), or -1.</summary>
    public virtual int TargetBall(GameState s) => -1;

    public virtual string Status(GameState s, int player) => "";

    /// <summary>Balls to show as icons on a player's HUD card.</summary>
    public virtual IReadOnlyList<int> PlayerBalls(GameState s, int player) => [];

    public virtual string Hint(GameState s) => "";

    // ---------------------------------------------------------------- judging

    protected abstract ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTableBefore, bool isBreak, Random rng);

    /// <summary>Applies a finished shot: judges it, restores the cue ball and passes the turn.</summary>
    public ShotOutcome ApplyShot(GameState s, ShotEvents ev, Random rng)
    {
        var pocketedNow = ev.Pocketed;
        bool OnTableBefore(int n) => !s.Balls[n].Pocketed || pocketedNow.Contains(n);

        var shooter = s.CurrentPlayer;
        bool wasBreak = s.IsBreakShot;
        s.IsBreakShot = false;
        shooter.Shots++;

        var o = Judge(s, ev, OnTableBefore, wasBreak, rng);

        if (s.Cue.Pocketed)
        {
            s.Cue.Pocketed = false;
            s.Cue.Vel = Vec2.Zero;
            s.Cue.Pos = s.FindFreeSpot(Table.HeadSpot, 0);
        }
        s.ShotNumber++;

        if (o.Foul)
        {
            shooter.Fouls++;
            shooter.ConsecutiveFouls++;
        }
        else
        {
            shooter.ConsecutiveFouls = 0;
            shooter.Pots += ev.PocketedObjects.Count();
        }

        // Visit statistics for the scoreboard.
        int pottedNow = ev.PocketedObjects.Count();
        if (!o.Foul && pottedNow > 0 && (o.TurnContinues || o.RackOver))
        {
            s.Run += pottedNow;
            shooter.BestRun = Math.Max(shooter.BestRun, s.Run);
        }
        if (o.Foul || !o.TurnContinues || o.RackOver) s.Run = 0;

        if (!o.RackOver && o.Foul && ThreeFoulRule && shooter.ConsecutiveFouls >= 3 && s.PlayerCount > 1)
        {
            o.RackOver = true;
            o.Winner = s.Opponent;
            o.Messages.Add($"Three consecutive fouls. {shooter.Name} loses the rack");
        }

        if (o.RackOver)
        {
            s.RackOver = true;
            s.RackWinner = o.Winner;
            if (o.Winner >= 0) s.Players[o.Winner].RacksWon++;
            s.MatchOver = !UsesRaceTo || o.Winner < 0 || s.Players[o.Winner].RacksWon >= s.RaceTo;
            s.BallInHand = false;
            return o;
        }

        if (!o.TurnContinues && s.PlayerCount > 1) s.Current = s.Opponent;
        s.BallInHand = (o.Foul && FoulGivesBallInHand) || o.ForceBallInHand;
        s.KitchenOnly = s.BallInHand && (o.ForceKitchen || (wasBreak && BreakScratchToKitchen && ev.CueScratched));
        return o;
    }

    /// <summary>Fouls common to most pool games. Returns null when the shot is legal.</summary>
    protected string? StandardFoul(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak)
    {
        if (ev.CueScratched) return "Cue ball pocketed";
        if (ev.FirstContact < 0) return "No ball contacted";
        if ((!isBreak || RequireLegalFirstContactOnBreak) && !IsLegalFirstContact(s, ev.FirstContact, onTable))
            return $"Wrong ball first ({ev.FirstContact})";
        bool potted = ev.PocketedObjects.Any();
        if (isBreak)
        {
            if (!potted && ev.RailBalls.Count < 4) return "Illegal break. Four object balls must reach a cushion";
        }
        else if (!potted && !ev.RailAfterContact)
        {
            return "No cushion after contact";
        }
        return null;
    }
}

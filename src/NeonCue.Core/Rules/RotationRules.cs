namespace NeonCue.Core;

/// <summary>Games where the lowest numbered ball must always be contacted first.</summary>
public abstract class LowestBallFirstRules : GameRules
{
    public override bool RequireLegalFirstContactOnBreak => true;

    public override bool IsLegalFirstContact(GameState s, int n, Func<int, bool> onTable)
    {
        if (n <= 0) return false;
        for (int i = 1; i < s.Balls.Length; i++)
            if (onTable(i)) return i == n;
        return false;
    }

    public override int TargetBall(GameState s) => s.LowestOnTable();

    public override string Hint(GameState s)
    {
        int t = s.LowestOnTable();
        return t > 0 ? $"The {t}-ball is first" : "";
    }
}

/// <summary>9-Ball and 10-Ball: legally pocket the money ball to win the rack.</summary>
public abstract class MoneyBallRules : LowestBallFirstRules
{
    protected abstract int MoneyBall { get; }
    /// <summary>Whether a legal pot of the money ball while other balls remain wins (9-ball yes, 10-ball no).</summary>
    protected abstract bool EarlyMoneyBallWins { get; }

    public override bool ThreeFoulRule => true;

    protected override ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak, Random rng)
    {
        var o = new ShotOutcome();
        var me = s.CurrentPlayer;
        var objs = ev.PocketedObjects.ToList();
        string? foul = StandardFoul(s, ev, onTable, isBreak);

        if (objs.Contains(MoneyBall))
        {
            bool othersLeft = s.ObjectBallsOnTable.Any(b => b.Number != MoneyBall);
            bool wins = foul == null && (EarlyMoneyBallWins || !othersLeft) && !(isBreak && !EarlyMoneyBallWins);
            if (wins)
            {
                o.RackOver = true;
                o.Winner = s.Current;
                o.Messages.Add($"{me.Name} sinks the {MoneyBall}-ball.");
                return o;
            }
            Respot(s, MoneyBall);
            o.Messages.Add($"The {MoneyBall}-ball is respotted.");
        }

        if (foul != null)
        {
            o.Foul = true;
            o.FoulReason = foul;
            o.Messages.Add($"FOUL. {foul}");
            if (s.PlayerCount > 1 && me.ConsecutiveFouls == 1)
                o.Messages.Add($"{me.Name} has two consecutive fouls");
        }
        else
        {
            o.TurnContinues = objs.Count > 0;
        }
        return o;
    }

    public override string Status(GameState s, int player)
    {
        int f = s.Players[player].ConsecutiveFouls;
        return f > 0 ? $"{f} consecutive foul{(f > 1 ? "s" : "")}" : "Lowest ball first";
    }
}

public sealed class NineBallRules : MoneyBallRules
{
    public override GameMode Mode => GameMode.NineBall;
    public override string Name => "9-Ball";
    public override string Tagline => "Contact the lowest ball first. A legal 9-ball wins the rack.";
    public override int BallCount => 9;
    protected override int MoneyBall => 9;
    protected override bool EarlyMoneyBallWins => true;

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = DiamondPositions(rng);
        Put(s, 1, pos[0]);
        Put(s, 9, pos[4]);
        var rest = new List<int> { 2, 3, 4, 5, 6, 7, 8 };
        Shuffle(rest, rng);
        int k = 0;
        for (int i = 1; i < pos.Count; i++)
            if (i != 4) Put(s, rest[k++], pos[i]);
    }
}

public sealed class TenBallRules : MoneyBallRules
{
    public override GameMode Mode => GameMode.TenBall;
    public override string Name => "10-Ball";
    public override string Tagline => "Contact the lowest ball first. The 10-ball wins only as the last ball.";
    public override int BallCount => 10;
    protected override int MoneyBall => 10;
    protected override bool EarlyMoneyBallWins => false;

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = TrianglePositions(4, rng);
        Put(s, 1, pos[0]);
        Put(s, 10, pos[4]);
        var rest = new List<int> { 2, 3, 4, 5, 6, 7, 8, 9 };
        Shuffle(rest, rng);
        int k = 0;
        for (int i = 1; i < pos.Count; i++)
            if (i != 4) Put(s, rest[k++], pos[i]);
    }
}

/// <summary>Rotation: lowest ball first, every ball scores its number, first to 61 wins.</summary>
public sealed class Rotation61Rules : LowestBallFirstRules
{
    public override GameMode Mode => GameMode.Rotation;
    public override string Name => "Rotation 61";
    public override string Tagline => "Contact the lowest ball first. Each ball scores its number. First to 61.";
    public override bool UsesRaceTo => false;
    public override bool IsPointsGame => true;

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = TrianglePositions(5, rng);
        Put(s, 1, pos[0]);
        Put(s, 2, pos[10]);
        Put(s, 3, pos[14]);
        Put(s, 15, pos[4]);
        var rest = Enumerable.Range(4, 11).ToList();
        Shuffle(rest, rng);
        int k = 0;
        for (int i = 1; i < pos.Count; i++)
            if (i != 4 && i != 10 && i != 14) Put(s, rest[k++], pos[i]);
    }

    protected override ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak, Random rng)
    {
        var o = new ShotOutcome();
        var me = s.CurrentPlayer;
        var objs = ev.PocketedObjects.ToList();
        string? foul = StandardFoul(s, ev, onTable, isBreak);

        if (foul != null)
        {
            o.Foul = true;
            o.FoulReason = foul;
            o.Messages.Add($"FOUL. {foul}");
            foreach (var n in objs.OrderBy(n => n)) Respot(s, n);
            if (objs.Count > 0) o.Messages.Add("Pocketed balls are respotted.");
        }
        else
        {
            int pts = objs.Sum();
            me.Score += pts;
            o.PointsScored = pts;
            o.TurnContinues = objs.Count > 0;
            if (pts > 0) o.Messages.Add($"+{pts} points.");
        }

        int target = s.TargetScore;
        if (me.Score >= target)
        {
            o.RackOver = true;
            o.Winner = s.Current;
            o.Messages.Add($"{me.Name} reaches {target}.");
        }
        else if (!s.ObjectBallsOnTable.Any())
        {
            o.RackOver = true;
            o.Winner = s.Players[0].Score >= s.Players[s.PlayerCount - 1].Score ? 0 : s.PlayerCount - 1;
            o.Messages.Add("The table is clear.");
        }
        return o;
    }

    public override string Status(GameState s, int player) => $"{s.Players[player].Score} / {s.TargetScore} pts";
}

namespace NeonCue.Core;

/// <summary>Simplified 14.1 continuous: any ball, one point per ball, re-rack when one ball remains.</summary>
public sealed class StraightPoolRules : GameRules
{
    public override GameMode Mode => GameMode.StraightPool;
    public override string Name => "Straight Pool";
    public override string Tagline => "Any ball, any pocket. One point per ball, -1 per foul. Endless re-racks to the target.";
    public override bool UsesRaceTo => false;
    public override bool IsPointsGame => true;

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = TrianglePositions(5, rng);
        var nums = Enumerable.Range(1, 15).ToList();
        Shuffle(nums, rng);
        for (int i = 0; i < pos.Count; i++) Put(s, nums[i], pos[i]);
    }

    public override bool IsLegalFirstContact(GameState s, int n, Func<int, bool> onTable) => n > 0;

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
            me.Score -= 1;
            o.Messages.Add($"FOUL — {foul} (-1)");
            foreach (var n in objs) Respot(s, n);
        }
        else
        {
            me.Score += objs.Count;
            o.PointsScored = objs.Count;
            o.TurnContinues = objs.Count > 0;
        }

        if (me.Score >= s.TargetScore)
        {
            o.RackOver = true;
            o.Winner = s.Current;
            o.Messages.Add($"{me.Name} reaches {s.TargetScore}!");
            return o;
        }

        if (s.ObjectBallsOnTable.Count() <= 1)
        {
            Rerack(s, rng);
            o.Reracked = true;
            o.Messages.Add("Re-rack!");
        }
        return o;
    }

    /// <summary>Racks the pocketed balls, leaving the apex empty when one ball is still in play.</summary>
    internal static void Rerack(GameState s, Random rng)
    {
        var pos = TrianglePositions(5, rng);
        var remaining = s.ObjectBallsOnTable.ToList();
        var toRack = s.Balls.Where(b => b.Number != 0 && b.Pocketed).Select(b => b.Number).ToList();
        Shuffle(toRack, rng);
        int start = remaining.Count > 0 ? 1 : 0;

        // Move any ball sitting in the rack area out of the way first.
        double rackMinX = Table.FootSpot.X - 2 * R;
        foreach (var b in remaining.Append(s.Cue))
        {
            if (b.Pocketed) continue;
            if (b.Pos.X > rackMinX && Math.Abs(b.Pos.Y - Table.FootSpot.Y) < 6 * R)
            {
                b.Pocketed = true; // exclude itself while searching
                b.Pos = s.FindFreeSpot(b.Number == 0 ? Table.HeadSpot : Table.CenterSpot, b.Number);
                b.Pocketed = false;
            }
        }

        for (int i = 0; i < toRack.Count && start + i < pos.Count; i++)
            Put(s, toRack[i], pos[start + i]);
    }

    public override string Status(GameState s, int player) => $"{s.Players[player].Score} / {s.TargetScore} pts";
}

/// <summary>Solo free play: pot everything, no penalties, endless racks.</summary>
public sealed class PracticeRules : GameRules
{
    public override GameMode Mode => GameMode.Practice;
    public override string Name => "Practice";
    public override string Tagline => "Solo free play. No fouls, no pressure — sharpen your stroke and position.";
    public override bool UsesRaceTo => false;
    public override bool IsSolo => true;

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = TrianglePositions(5, rng);
        var nums = Enumerable.Range(1, 15).ToList();
        Shuffle(nums, rng);
        int eightIdx = nums.IndexOf(8);
        (nums[eightIdx], nums[4]) = (nums[4], nums[eightIdx]);
        for (int i = 0; i < pos.Count; i++) Put(s, nums[i], pos[i]);
    }

    public override bool IsLegalFirstContact(GameState s, int n, Func<int, bool> onTable) => n > 0;

    protected override ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak, Random rng)
    {
        var o = new ShotOutcome { TurnContinues = true };
        var me = s.CurrentPlayer;
        int potted = ev.PocketedObjects.Count();
        me.Score += potted;
        o.PointsScored = potted;
        if (ev.CueScratched)
        {
            o.ForceBallInHand = true;
            o.Messages.Add("Scratch — ball in hand");
        }
        if (!s.ObjectBallsOnTable.Any())
        {
            NewRackKeepingScore(s, rng);
            o.Reracked = true;
            o.ForceBallInHand = true;
            o.ForceKitchen = true;
            o.Messages.Add("Rack cleared! New rack");
        }
        return o;
    }

    void NewRackKeepingScore(GameState s, Random rng)
    {
        foreach (var b in s.Balls)
        {
            if (b.Number != 0) b.Pocketed = true;
        }
        s.Cue.Pocketed = false;
        s.Cue.Pos = Table.HeadSpot;
        PlaceRack(s, rng);
        s.IsBreakShot = true;
        s.RackNumber++;
    }

    public override string Status(GameState s, int player)
    {
        var p = s.Players[player];
        return $"{p.Score} potted • {p.Shots} shots";
    }
}

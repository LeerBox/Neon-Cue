namespace NeonCue.Core;

/// <summary>
/// Snooker on a 12-foot table. Balls: 0 cue, 1-15 reds, 16 yellow (2), 17 green (3), 18 brown (4),
/// 19 blue (5), 20 pink (6), 21 black (7).
/// Red, then any colour (the colour hit first is the nominated one); colours are respotted while
/// reds remain, then taken in order yellow to black. Fouls award at least 4 to the opponent, who
/// plays from where the cue ball lies (in-off: from the D).
/// </summary>
public sealed class SnookerRules : GameRules
{
    public const int Yellow = 16, Green = 17, Brown = 18, Blue = 19, Pink = 20, Black = 21;

    public override GameMode Mode => GameMode.Snooker;
    public override string Name => "Snooker";
    public override string Tagline => "Red, then a colour, on a 12-ft table. Fouls give 4+ points. Most points after the black wins.";
    public override int BallCount => 21;
    public override bool IsPointsGame => true;
    public override TableKind TableKind => TableKind.Snooker;
    public override bool RequireLegalFirstContactOnBreak => true;
    protected override bool FoulGivesBallInHand => false;

    public static bool IsRed(int n) => n >= 1 && n <= 15;
    public static bool IsColour(int n) => n >= Yellow && n <= Black;
    public static int Value(int n) => IsRed(n) ? 1 : IsColour(n) ? n - 14 : 0;

    public static string ColourName(int n) => n switch
    {
        Yellow => "yellow", Green => "green", Brown => "brown", Blue => "blue", Pink => "pink", Black => "black",
        _ => "red",
    };

    /// <summary>The official spot of each colour.</summary>
    public static Vec2 Spot(int n) => n switch
    {
        Yellow => new Vec2(Table.BaulkX, Table.Height / 2 + Table.DRadius),
        Green => new Vec2(Table.BaulkX, Table.Height / 2 - Table.DRadius),
        Brown => new Vec2(Table.BaulkX, Table.Height / 2),
        Blue => Table.CenterSpot,
        Pink => new Vec2(Table.Width * 0.75, Table.Height / 2),
        _ => new Vec2(Table.Width * (1 - 0.0908), Table.Height / 2),
    };

    protected override void PlaceRack(GameState s, Random rng)
    {
        // Fifteen reds in a triangle, apex just behind the pink.
        double gap = 2 * R + 0.08, dx = gap * Math.Sqrt(3) / 2;
        Vec2 apex = Spot(Pink) + new Vec2(2 * R + 1.5, 0);
        int n = 1;
        for (int row = 0; row < 5; row++)
            for (int i = 0; i <= row; i++)
                Put(s, n++, new Vec2(apex.X + row * dx + (rng.NextDouble() - 0.5) * 0.04, apex.Y + (i - row / 2.0) * gap));
        for (int c = Yellow; c <= Black; c++) Put(s, c, Spot(c));
    }

    protected override void OnNewRack(GameState s)
    {
        s.OnColour = false;
        s.CurrentBreak = 0;
        foreach (var p in s.Players) p.Score = 0;
    }

    static bool RedsOn(GameState s, Func<int, bool> onTable)
    {
        for (int i = 1; i <= 15; i++) if (onTable(i)) return true;
        return false;
    }

    static int LowestColour(Func<int, bool> onTable)
    {
        for (int c = Yellow; c <= Black; c++) if (onTable(c)) return c;
        return -1;
    }

    /// <summary>The ball (or kind of ball) the striker must hit first.</summary>
    public static string BallOn(GameState s)
    {
        Func<int, bool> on = n => !s.Balls[n].Pocketed;
        if (s.OnColour) return "a colour";
        if (RedsOn(s, on)) return "red";
        int c = LowestColour(on);
        return c > 0 ? ColourName(c) : "-";
    }

    public override bool IsLegalFirstContact(GameState s, int n, Func<int, bool> onTable)
    {
        if (n <= 0) return false;
        if (s.OnColour) return IsColour(n);
        if (RedsOn(s, onTable)) return IsRed(n);
        return n == LowestColour(onTable);
    }

    public override bool IsGoodPot(GameState s, int n) => IsLegalFirstContact(s, n);

    public override int TargetBall(GameState s)
    {
        Func<int, bool> on = n => !s.Balls[n].Pocketed;
        return !s.OnColour && !RedsOn(s, on) ? LowestColour(on) : -1;
    }

    /// <summary>Points still available to the player at the table.</summary>
    public static int PointsRemaining(GameState s)
    {
        int reds = 0;
        for (int i = 1; i <= 15; i++) if (!s.Balls[i].Pocketed) reds++;
        if (reds > 0) return reds * 8 + 27 + (s.OnColour ? 7 : 0);
        if (s.OnColour) return 7 + 27;
        int sum = 0;
        for (int c = Yellow; c <= Black; c++) if (!s.Balls[c].Pocketed) sum += Value(c);
        return sum;
    }

    public static bool NeedsSnookers(GameState s, int player)
    {
        if (s.PlayerCount < 2) return false;
        int deficit = s.Players[1 - player].Score - s.Players[player].Score;
        return deficit > PointsRemaining(s);
    }

    /// <summary>Concede when only snookers can save the frame and they are realistically out of reach.</summary>
    public override bool ShouldConcede(GameState s, int player)
    {
        if (!NeedsSnookers(s, player)) return false;
        int deficit = s.Players[1 - player].Score - s.Players[player].Score;
        int remaining = PointsRemaining(s);
        return remaining <= 13 || deficit - remaining > 16;
    }

    protected override ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak, Random rng)
    {
        var o = new ShotOutcome();
        var me = s.CurrentPlayer;
        var opp = s.Players[s.Opponent];
        var pots = ev.PocketedObjects.ToList();
        int first = ev.FirstContact;

        bool redsBefore = RedsOn(s, onTable);
        bool colourStage = s.OnColour;
        int seq = !colourStage && !redsBefore ? LowestColour(onTable) : -1;
        // In the colour stage the colour struck first is the nominated ball.
        int nominated = colourStage && IsColour(first) ? first : -1;

        bool LegalPot(int n) =>
            colourStage ? n == nominated
            : seq > 0 ? n == seq
            : IsRed(n);

        string? foul = null;
        if (ev.CueScratched) foul = "In-off";
        else if (first < 0) foul = "Missed all balls";
        else if (!IsLegalFirstContact(s, first, onTable)) foul = $"Hit the {ColourName(first)} first";
        else if (pots.Any(n => !LegalPot(n))) foul = $"Potted the {ColourName(pots.First(n => !LegalPot(n)))}";

        if (foul != null)
        {
            int onValue = colourStage ? Math.Max(4, nominated > 0 ? Value(nominated) : 4) : seq > 0 ? Value(seq) : 1;
            int penalty = Math.Max(4, onValue);
            if (first > 0) penalty = Math.Max(penalty, Value(first));
            foreach (var n in pots) penalty = Math.Max(penalty, Value(n));
            opp.Score += penalty;
            o.Foul = true;
            o.FoulReason = foul;
            o.Messages.Add($"FOUL — {foul} (+{penalty} to {opp.Name})");

            bool blackWasLast = seq == Black;
            foreach (var n in pots.Where(IsColour)) Respot(s, n);
            s.OnColour = false;
            s.CurrentBreak = 0;
            if (ev.CueScratched) { o.ForceBallInHand = true; o.ForceKitchen = true; }

            if (blackWasLast) EndFrameOrRespotBlack(s, o, rng);
            return o;
        }

        int scored = 0;
        if (colourStage)
        {
            if (nominated > 0 && pots.Contains(nominated))
            {
                scored = Value(nominated);
                Respot(s, nominated);
            }
            s.OnColour = false;
        }
        else if (seq > 0)
        {
            if (pots.Contains(seq)) scored = Value(seq);
            s.OnColour = false;
        }
        else
        {
            scored = pots.Count(IsRed);
            s.OnColour = scored > 0;
        }

        if (scored > 0)
        {
            me.Score += scored;
            s.CurrentBreak += scored;
            me.HighBreak = Math.Max(me.HighBreak, s.CurrentBreak);
            o.PointsScored = scored;
            o.TurnContinues = true;
            o.Messages.Add(s.CurrentBreak >= 10 ? $"+{scored}  •  break {s.CurrentBreak}" : $"+{scored}");
        }
        else
        {
            s.CurrentBreak = 0;
            s.OnColour = false;
        }

        if (seq == Black && scored > 0) EndFrameOrRespotBlack(s, o, rng);
        else if (!s.ObjectBallsOnTable.Any()) EndFrameOrRespotBlack(s, o, rng);

        if (!o.RackOver && o.TurnContinues == false) s.CurrentBreak = 0;
        return o;
    }

    /// <summary>Black is gone: highest score wins; a tie re-spots the black for a shoot-out from the D.</summary>
    void EndFrameOrRespotBlack(GameState s, ShotOutcome o, Random rng)
    {
        int a = s.Players[0].Score, b = s.PlayerCount > 1 ? s.Players[1].Score : int.MinValue;
        if (a != b || s.PlayerCount < 2)
        {
            o.RackOver = true;
            o.Winner = a > b ? 0 : 1;
            o.Messages.Add($"{s.Players[o.Winner].Name} wins the frame {Math.Max(a, b)}–{Math.Min(a, b)}");
            return;
        }
        Respot(s, Black);
        o.TurnContinues = false;
        o.ForceBallInHand = true;
        o.ForceKitchen = true;
        o.Messages.Add("Scores level — black re-spotted");
    }

    protected override void Respot(GameState s, int number)
    {
        if (!IsColour(number)) return; // reds stay down
        var b = s.Balls[number];
        b.Pocketed = false;
        b.Vel = Vec2.Zero;
        // Own spot, else the highest free spot, else nearest free point towards the top cushion.
        foreach (var spot in new[] { Spot(number), Spot(Black), Spot(Pink), Spot(Blue), Spot(Brown), Spot(Green), Spot(Yellow) })
        {
            if (SpotFree(s, spot, number)) { b.Pos = spot; return; }
        }
        b.Pos = s.FindFreeSpot(Spot(number), number);
    }

    static bool SpotFree(GameState s, Vec2 p, int self)
    {
        foreach (var o in s.Balls)
            if (!o.Pocketed && o.Number != self && Vec2.Distance(o.Pos, p) < 2 * R + 0.5) return false;
        return true;
    }

    public override string Status(GameState s, int player)
    {
        var p = s.Players[player];
        string txt = $"{p.Score} pts";
        if (s.Current == player && s.CurrentBreak > 0) txt += $" • break {s.CurrentBreak}";
        else if (p.HighBreak > 0) txt += $" • best {p.HighBreak}";
        return txt;
    }

    public override string Hint(GameState s)
    {
        string on = BallOn(s).ToUpperInvariant();
        string txt = $"ON: {on}  •  {PointsRemaining(s)} left";
        if (NeedsSnookers(s, s.Current)) txt += "  •  SNOOKERS NEEDED";
        return txt;
    }
}

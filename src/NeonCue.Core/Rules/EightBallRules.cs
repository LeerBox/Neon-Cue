namespace NeonCue.Core;

public sealed class EightBallRules : GameRules
{
    public override GameMode Mode => GameMode.EightBall;
    public override string Name => "8-Ball";
    public override string Tagline => "Claim solids or stripes, clear your group, then sink the 8 to win.";
    public override bool BreakScratchToKitchen => true;

    static bool InGroup(int n, Group g) => g switch
    {
        Group.Solids => n >= 1 && n <= 7,
        Group.Stripes => n >= 9 && n <= 15,
        _ => false,
    };

    static IEnumerable<int> GroupBalls(Group g) => g == Group.Solids ? Enumerable.Range(1, 7) : Enumerable.Range(9, 7);

    static bool GroupCleared(Group g, Func<int, bool> onTable) => g != Group.None && GroupBalls(g).All(n => !onTable(n));

    protected override void PlaceRack(GameState s, Random rng)
    {
        var pos = TrianglePositions(5, rng);
        var solids = Enumerable.Range(1, 7).ToList();
        var stripes = Enumerable.Range(9, 7).ToList();
        Shuffle(solids, rng);
        Shuffle(stripes, rng);

        // Back corners: one solid and one stripe. Centre: the 8.
        bool solidLeft = rng.Next(2) == 0;
        Put(s, solidLeft ? solids[0] : stripes[0], pos[10]);
        Put(s, solidLeft ? stripes[0] : solids[0], pos[14]);
        Put(s, 8, pos[4]);

        var rest = solids.Skip(1).Concat(stripes.Skip(1)).ToList();
        Shuffle(rest, rng);
        int k = 0;
        for (int i = 0; i < pos.Count; i++)
        {
            if (i == 4 || i == 10 || i == 14) continue;
            Put(s, rest[k++], pos[i]);
        }
    }

    public override bool IsLegalFirstContact(GameState s, int n, Func<int, bool> onTable)
    {
        if (n <= 0) return false;
        var g = s.CurrentPlayer.Group;
        if (s.TableOpen || g == Group.None) return n != 8;
        if (GroupCleared(g, onTable)) return n == 8;
        return InGroup(n, g);
    }

    public override bool IsGoodPot(GameState s, int n) => IsLegalFirstContact(s, n);

    protected override ShotOutcome Judge(GameState s, ShotEvents ev, Func<int, bool> onTable, bool isBreak, Random rng)
    {
        var o = new ShotOutcome();
        var me = s.CurrentPlayer;
        var opp = s.Players[s.Opponent];
        var objs = ev.PocketedObjects.ToList();
        string? foul = StandardFoul(s, ev, onTable, isBreak);

        if (objs.Contains(8))
        {
            if (isBreak)
            {
                Respot(s, 8);
                o.Messages.Add("8-ball on the break — respotted");
            }
            else
            {
                bool cleared = !s.TableOpen && GroupCleared(me.Group, onTable);
                o.RackOver = true;
                if (foul == null && cleared)
                {
                    o.Winner = s.Current;
                    o.Messages.Add($"{me.Name} sinks the 8!");
                }
                else
                {
                    o.Winner = s.Opponent;
                    o.Foul = foul != null;
                    o.FoulReason = foul;
                    o.Messages.Add(foul != null ? $"FOUL on the 8 — {foul}" : "8-ball pocketed early!");
                }
                return o;
            }
        }

        if (foul != null)
        {
            o.Foul = true;
            o.FoulReason = foul;
            o.Messages.Add($"FOUL — {foul}");
        }

        if (!isBreak && foul == null && s.TableOpen)
        {
            int first = objs.FirstOrDefault(n => n != 8);
            if (first > 0)
            {
                me.Group = first < 8 ? Group.Solids : Group.Stripes;
                opp.Group = me.Group == Group.Solids ? Group.Stripes : Group.Solids;
                s.TableOpen = false;
                o.Messages.Add($"{me.Name} is {me.Group.ToString().ToUpperInvariant()}");
            }
        }

        if (foul == null)
        {
            if (isBreak) o.TurnContinues = objs.Any(n => n != 8);
            else if (!s.TableOpen) o.TurnContinues = objs.Any(n => InGroup(n, me.Group));
        }
        return o;
    }

    public override string Status(GameState s, int player)
    {
        var p = s.Players[player];
        if (s.TableOpen || p.Group == Group.None) return "Open table";
        int left = GroupBalls(p.Group).Count(n => !s.Balls[n].Pocketed);
        return left == 0 ? $"{p.Group} • on the 8" : $"{p.Group} • {left} left";
    }

    public override IReadOnlyList<int> PlayerBalls(GameState s, int player)
    {
        var p = s.Players[player];
        if (s.TableOpen || p.Group == Group.None) return [];
        var list = GroupBalls(p.Group).Where(n => !s.Balls[n].Pocketed).ToList();
        if (list.Count == 0 && !s.Balls[8].Pocketed) list.Add(8);
        return list;
    }

    public override string Hint(GameState s) =>
        s.TableOpen ? "Open table — any ball except the 8" : $"Shoot your {s.CurrentPlayer.Group.ToString().ToLowerInvariant()}";
}

namespace NeonCue.Core;

/// <summary>
/// Live win probabilities for the scoreboard. The current rack/frame is judged from what is left
/// to play (points remaining, balls per group, ball on), who is at the table and the players'
/// skill; the match then combines that with racks won and the race length.
/// </summary>
public static class WinOdds
{
    /// <summary>Rough playing strength 0..1 (humans are assumed to play like a strong amateur).</summary>
    public static double Skill(PlayerState p) => !p.IsAI ? 0.6 : p.Level switch
    {
        AILevel.Rookie => 0.3,
        AILevel.Amateur => 0.48,
        AILevel.Pro => 0.7,
        _ => 0.86,
    };

    static double Logistic(double z) => 1 / (1 + Math.Exp(-z));

    /// <summary>Probability that <paramref name="player"/> wins the current rack/frame.</summary>
    public static double Rack(GameState s, GameRules rules, int player)
    {
        if (s.PlayerCount < 2) return 1;
        if (s.RackOver) return s.RackWinner == player ? 1 : 0;
        int other = 1 - player;
        double atTable = s.Current == player ? 1 : -1;
        double skill = (Skill(s.Players[player]) - Skill(s.Players[other])) * 1.6;
        double z;

        switch (rules.Mode)
        {
            case GameMode.Snooker:
            {
                int lead = s.Players[player].Score - s.Players[other].Score;
                int rem = SnookerRules.PointsRemaining(s);
                if (lead > rem) return 0.99;
                if (-lead > rem) return 0.01;
                z = (lead + atTable * 5) / (6 + 0.28 * rem) + skill * Math.Min(1, rem / 60.0);
                break;
            }
            case GameMode.Rotation:
            case GameMode.StraightPool:
            {
                int target = s.TargetScore;
                int need = target - s.Players[player].Score, needOther = target - s.Players[other].Score;
                double scale = rules.Mode == GameMode.Rotation ? 8 + 0.2 * Math.Min(need, needOther) : 3 + 0.15 * Math.Min(need, needOther);
                z = (needOther - need + atTable * (rules.Mode == GameMode.Rotation ? 6 : 2)) / scale + skill;
                break;
            }
            case GameMode.EightBall:
            {
                int Left(int i)
                {
                    var g = s.Players[i].Group;
                    if (s.TableOpen || g == Group.None) return 8;
                    int n = Enumerable.Range(g == Group.Solids ? 1 : 9, 7).Count(b => !s.Balls[b].Pocketed);
                    return n + 1; // plus the 8
                }
                z = (Left(other) - Left(player)) * 0.45 + atTable * 0.55 + skill;
                break;
            }
            default:
            {
                // Rotation-style money-ball games: the player at the table holds an edge that grows
                // as the rack thins out.
                int n = s.ObjectBallsOnTable.Count();
                z = atTable * (0.35 + 2.4 / (n + 1)) + skill;
                break;
            }
        }
        return Math.Clamp(Logistic(z), 0.01, 0.99);
    }

    /// <summary>Probability that <paramref name="player"/> wins the match.</summary>
    public static double Match(GameState s, GameRules rules, int player)
    {
        if (s.PlayerCount < 2) return 1;
        double rack = Rack(s, rules, player);
        if (!rules.UsesRaceTo) return rack;
        int other = 1 - player;
        int needMe = s.RaceTo - s.Players[player].RacksWon, needOther = s.RaceTo - s.Players[other].RacksWon;
        if (needMe <= 0) return 1;
        if (needOther <= 0) return 0;
        if (s.RackOver) return Race(needMe, needOther, PerRack(s, player));

        double q = PerRack(s, player);
        return rack * Race(needMe - 1, needOther, q) + (1 - rack) * Race(needMe, needOther - 1, q);
    }

    /// <summary>Chance of winning a fresh rack, from skill alone.</summary>
    static double PerRack(GameState s, int player) =>
        Logistic((Skill(s.Players[player]) - Skill(s.Players[1 - player])) * 2.2);

    /// <summary>Win probability of a race needing a vs b more racks, each won with probability q.</summary>
    static double Race(int a, int b, double q)
    {
        if (a <= 0) return 1;
        if (b <= 0) return 0;
        var memo = new double[a + 1, b + 1];
        for (int i = 0; i <= a; i++)
            for (int j = 0; j <= b; j++)
                memo[i, j] = i == 0 ? 1 : j == 0 ? 0 : q * memo[i - 1, j] + (1 - q) * memo[i, j - 1];
        return memo[a, b];
    }
}

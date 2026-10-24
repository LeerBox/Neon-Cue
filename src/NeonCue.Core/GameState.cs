namespace NeonCue.Core;

public enum GameMode { EightBall, NineBall, TenBall, StraightPool, Rotation, Practice }
public enum Group { None, Solids, Stripes }
public enum AILevel { Rookie, Amateur, Pro, Master }
public enum OpponentType { Cpu, TwoPlayer, CpuVsCpu }

public sealed class PlayerState
{
    public string Name = "";
    public bool IsAI;
    public AILevel Level = AILevel.Pro;
    public CueSpec Cue = CueCatalog.Standard;
    public Group Group;
    public int Score;
    public int RacksWon;
    public int Fouls;
    public int ConsecutiveFouls;
    public int Shots;
    public int Pots;

    public PlayerState Clone() => (PlayerState)MemberwiseClone();
}

public sealed class GameState
{
    public GameMode Mode;
    /// <summary>Indexed by ball number: Balls[0] is the cue ball.</summary>
    public Ball[] Balls = [];
    public PlayerState[] Players = [];
    public int Current;
    public bool IsBreakShot = true;
    public bool BallInHand = true;
    public bool KitchenOnly = true;
    public bool TableOpen = true;
    public bool RackOver;
    public int RackWinner = -1;
    public bool MatchOver;
    public int RaceTo = 1;
    public int TargetScore = 50;
    public int Breaker;
    public int RackNumber = 1;
    public int ShotNumber;

    public Ball Cue => Balls[0];
    public int PlayerCount => Players.Length;
    public PlayerState CurrentPlayer => Players[Current];
    public int Opponent => PlayerCount > 1 ? 1 - Current : Current;

    public IEnumerable<Ball> ObjectBallsOnTable => Balls.Where(b => b.Number != 0 && !b.Pocketed);

    public int LowestOnTable()
    {
        for (int i = 1; i < Balls.Length; i++)
            if (!Balls[i].Pocketed) return i;
        return -1;
    }

    public GameState Clone()
    {
        var c = (GameState)MemberwiseClone();
        c.Balls = new Ball[Balls.Length];
        for (int i = 0; i < Balls.Length; i++) c.Balls[i] = Balls[i].Clone();
        c.Players = new PlayerState[Players.Length];
        for (int i = 0; i < Players.Length; i++) c.Players[i] = Players[i].Clone();
        return c;
    }

    public bool IsValidCuePlacement(Vec2 p)
    {
        const double R = Table.R;
        if (!Table.InsidePlayArea(p)) return false;
        if (KitchenOnly && p.X > Table.HeadStringX) return false;
        double min = (2 * R + 0.5) * (2 * R + 0.5);
        for (int i = 1; i < Balls.Length; i++)
        {
            var b = Balls[i];
            if (!b.Pocketed && Vec2.DistanceSq(b.Pos, p) < min) return false;
        }
        return true;
    }

    /// <summary>Nearest free spot to <paramref name="preferred"/>, searching along the long axis first.</summary>
    public Vec2 FindFreeSpot(Vec2 preferred, int self = -1)
    {
        const double R = Table.R;
        bool Free(Vec2 p)
        {
            if (!Table.InsidePlayArea(p)) return false;
            foreach (var b in Balls)
            {
                if (b.Pocketed || b.Number == self) continue;
                if (Vec2.DistanceSq(b.Pos, p) < (2 * R + 0.5) * (2 * R + 0.5)) return false;
            }
            return true;
        }

        if (Free(preferred)) return preferred;
        for (double dx = 2 * R + 1; dx < Table.Width; dx += 2)
        {
            var p = new Vec2(preferred.X + dx, preferred.Y);
            if (p.X < Table.Width - R && Free(p)) return p;
            p = new Vec2(preferred.X - dx, preferred.Y);
            if (p.X > R && Free(p)) return p;
        }
        for (double rad = 2 * R; rad < Table.Width; rad += R)
        {
            for (int k = 0; k < 24; k++)
            {
                var p = preferred + Vec2.FromAngle(k * Math.PI / 12) * rad;
                if (Free(p)) return p;
            }
        }
        return preferred;
    }
}

namespace NeonCue.Core;

public enum BallKind { Cue, Solid, Eight, Stripe }

public sealed class Ball
{
    public int Number { get; }
    public Vec2 Pos;
    public Vec2 Vel;
    public bool Pocketed;

    /// <summary>Follow (+) / draw (-) applied by the cue, consumed on first ball contact.</summary>
    public double SpinTop;

    /// <summary>Right (+) / left (-) english, released on cushion contact.</summary>
    public double SpinSide;

    /// <summary>True once the cue ball's follow/draw has been converted at first contact.</summary>
    public bool SpinUsed;

    public Ball(int number, Vec2 pos)
    {
        Number = number;
        Pos = pos;
    }

    public BallKind Kind => Number switch
    {
        0 => BallKind.Cue,
        8 => BallKind.Eight,
        < 8 => BallKind.Solid,
        _ => BallKind.Stripe,
    };

    public bool IsMoving => !Pocketed && (Vel.X != 0 || Vel.Y != 0);

    public Ball Clone() => new(Number, Pos)
    {
        Vel = Vel,
        Pocketed = Pocketed,
        SpinTop = SpinTop,
        SpinSide = SpinSide,
        SpinUsed = SpinUsed,
    };
}

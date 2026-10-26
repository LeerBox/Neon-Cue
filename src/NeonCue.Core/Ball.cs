namespace NeonCue.Core;

public enum BallKind { Cue, Solid, Eight, Stripe }

public sealed class Ball
{
    public int Number { get; }
    public Vec2 Pos;
    public Vec2 Vel;
    public bool Pocketed;

    /// <summary>
    /// Angular velocity (rad/s) in the table frame: x right, y towards the bottom rail, z down into
    /// the cloth. A ball rolling with velocity v has W = (vy, -vx, 0) / R; positive Wz is clockwise as
    /// seen from above (left-hand english on a ball moving up the table).
    /// </summary>
    public double Wx, Wy, Wz;

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

    /// <summary>Moving across the cloth, or sliding/spinning in a way that will make it move.</summary>
    public bool IsMoving => !Pocketed && (Vel.X != 0 || Vel.Y != 0 || Wx != 0 || Wy != 0);

    public void Stop()
    {
        Vel = Vec2.Zero;
        Wx = Wy = Wz = 0;
    }

    public Ball Clone() => new(Number, Pos)
    {
        Vel = Vel,
        Pocketed = Pocketed,
        Wx = Wx,
        Wy = Wy,
        Wz = Wz,
    };
}

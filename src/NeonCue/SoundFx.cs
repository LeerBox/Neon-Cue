using NeonCue.Audio;

namespace NeonCue;

/// <summary>
/// Game-facing sound API. Every hit gets its own voice in the mixer, so sounds overlap instead of
/// cutting each other off; loudness follows impact speed, stereo pan follows the ball's position
/// on the table, and pitch varies slightly per hit so repeats don't sound mechanical.
/// </summary>
sealed class SoundFx : IDisposable
{
    readonly Mixer mixer = new();
    readonly SoundBank bank = new();
    readonly WaveOut output;
    readonly Random rng = new();
    bool enabled = true;

    public SoundFx()
    {
        output = new WaveOut(mixer);
    }

    public bool Enabled
    {
        get => enabled;
        set { enabled = value; if (!value) mixer.SetRolling(0); }
    }

    /// <param name="x01">Horizontal table position, 0 = left rail .. 1 = right rail (stereo pan).</param>
    public void Clack(double speed, double x01 = 0.5) => Play(bank.Clack, Curve(speed, 30, 1500, 0.7) * 0.85, x01, 0.06);
    public void Cushion(double speed, double x01 = 0.5) => Play(bank.Cushion, Curve(speed, 60, 1400, 0.8) * 0.7, x01, 0.05);
    public void Cue(double power, double x01 = 0.5) => Play(bank.Cue, 0.3 + 0.6 * Math.Clamp(power, 0, 1), x01, 0.03);
    public void Pocket(double x01 = 0.5) => Play(bank.Pocket, 0.85, x01, 0.04);
    public void Win() => Play(bank.Win, 0.7, 0.5, 0);
    public void Click() => Play(bank.Click, 0.6, 0.5, 0);

    /// <summary>Continuous rolling rumble; pass the summed speed of all moving balls (0 when still).</summary>
    public void Rolling(double totalSpeed) => mixer.SetRolling(enabled ? Math.Pow(Math.Clamp(totalSpeed / 4000, 0, 1), 0.8) * 0.35 : 0);

    static double Curve(double v, double floor, double full, double exp) =>
        v <= floor ? 0 : Math.Pow(Math.Clamp((v - floor) / (full - floor), 0, 1), exp);

    void Play(float[] sound, double gain, double x01, double pitchSpread)
    {
        if (!enabled || !output.IsOpen) return;
        double pan = 0.5 + (Math.Clamp(x01, 0, 1) - 0.5) * 0.7; // gentle stereo, never hard left/right
        double pitch = 1 + (rng.NextDouble() * 2 - 1) * pitchSpread;
        mixer.Play(sound, gain, pan, pitch);
    }

    public void Dispose() => output.Dispose();
}

namespace NeonCue.Audio;

/// <summary>Synthesised billiard sounds (44.1 kHz mono), generated once at start-up.</summary>
public sealed class SoundBank
{
    public float[] Clack { get; }
    public float[] Cushion { get; }
    public float[] Cue { get; }
    public float[] Pocket { get; }
    public float[] Win { get; }
    public float[] Click { get; }

    public SoundBank()
    {
        var rnd = new Random(3);
        double White() => rnd.NextDouble() * 2 - 1;

        // Phenolic ball-on-ball: bright, very short, with a hard transient.
        Clack = Synth(0.045, t =>
            0.45 * Math.Sin(2 * Math.PI * 3150 * t) * Math.Exp(-t * 140)
            + 0.30 * Math.Sin(2 * Math.PI * 4720 * t) * Math.Exp(-t * 170)
            + 0.18 * Math.Sin(2 * Math.PI * 1980 * t) * Math.Exp(-t * 90)
            + 0.55 * White() * Math.Exp(-t * 900));

        // Rubber cushion: a dull thump.
        Cushion = LowPass(Synth(0.08, t =>
            0.6 * Math.Sin(2 * Math.PI * 165 * t) * Math.Exp(-t * 38)
            + 0.3 * Math.Sin(2 * Math.PI * 92 * t) * Math.Exp(-t * 30)
            + 0.35 * White() * Math.Exp(-t * 120)), 0.25);

        // Leather tip on the cue ball: a woody "tock".
        Cue = Synth(0.07, t =>
            0.45 * Math.Sin(2 * Math.PI * 880 * t) * Math.Exp(-t * 75)
            + 0.25 * Math.Sin(2 * Math.PI * 1760 * t) * Math.Exp(-t * 110)
            + 0.30 * Math.Sin(2 * Math.PI * 210 * t) * Math.Exp(-t * 45)
            + 0.35 * White() * Math.Exp(-t * 400));

        // Ball drops into the pocket: low thud, then a few muffled bumps against the liner.
        Pocket = LowPass(Synth(0.42, t =>
        {
            double v = 0.8 * Math.Sin(2 * Math.PI * (120 - 45 * t) * t) * Math.Exp(-t * 11);
            foreach (var (at, amp) in new[] { (0.035, 0.5), (0.085, 0.35), (0.15, 0.22), (0.23, 0.12) })
                if (t > at) v += amp * Math.Sin(2 * Math.PI * 260 * (t - at)) * Math.Exp(-(t - at) * 60);
            return v + 0.15 * White() * Math.Exp(-t * 40);
        }), 0.3);

        Win = Synth(1.1, t =>
        {
            double[] notes = [523.25, 659.25, 783.99, 1046.5];
            double v = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                double s = t - i * 0.12;
                if (s > 0) v += 0.2 * Math.Sin(2 * Math.PI * notes[i] * s) * Math.Exp(-s * 4.5);
            }
            return v;
        });

        Click = Synth(0.025, t => 0.3 * Math.Sin(2 * Math.PI * 1500 * t) * Math.Exp(-t * 160));
    }

    static float[] Synth(double seconds, Func<double, double> f)
    {
        int n = (int)(seconds * Mixer.SampleRate);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Mixer.SampleRate;
            double attack = Math.Min(1, i / (Mixer.SampleRate * 0.0004)); // no click at the start
            double release = Math.Min(1, (n - i) / (Mixer.SampleRate * 0.004));
            s[i] = (float)Math.Clamp(f(t) * attack * release, -1, 1);
        }
        return s;
    }

    static float[] LowPass(float[] s, double k)
    {
        float y = 0;
        for (int i = 0; i < s.Length; i++) s[i] = y += (float)k * (s[i] - y);
        return s;
    }
}

namespace NeonCue.Audio;

/// <summary>
/// Synthesised billiard knocks (44.1 kHz mono), generated once at start-up. Only tonal, fast-decaying
/// knocks: the single noise component is a ~1 ms contact transient, so nothing hisses or rumbles.
/// </summary>
public sealed class SoundBank
{
    public float[] Clack { get; }
    public float[] Cushion { get; }
    public float[] Pocket { get; }
    public float[] Win { get; }
    public float[] Click { get; }

    readonly Dictionary<string, float[]> cueStrikes = new();

    /// <summary>How a cue sounds when its tip meets the cue ball.</summary>
    public sealed record CueVoice(
        double Pitch,        // main body tone (Hz)
        double Overtone,     // second partial as a ratio of Pitch
        double Decay,        // 1/s, higher = shorter, drier knock
        double Thump,        // low body tone (Hz) of the butt/shaft
        double Snap,         // 0..1 sharpness of the contact transient
        double Ring,         // 0..1 level of a resonant ring (fittings, carbon, inlays)
        double RingPitch,    // Hz
        double RingDecay,    // 1/s
        double Level);       // overall loudness

    /// <summary>Each cue's strike: materials decide pitch, dryness and ring.</summary>
    public static readonly IReadOnlyDictionary<string, CueVoice> CueVoices = new Dictionary<string, CueVoice>
    {
        // Worn bar cue: low, dull, dead wooden knock.
        ["house"] = new(560, 1.8, 70, 140, 0.15, 0, 0, 1, 0.85),
        // Maple starter: clean, neutral "tock".
        ["starter"] = new(860, 2.0, 80, 190, 0.3, 0, 0, 1, 0.9),
        // Lively custom: higher and snappier.
        ["viper"] = new(1080, 2.35, 95, 220, 0.45, 0.05, 2600, 30, 0.95),
        // Phenolic break tip: hard, bright, loud crack.
        ["thunder"] = new(1550, 2.7, 130, 250, 0.9, 0.04, 3900, 40, 1.15),
        // Carbon shaft: crisp, glassy tick with a short high ring.
        ["phantom"] = new(1280, 3.1, 110, 170, 0.55, 0.14, 3450, 22, 0.95),
        // Hand-made inlay cue: soft, warm, rounded.
        ["silk"] = new(720, 2.0, 50, 160, 0.12, 0.08, 1500, 14, 0.85),
        // Ebony & gold: rich knock with a bell-like ring from the gold fittings.
        ["dragon"] = new(830, 2.5, 62, 135, 0.35, 0.18, 2200, 9, 1.0),
        // Black diamond: deep, crisp knock with a shimmering double ring.
        ["royale"] = new(960, 2.8, 72, 118, 0.45, 0.22, 2900, 7, 1.05),
        // Pale ash and linen: a small, dry, polite tap.
        ["linen"] = new(740, 1.9, 90, 175, 0.18, 0.02, 1200, 28, 0.72),
        // Ivory points: a short bright click, almost no body.
        ["ivory"] = new(1240, 2.4, 120, 210, 0.5, 0.06, 3100, 36, 0.88),
        // Helical carbon: a thin tick and a long high ring.
        ["helix"] = new(1460, 3.3, 100, 160, 0.62, 0.2, 4200, 12, 0.92),
        // Crystal: a soft knock that leaves a chime.
        ["aurora"] = new(880, 2.6, 48, 150, 0.22, 0.28, 1760, 5, 0.9),
    };

    readonly Dictionary<string, float[]> cueDraws = new();

    public SoundBank()
    {
        var rnd = new Random(3);
        double Tick() => rnd.NextDouble() * 2 - 1;

        // Phenolic ball-on-ball: bright, very short.
        Clack = Synth(0.045, t =>
            0.45 * Math.Sin(2 * Math.PI * 3150 * t) * Math.Exp(-t * 140)
            + 0.30 * Math.Sin(2 * Math.PI * 4720 * t) * Math.Exp(-t * 170)
            + 0.18 * Math.Sin(2 * Math.PI * 1980 * t) * Math.Exp(-t * 90)
            + 0.35 * Tick() * Math.Exp(-t * 2500));

        // Rubber cushion: a dull thump, purely tonal.
        Cushion = LowPass(Synth(0.08, t =>
            0.6 * Math.Sin(2 * Math.PI * 165 * t) * Math.Exp(-t * 38)
            + 0.3 * Math.Sin(2 * Math.PI * 92 * t) * Math.Exp(-t * 30)), 0.3);

        // Ball drops into the pocket: low thud, then a few muffled bumps against the liner.
        Pocket = LowPass(Synth(0.38, t =>
        {
            double v = 0.8 * Math.Sin(2 * Math.PI * (120 - 45 * t) * t) * Math.Exp(-t * 12);
            foreach (var (at, amp) in new[] { (0.035, 0.5), (0.085, 0.35), (0.15, 0.22), (0.23, 0.12) })
                if (t > at) v += amp * Math.Sin(2 * Math.PI * 260 * (t - at)) * Math.Exp(-(t - at) * 60);
            return v;
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

        foreach (var (id, voice) in CueVoices)
        {
            cueStrikes[id] = MakeCue(voice, id.GetHashCode());
            cueDraws[id] = MakeDraw(voice, id.GetHashCode());
        }
    }

    /// <summary>The strike sound of a cue (falls back to the starter cue's).</summary>
    public float[] CueStrike(string? cueId) =>
        cueId != null && cueStrikes.TryGetValue(cueId, out var s) ? s : cueStrikes["starter"];

    /// <summary>The quiet sound of that cue being drawn back. One gesture, not a loop.</summary>
    public float[] CueDraw(string? cueId) =>
        cueId != null && cueDraws.TryGetValue(cueId, out var s) ? s : cueDraws["starter"];

    static float[] MakeCue(CueVoice v, int seed)
    {
        var rnd = new Random(seed);
        double length = 0.07 + (v.Ring > 0 ? Math.Min(0.4, 4.0 / v.RingDecay) : 0);
        return Synth(length, t =>
        {
            double s = 0.5 * Math.Sin(2 * Math.PI * v.Pitch * t) * Math.Exp(-t * v.Decay)
                     + 0.25 * Math.Sin(2 * Math.PI * v.Pitch * v.Overtone * t) * Math.Exp(-t * v.Decay * 1.6)
                     + 0.35 * Math.Sin(2 * Math.PI * v.Thump * t) * Math.Exp(-t * 45)
                     + 0.4 * v.Snap * (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 2500);
            if (v.Ring > 0)
                s += v.Ring * (Math.Sin(2 * Math.PI * v.RingPitch * t) + 0.5 * Math.Sin(2 * Math.PI * v.RingPitch * 1.5 * t))
                     * Math.Exp(-t * v.RingDecay) * Math.Min(1, t * 400);
            return s * v.Level;
        });
    }

    /// <summary>A short draw: the shaft tone, soft, with that cue's ring if it has one.</summary>
    static float[] MakeDraw(CueVoice v, int seed)
    {
        var rnd = new Random(seed + 17);
        return Synth(0.16, t =>
        {
            double env = Math.Sin(Math.PI * Math.Clamp(t / 0.16, 0, 1));
            double s = 0.25 * Math.Sin(2 * Math.PI * v.Thump * 0.5 * t) * env
                     + 0.08 * v.Snap * (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 80);
            if (v.Ring > 0)
                s += v.Ring * 0.35 * Math.Sin(2 * Math.PI * v.RingPitch * 0.5 * t) * env;
            return s * 0.55;
        });
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

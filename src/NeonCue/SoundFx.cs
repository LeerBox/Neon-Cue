using System.Diagnostics;
using System.Media;
using System.Text;

namespace NeonCue;

/// <summary>Tiny synthesized sound set — no audio files needed.</summary>
sealed class SoundFx : IDisposable
{
    const int Rate = 22050;

    readonly SoundPlayer[] clacks;
    readonly SoundPlayer cue, pocket, cushion, win, click;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastPlay = -1;
    int lastPriority;

    public bool Enabled { get; set; } = true;

    public SoundFx()
    {
        var rnd = new Random(3);
        clacks = new[] { 0.35f, 0.6f, 0.95f }.Select(v => Make(Synth(0.05, t =>
            v * (0.55 * Math.Sin(2 * Math.PI * 2900 * t) * Math.Exp(-t * 95)
               + 0.3 * Math.Sin(2 * Math.PI * 1850 * t) * Math.Exp(-t * 70)
               + 0.35 * (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 170))))).ToArray();
        cue = Make(Synth(0.09, t =>
            0.5 * Math.Sin(2 * Math.PI * 720 * t) * Math.Exp(-t * 60)
            + 0.35 * Math.Sin(2 * Math.PI * 190 * t) * Math.Exp(-t * 40)
            + 0.4 * (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 130)));
        pocket = Make(Synth(0.38, t =>
            0.75 * Math.Sin(2 * Math.PI * (130 - 50 * t) * t) * Math.Exp(-t * 9)
            + 0.25 * (rnd.NextDouble() * 2 - 1) * (Math.Exp(-Math.Abs(t - 0.03) * 220) + Math.Exp(-Math.Abs(t - 0.08) * 260))));
        cushion = Make(Synth(0.07, t =>
            0.55 * Math.Sin(2 * Math.PI * 260 * t) * Math.Exp(-t * 45)
            + 0.18 * (rnd.NextDouble() * 2 - 1) * Math.Exp(-t * 90)));
        win = Make(Synth(1.0, t =>
        {
            double[] notes = [523.25, 659.25, 783.99, 1046.5];
            double v = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                double s = t - i * 0.12;
                if (s > 0) v += 0.22 * Math.Sin(2 * Math.PI * notes[i] * s) * Math.Exp(-s * 4.5);
            }
            return v;
        }));
        click = Make(Synth(0.03, t => 0.3 * Math.Sin(2 * Math.PI * 1500 * t) * Math.Exp(-t * 140)));
    }

    public void Clack(double speed) => Play(clacks[speed > 900 ? 2 : speed > 350 ? 1 : 0], 1);
    public void Cue(double power) => Play(cue, 2);
    public void Pocket() => Play(pocket, 3);
    public void Cushion(double speed) { if (speed > 180) Play(cushion, 0); }
    public void Win() => Play(win, 4);
    public void Click() => Play(click, 1);

    void Play(SoundPlayer p, int priority)
    {
        if (!Enabled) return;
        double now = clock.Elapsed.TotalSeconds;
        double since = now - lastPlay;
        if (since < 0.035) return;
        if (priority < lastPriority && since < 0.14) return;
        try { p.Play(); } catch { /* audio device unavailable */ }
        lastPlay = now;
        lastPriority = priority;
    }

    static float[] Synth(double seconds, Func<double, double> f)
    {
        var s = new float[(int)(seconds * Rate)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = i / (double)Rate;
            double fade = Math.Min(1, (s.Length - i) / (Rate * 0.004));
            s[i] = (float)Math.Clamp(f(t) * fade, -1, 1);
        }
        return s;
    }

    static SoundPlayer Make(float[] samples)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            int dataLen = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataLen);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataLen);
            foreach (var v in samples) w.Write((short)(v * 30000));
        }
        ms.Position = 0;
        var player = new SoundPlayer(ms);
        player.Load();
        return player;
    }

    public void Dispose()
    {
        foreach (var c in clacks) c.Dispose();
        cue.Dispose();
        pocket.Dispose();
        cushion.Dispose();
        win.Dispose();
        click.Dispose();
    }
}

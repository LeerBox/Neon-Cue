using System.Collections.Concurrent;

namespace NeonCue.Audio;

/// <summary>
/// Software mixer: any number of overlapping one-shot voices (with gain, stereo pan and pitch)
/// plus a continuous rolling rumble, rendered to 16-bit stereo. Thread-safe to trigger from the
/// game thread; <see cref="Render"/> runs on the audio thread (or offline in tests).
/// </summary>
public sealed class Mixer
{
    public const int SampleRate = 44100;
    public const int MaxVoices = 32;

    sealed class Voice
    {
        public required float[] Data;
        public double Pos;
        public double Step;
        public float GainL, GainR;
        public long StartFrame;
    }

    readonly ConcurrentQueue<Voice> incoming = new();
    readonly List<Voice> active = new();
    readonly Random noise = new(11);
    double brown;
    float rollGain;
    volatile float rollTarget;
    long frame;

    public float MasterGain { get; set; } = 0.75f;

    /// <summary>Total voices ever started and voices dropped by the polyphony cap (for tests).</summary>
    public int Started { get; private set; }
    public int Dropped { get; private set; }
    public int PeakVoices { get; private set; }
    public long FramesRendered => frame;

    /// <summary>Queues a sound. <paramref name="pan"/> 0 = left .. 1 = right; <paramref name="pitch"/> 1 = original.</summary>
    public void Play(float[] data, double gain, double pan = 0.5, double pitch = 1.0, long atFrame = -1)
    {
        if (gain <= 0.001) return;
        pan = Math.Clamp(pan, 0, 1);
        incoming.Enqueue(new Voice
        {
            Data = data,
            Step = pitch,
            GainL = (float)(gain * Math.Cos(pan * Math.PI / 2) * Math.Sqrt(2)),
            GainR = (float)(gain * Math.Sin(pan * Math.PI / 2) * Math.Sqrt(2)),
            StartFrame = atFrame,
        });
    }

    /// <summary>Loudness of the continuous ball-rolling rumble, 0..1 (smoothed internally).</summary>
    public void SetRolling(double level) => rollTarget = (float)Math.Clamp(level, 0, 1);

    /// <summary>Mixes <paramref name="frames"/> stereo frames into <paramref name="dest"/> (interleaved L,R).</summary>
    public void Render(short[] dest, int frames)
    {
        while (incoming.TryDequeue(out var v))
        {
            if (active.Count >= MaxVoices)
            {
                // Steal the quietest voice so a loud new hit is never lost.
                var quietest = active.OrderBy(a => a.GainL + a.GainR).First();
                Dropped++;
                if (quietest.GainL + quietest.GainR < v.GainL + v.GainR) active.Remove(quietest);
                else continue;
            }
            active.Add(v);
            Started++;
        }
        PeakVoices = Math.Max(PeakVoices, active.Count);

        for (int i = 0; i < frames; i++)
        {
            float l = 0, r = 0;
            for (int k = active.Count - 1; k >= 0; k--)
            {
                var v = active[k];
                if (v.StartFrame > frame + i) continue;
                int p = (int)v.Pos;
                if (p >= v.Data.Length - 1) { active.RemoveAt(k); continue; }
                float frac = (float)(v.Pos - p);
                float s = v.Data[p] + (v.Data[p + 1] - v.Data[p]) * frac;
                l += s * v.GainL;
                r += s * v.GainR;
                v.Pos += v.Step;
            }

            // Rolling: brown noise, gain eased towards the target to avoid zipper noise.
            rollGain += (rollTarget - rollGain) * 0.0008f;
            if (rollGain > 0.0005f)
            {
                brown = brown * 0.985 + (noise.NextDouble() * 2 - 1) * 0.015;
                float rumble = (float)(brown * 6) * rollGain;
                l += rumble;
                r += rumble;
            }

            dest[2 * i] = ToPcm(l * MasterGain);
            dest[2 * i + 1] = ToPcm(r * MasterGain);
        }
        frame += frames;
    }

    /// <summary>Soft limiter: linear up to 0.6, then compresses smoothly instead of clipping.</summary>
    static short ToPcm(float x)
    {
        float a = Math.Abs(x);
        if (a > 0.6f) x = Math.Sign(x) * (0.6f + 0.4f * (float)Math.Tanh((a - 0.6f) / 0.4f));
        return (short)(x * 32000);
    }
}

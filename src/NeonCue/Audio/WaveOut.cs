using System.Runtime.InteropServices;

namespace NeonCue.Audio;

/// <summary>
/// Streams a <see cref="Mixer"/> to the default Windows audio device through winmm waveOut,
/// with a ring of small buffers (constant ~70 ms latency, enough slack that a busy frame never
/// starves the device and crackles) refilled by a dedicated thread.
/// </summary>
public sealed class WaveOut : IDisposable
{
    const int Buffers = 6;
    const int FramesPerBuffer = 512;
    const uint WHDR_DONE = 0x1;
    const int HeaderSize = 48;    // WAVEHDR on x64
    const int FlagsOffset = 24;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, uint device, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

    readonly Mixer mixer;
    readonly IntPtr hwo;
    readonly IntPtr[] headers = new IntPtr[Buffers];
    readonly IntPtr[] data = new IntPtr[Buffers];
    readonly bool[] queued = new bool[Buffers];
    readonly short[] scratch = new short[FramesPerBuffer * 2];
    readonly Thread? thread;
    volatile bool running;

    public bool IsOpen { get; }

    public WaveOut(Mixer mixer)
    {
        this.mixer = mixer;
        if (!OperatingSystem.IsWindows()) return;
        var fmt = new WaveFormatEx
        {
            wFormatTag = 1,
            nChannels = 2,
            nSamplesPerSec = Mixer.SampleRate,
            nAvgBytesPerSec = Mixer.SampleRate * 4,
            nBlockAlign = 4,
            wBitsPerSample = 16,
        };
        try
        {
            if (waveOutOpen(out hwo, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
        }
        catch (DllNotFoundException) { return; }

        int bytes = FramesPerBuffer * 4;
        for (int i = 0; i < Buffers; i++)
        {
            data[i] = Marshal.AllocHGlobal(bytes);
            headers[i] = Marshal.AllocHGlobal(HeaderSize);
            for (int b = 0; b < HeaderSize; b++) Marshal.WriteByte(headers[i], b, 0);
            Marshal.WriteIntPtr(headers[i], 0, data[i]);
            Marshal.WriteInt32(headers[i], 8, bytes);
            waveOutPrepareHeader(hwo, headers[i], HeaderSize);
        }
        IsOpen = true;
        running = true;
        thread = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "NeonCue audio" };
        thread.Start();
    }

    void Pump()
    {
        while (running)
        {
            for (int i = 0; i < Buffers && running; i++)
            {
                uint flags = (uint)Marshal.ReadInt32(headers[i], FlagsOffset);
                if (queued[i] && (flags & WHDR_DONE) == 0) continue;
                mixer.Render(scratch, FramesPerBuffer);
                Marshal.Copy(scratch, 0, data[i], scratch.Length);
                Marshal.WriteInt32(headers[i], FlagsOffset, (int)(flags & ~WHDR_DONE));
                waveOutWrite(hwo, headers[i], HeaderSize);
                queued[i] = true;
            }
            Thread.Sleep(2);
        }
    }

    public void Dispose()
    {
        if (!IsOpen) return;
        running = false;
        thread?.Join(500);
        waveOutReset(hwo);
        for (int i = 0; i < Buffers; i++)
        {
            waveOutUnprepareHeader(hwo, headers[i], HeaderSize);
            Marshal.FreeHGlobal(headers[i]);
            Marshal.FreeHGlobal(data[i]);
        }
        waveOutClose(hwo);
    }
}

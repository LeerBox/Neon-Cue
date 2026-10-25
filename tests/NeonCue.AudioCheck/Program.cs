// Offline audio check: plays a real break plus follow-up shots through the physics engine,
// schedules every hit into the mixer at its exact time, renders a WAV and reports whether any
// sound was dropped, how many overlapped, and whether the output clipped. It also replays the
// same event list through the old one-sound-at-a-time rules for comparison.
//   dotnet run --project tests/NeonCue.AudioCheck -- [out.wav]
using NeonCue.Audio;
using NeonCue.Core;

string wavPath = args.Length > 0 ? args[0] : "audiocheck.wav";
var events = new List<(double time, string kind, double gain)>();
var listener = new Recorder(events);

var rules = GameRules.Create(GameMode.EightBall);
var s = new GameState { Mode = GameMode.EightBall, Players = [new PlayerState { Name = "A" }, new PlayerState { Name = "B" }] };
var rng = new Random(5);
rules.NewRack(s, rng);

double clock = 0;
void Shoot(double angle, double power)
{
    events.Add((clock, "cue", 0.3 + 0.6 * power));
    Physics.Strike(s.Cue, angle, power, 0, 0);
    var ev = new ShotEvents();
    listener.Clock = () => clock;
    while (Physics.Step(s.Balls, ev, listener)) clock += Physics.Dt;
    rules.ApplyShot(s, ev, rng);
    clock += 0.8;
}

s.Cue.Pos = new Vec2(Table.HeadStringX - 25, Table.Height / 2);
Shoot((s.ObjectBallsOnTable.OrderBy(b => b.Pos.X).First().Pos - s.Cue.Pos).Angle, 1.0);
for (int i = 0; i < 3 && s.ObjectBallsOnTable.Any(); i++)
{
    var t = s.ObjectBallsOnTable.OrderBy(b => Vec2.Distance(b.Pos, s.Cue.Pos)).First();
    Shoot((t.Pos - s.Cue.Pos).Angle, 0.55);
}

var bank = new SoundBank();
var mixer = new Mixer();
int totalFrames = (int)((clock + 0.5) * Mixer.SampleRate);
var pcm = new short[totalFrames * 2];
var block = new short[512 * 2];
int next = 0;
for (int f = 0; f < totalFrames; f += 512)
{
    int n = Math.Min(512, totalFrames - f);
    // Like the game: each sound is triggered when its moment arrives, placed at its exact frame.
    while (next < events.Count && events[next].time * Mixer.SampleRate < f + n)
    {
        var (time, kind, gain) = events[next++];
        float[] snd = kind switch { "clack" => bank.Clack, "cushion" => bank.Cushion, "pocket" => bank.Pocket, _ => bank.CueStrike("starter") };
        mixer.Play(snd, gain, 0.5, 1.0, (long)(time * Mixer.SampleRate));
    }
    mixer.Render(block, n);
    Array.Copy(block, 0, pcm, f * 2, n * 2);
}
int clipped = pcm.Count(v => Math.Abs((int)v) >= 32000);

// Silence check: wherever no sound has started in the last 0.5 s, the output must be exactly zero.
var starts = events.Select(e => (long)(e.time * Mixer.SampleRate)).OrderBy(x => x).ToArray();
long hold = (long)(0.5 * Mixer.SampleRate);
int noisy = 0, silentFrames = 0, si = 0;
for (long f = 0; f < totalFrames; f++)
{
    while (si < starts.Length && starts[si] <= f) si++;
    bool quiet = si == 0 || f - starts[si - 1] > hold;
    if (!quiet) continue;
    silentFrames++;
    if (pcm[2 * f] != 0 || pcm[2 * f + 1] != 0) noisy++;
}
int peak = pcm.Max(v => Math.Abs((int)v));
WriteWav(wavPath, pcm);

// Old system: one sound at a time, 35 ms minimum gap, lower priority skipped within 140 ms.
int oldPlayed = 0, oldCut = 0, lastPrio = 0;
double lastPlay = -1, lastEnd = -1;
var len = new Dictionary<string, double> { ["clack"] = 0.05, ["cushion"] = 0.07, ["cue"] = 0.09, ["pocket"] = 0.38 };
var prio = new Dictionary<string, int> { ["clack"] = 1, ["cushion"] = 0, ["cue"] = 2, ["pocket"] = 3 };
foreach (var (time, kind, gain) in events)
{
    if (kind == "clack" && gain < 0.02) continue; // old code also ignored very soft taps
    double since = time - lastPlay;
    if (since < 0.035) continue;
    if (prio[kind] < lastPrio && since < 0.14) continue;
    if (time < lastEnd) oldCut++;
    oldPlayed++;
    lastPlay = time;
    lastEnd = time + len[kind];
    lastPrio = prio[kind];
}
int audible = events.Count(e => e.gain >= 0.02);

Console.WriteLine($"sound events in the break + 3 shots : {events.Count} ({audible} audible)");
Console.WriteLine($"OLD player (one sound at a time)    : played {oldPlayed}, skipped {audible - oldPlayed}, cut off early {oldCut}");
Console.WriteLine($"NEW mixer                           : played {mixer.Started}, dropped {mixer.Dropped}, most at once {mixer.PeakVoices}");
Console.WriteLine($"output peak {peak * 100 / 32767}% of full scale, clipped samples {clipped}");
Console.WriteLine($"between hits: {silentFrames / (double)Mixer.SampleRate:0.0} s that should be silent, non-zero samples {noisy}");

// Each cue's strike: brightness (zero-crossings/s), ring length (time to -40 dB), loudness.
string cuesWav = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(wavPath))!, "cue-sounds.wav");
var feats = new List<(string id, double zcr, double ms, double rms)>();
var cueTrack = new List<short>();
foreach (var c in CueCatalog.All)
{
    var x = bank.CueStrike(c.Id);
    int zc = 0;
    for (int i = 1; i < x.Length; i++) if (x[i - 1] < 0 != x[i] < 0) zc++;
    float pk = x.Max(Math.Abs);
    int last = Array.FindLastIndex(x, v => Math.Abs(v) > pk * 0.01f);
    double rms = Math.Sqrt(x.Average(v => (double)v * v));
    feats.Add((c.Id, zc / (x.Length / (double)Mixer.SampleRate), last * 1000.0 / Mixer.SampleRate, rms));
    foreach (var v in x) { short q = (short)(v * 0.75f * 32000); cueTrack.Add(q); cueTrack.Add(q); }
    cueTrack.AddRange(new short[(int)(0.6 * Mixer.SampleRate) * 2]);
}
WriteWav(cuesWav, cueTrack.ToArray());
Console.WriteLine();
Console.WriteLine($"{"cue",-9} {"bright Hz",9} {"length ms",9} {"rms",6}");
foreach (var f in feats) Console.WriteLine($"{f.id,-9} {f.zcr,9:0} {f.ms,9:0} {f.rms,6:0.000}");
int samePairs = 0;
for (int i = 0; i < feats.Count; i++)
    for (int j = i + 1; j < feats.Count; j++)
    {
        var a = feats[i]; var b = feats[j];
        bool differ = Math.Abs(a.zcr - b.zcr) / Math.Max(a.zcr, b.zcr) > 0.08 || Math.Abs(a.ms - b.ms) / Math.Max(a.ms, b.ms) > 0.2;
        if (!differ) { samePairs++; Console.WriteLine($"  too similar: {a.id} / {b.id}"); }
    }
Console.WriteLine($"wrote {cuesWav}");
Console.WriteLine($"wrote {wavPath} ({totalFrames / (double)Mixer.SampleRate:0.0} s)");
bool ok = mixer.Dropped == 0 && clipped == 0 && noisy == 0 && samePairs == 0 && mixer.Started == events.Count(e => e.gain > 0.001);
Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
return ok ? 0 : 1;

static void WriteWav(string path, short[] pcm)
{
    using var w = new BinaryWriter(File.Create(path));
    int bytes = pcm.Length * 2;
    w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVEfmt "u8); w.Write(16);
    w.Write((short)1); w.Write((short)2); w.Write(Mixer.SampleRate); w.Write(Mixer.SampleRate * 4);
    w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
    foreach (var v in pcm) w.Write(v);
}

sealed class Recorder(List<(double time, string kind, double gain)> events) : IPhysicsListener
{
    public Func<double> Clock = () => 0;
    static double Curve(double v, double floor, double full, double exp) =>
        v <= floor ? 0 : Math.Pow(Math.Clamp((v - floor) / (full - floor), 0, 1), exp);
    public void BallCollision(Ball a, Ball b, double speed) => events.Add((Clock(), "clack", Curve(speed, 30, 1500, 0.7) * 0.85));
    public void CushionHit(Ball ball, double speed) => events.Add((Clock(), "cushion", Curve(speed, 60, 1400, 0.8) * 0.7));
    public void BallPocketed(Ball ball, Pocket pocket) => events.Add((Clock(), "pocket", 0.85));
}

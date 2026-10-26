namespace NeonCue;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // neoncue --render <out.png> [menu|game]  : offscreen screenshot for docs.
        if (args.Length >= 2 && args[0] == "--render")
        {
            Snapshot.Render(args[1], args.Length > 2 ? args[2] : "menu");
            return;
        }

        if (args.Length >= 2 && args[0] == "--bench")
        {
            Bench.Run(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--playtest")
        {
            bool ok = PlayTest.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 150);
            Environment.Exit(ok ? 0 : 1);
        }

        // Smooth frames: the UI thread outranks background work, and the GC avoids long pauses.
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Application.Run(new GameForm());
    }
}

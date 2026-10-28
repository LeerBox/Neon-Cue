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
            try
            {
                bool ok = PlayTest.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 150);
                Environment.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                File.WriteAllText(args[1] + ".err", ex.ToString());
                Environment.Exit(2);
            }
        }

        if (args.Length >= 1 && args[0] == "--undo-check")
            Environment.Exit(UndoCheck.Run() ? 0 : 1);

        if (args.Length >= 1 && args[0] == "--aim-check")
            Environment.Exit(AimCheck.Run() ? 0 : 1);

        if (args.Length >= 1 && args[0] == "--gdi-check")
            Environment.Exit(GdiCheck.Run() ? 0 : 1);

        // Smooth frames: the UI thread outranks background work, and the GC avoids long pauses.
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            using var dialog = new ThreadExceptionDialog(e.Exception);
            if (dialog.ShowDialog() == DialogResult.Abort) Environment.Exit(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
        };
        Application.Run(new GameForm());
    }

    /// <summary>Records the full exception. The dialog still shows it; the log is for the next diagnosis.</summary>
    static void WriteCrashLog(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeonCue");
            Directory.CreateDirectory(dir);
            string context;
            try
            {
                var s = AppSettings.Load();
                var d = s.DressFor(s.EquippedCue);
                context = $"mode={s.Mode} style={s.Style} cue={s.EquippedCue} guide={s.Guide} dress={d.Key}\r\n";
            }
            catch (Exception load)
            {
                context = "settings unreadable: " + load.Message + "\r\n";
            }
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"\r\n==== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ====\r\n{context}{ex}\r\n");
        }
        catch (Exception)
        {
            // A failed log must not replace the exception the player is about to see.
        }
    }
}

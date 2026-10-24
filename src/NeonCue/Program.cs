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

        Application.Run(new GameForm());
    }
}

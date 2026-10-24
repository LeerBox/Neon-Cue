using System.Diagnostics;
using System.Runtime.InteropServices;
using NeonCue.Rendering;
using NeonCue.Screens;

namespace NeonCue;

sealed class GameForm : Form, IHost
{
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);

    readonly System.Windows.Forms.Timer timer;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last;
    IScreen screen;

    public AppSettings Settings { get; }
    public SoundFx Sound { get; }

    public GameForm()
    {
        Settings = AppSettings.Load();
        Sound = new SoundFx { Enabled = Settings.Sound };

        Text = "Neon Cue";
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(980, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        try { Icon = Theme.CreateIcon(); } catch (ExternalException) { }

        screen = new MenuScreen(this);
        timeBeginPeriod(1);
        timer = new System.Windows.Forms.Timer { Interval = 8 };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, now - last);
        last = now;
        screen.Update(dt);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) => screen.Draw(e.Graphics, ClientSize);

    protected override void OnMouseMove(MouseEventArgs e) => screen.MouseMove(e);
    protected override void OnMouseDown(MouseEventArgs e) => screen.MouseDown(e);
    protected override void OnMouseUp(MouseEventArgs e) => screen.MouseUp(e);
    protected override void OnMouseWheel(MouseEventArgs e) => screen.MouseWheel(e);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        screen.KeyDown(keyData) || base.ProcessCmdKey(ref msg, keyData);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        Settings.Save();
        timeEndPeriod(1);
        Sound.Dispose();
        base.OnFormClosed(e);
    }

    public void ShowMenu() => screen = new MenuScreen(this);

    public void StartGame()
    {
        Settings.Save();
        screen = new GameScreen(this);
    }

    public void ShowShop() => screen = new ShopScreen(this);

    public void Quit() => Close();
}

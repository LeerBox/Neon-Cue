using System.Diagnostics;
using System.Runtime.InteropServices;
using NeonCue.Core;
using NeonCue.Rendering;
using NeonCue.Screens;

namespace NeonCue;

/// <summary>
/// Main window and game loop. Instead of a WinForms timer (coarse and uneven, ~10-16 ms), frames are
/// driven from the idle message loop and paced to the monitor's refresh rate with a precise wait,
/// then drawn straight into a back buffer, so every frame advances by an even amount of time.
/// </summary>
sealed class GameForm : Form, IHost
{
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeMessage
    {
        public IntPtr Handle;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public Point Location;
    }

    [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage msg, IntPtr hWnd, uint min, uint max, uint remove);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode dm);

    readonly Stopwatch clock = Stopwatch.StartNew();
    double last, nextFrame;
    BufferedGraphics? buffer;
    Size bufferSize;
    IScreen screen;

    public AppSettings Settings { get; }
    public SoundFx Sound { get; }

    public GameForm()
    {
        Settings = AppSettings.Load();
        Sound = new SoundFx { Enabled = Settings.Sound };
        Look.Style = Settings.Style;

        Text = "Neon Cue";
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(980, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
        try { Icon = Theme.CreateIcon(); } catch (ExternalException) { }
        if (!Settings.Fullscreen) WindowState = FormWindowState.Maximized;

        screen = new MenuScreen(this);
        timeBeginPeriod(1);
        Application.Idle += OnIdle;
    }

    bool fullscreen;
    Rectangle windowedBounds;
    bool windowedMaximized;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Settings.Fullscreen) EnterFullscreen();
    }

    void ToggleFullscreen()
    {
        if (fullscreen) ExitFullscreen();
        else EnterFullscreen();
        Settings.Fullscreen = fullscreen;
        Settings.Save();
    }

    void EnterFullscreen()
    {
        if (fullscreen) return;
        windowedMaximized = WindowState == FormWindowState.Maximized;
        if (windowedMaximized) WindowState = FormWindowState.Normal;
        windowedBounds = Bounds;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = Screen.FromControl(this).Bounds;
        fullscreen = true;
    }

    void ExitFullscreen()
    {
        if (!fullscreen) return;
        FormBorderStyle = FormBorderStyle.Sizable;
        fullscreen = false;
        if (windowedMaximized) WindowState = FormWindowState.Maximized;
        else Bounds = windowedBounds.Width > 0 ? windowedBounds : new Rectangle(Bounds.X, Bounds.Y, 1280, 800);
    }

    /// <summary>Frame period of the monitor the window is on (falls back to 144 Hz).</summary>
    double FramePeriod()
    {
        var dm = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
        string? device = IsHandleCreated ? Screen.FromControl(this).DeviceName : null;
        int hz = EnumDisplaySettings(device, -1, ref dm) && dm.dmDisplayFrequency > 1 ? dm.dmDisplayFrequency : 144;
        return 1.0 / Math.Clamp(hz, 30, 500);
    }

    double period = 1 / 144.0;
    double periodCheckedAt = -10;

    void OnIdle(object? sender, EventArgs e)
    {
        while (!PeekMessage(out _, IntPtr.Zero, 0, 0, 0) && !IsDisposed)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - periodCheckedAt > 2) { period = FramePeriod(); periodCheckedAt = now; }

            double wait = nextFrame - now;
            if (wait > 0)
            {
                // Sleep while far from the deadline, then spin the last moment for an exact cadence.
                if (wait > 0.0015) Thread.Sleep(1);
                else Thread.SpinWait(50);
                continue;
            }
            nextFrame = Math.Max(nextFrame + period, now - period);
            Frame(now);
        }
    }

    void Frame(double now)
    {
        double dt = Math.Clamp(now - last, 0, 0.05);
        last = now;
        screen.Update(dt);
        Render();
    }

    void Render()
    {
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0 || !IsHandleCreated) return;
        if (buffer == null || bufferSize != size)
        {
            buffer?.Dispose();
            using var target = CreateGraphics();
            buffer = BufferedGraphicsManager.Current.Allocate(target, new Rectangle(Point.Empty, size));
            bufferSize = size;
        }
        screen.Draw(buffer.Graphics, size);
        using var g = CreateGraphics();
        buffer.Render(g);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) => Render();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Render();
    }

    protected override void OnMouseMove(MouseEventArgs e) => screen.MouseMove(e);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Capture keeps move events arriving after the pointer leaves the window.
        if (e.Button == MouseButtons.Left) Capture = true;
        screen.MouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        screen.MouseUp(e);
        if (e.Button == MouseButtons.Left) Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) screen.PointerCaptureLost();
    }

    protected override void OnMouseWheel(MouseEventArgs e) => screen.MouseWheel(e);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.F11)
        {
            ToggleFullscreen();
            return true;
        }
        return screen.KeyDown(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Application.Idle -= OnIdle;
        Settings.Save();
        timeEndPeriod(1);
        Sound.Dispose();
        buffer?.Dispose();
        base.OnFormClosed(e);
    }

    public void ShowMenu() => screen = new MenuScreen(this);

    public void StartGame()
    {
        Settings.Save();
        screen = new GameScreen(this);
    }

    public void ShowShop() => screen = new ShopScreen(this);

    public void ShowDress(string cueId)
    {
        var cue = CueCatalog.Get(cueId);
        if (!Settings.Owns(cue)) { ShowShop(); return; }
        screen = new DressScreen(this, cueId);
    }

    public void Quit() => Close();
}

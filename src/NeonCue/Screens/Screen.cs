using System.Drawing.Drawing2D;
using NeonCue.Rendering;

namespace NeonCue.Screens;

interface IHost
{
    AppSettings Settings { get; }
    SoundFx Sound { get; }
    void ShowMenu();
    void StartGame();
    void ShowShop();
    void Quit();
}

interface IScreen
{
    void Update(double dt);
    void Draw(Graphics g, Size size);
    void MouseMove(MouseEventArgs e) { }
    void MouseDown(MouseEventArgs e) { }
    void MouseUp(MouseEventArgs e) { }
    void MouseWheel(MouseEventArgs e) { }
    bool KeyDown(Keys keyData) => false;
}

sealed class UiButton
{
    public RectangleF Rect;
    public string Text = "";
    public Action OnClick = () => { };
    public bool Primary;
    public bool Hover;
    public Color Accent = Theme.Cyan;

    public bool Contains(Point p) => Rect.Contains(p);

    /// <summary>Primary: solid champagne gold with dark lettering. Secondary: hairline outline that warms to gold on hover.</summary>
    public void Draw(Graphics g, float u)
    {
        float radius = 4 * u;
        using var path = Gfx.RoundRect(Rect, radius);
        if (Primary)
        {
            using var br = new LinearGradientBrush(Rect, Hover ? Ui.GoldBright : Theme.Mix(Ui.GoldBright, Ui.Gold, 0.4), Hover ? Ui.Gold : Theme.Mix(Ui.Gold, Color.Black, 0.12), 90f);
            g.FillPath(br, path);
        }
        else
        {
            using var br = new SolidBrush(Color.FromArgb(Hover ? 220 : 150, Hover ? Ui.Panel2 : Ui.Panel));
            g.FillPath(br, path);
            using var pen = new Pen(Hover ? Ui.Gold : Ui.LineStrong, 1f);
            g.DrawPath(pen, path);
        }
        var font = Ui.Head((Primary ? 19 : 14) * u);
        Ui.Tracked(g, Text.ToUpperInvariant(), font, Primary ? Ui.Ink : Hover ? Color.White : Ui.Text,
            Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2, (Primary ? 3.2f : 2.2f) * u, StringAlignment.Center);
    }

    public static bool Hit(IEnumerable<UiButton> buttons, Point p)
    {
        foreach (var b in buttons)
        {
            if (b.Contains(p)) { b.OnClick(); return true; }
        }
        return false;
    }

    public static void UpdateHover(IEnumerable<UiButton> buttons, Point p)
    {
        foreach (var b in buttons) b.Hover = b.Contains(p);
    }
}

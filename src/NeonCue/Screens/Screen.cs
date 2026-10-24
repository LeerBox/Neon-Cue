using System.Drawing.Drawing2D;
using NeonCue.Rendering;

namespace NeonCue.Screens;

interface IHost
{
    AppSettings Settings { get; }
    SoundFx Sound { get; }
    void ShowMenu();
    void StartGame();
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

    public void Draw(Graphics g, float u)
    {
        using var path = Gfx.RoundRect(Rect, Rect.Height / 2);
        if (Primary)
        {
            using var br = new LinearGradientBrush(Rect, Theme.A(Theme.Magenta, Hover ? 255 : 215), Theme.A(Theme.Violet, Hover ? 255 : 215), 0f);
            g.FillPath(br, path);
            Gfx.GlowPath(g, path, Theme.Magenta, 1.5f * u, (Hover ? 4 : 3) * u, 4, Hover ? 48 : 30);
        }
        else
        {
            using var br = new SolidBrush(Theme.A(Hover ? Theme.PanelLight : Theme.Panel, 235));
            g.FillPath(br, path);
            if (Hover) Gfx.GlowPath(g, path, Accent, 1.4f * u, 2.5f * u, 3, 40);
            else
            {
                using var pen = new Pen(Theme.A(Accent, 120), 1.2f * u);
                g.DrawPath(pen, path);
            }
        }
        Gfx.Text(g, Text, Theme.F((Primary ? 22 : 16) * u, FontStyle.Bold), Primary ? Color.White : Theme.Text, Rect, StringAlignment.Center);
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

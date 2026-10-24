using System.Drawing.Drawing2D;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue.Screens;

sealed class MenuScreen : IScreen
{
    sealed class Row
    {
        public string Label = "";
        public Func<string> Value = () => "";
        public Action<int> Change = _ => { };
        public Func<bool> Enabled = () => true;
        public RectangleF Rect;
    }

    sealed class Floater
    {
        public PointF Pos;
        public PointF Vel;
        public int Number;
    }

    static readonly GameMode[] Modes = Enum.GetValues<GameMode>();
    static readonly int[] RaceOptions = [1, 3, 5, 7, 9];
    static readonly int[] TargetOptions = [25, 50, 75, 100, 150];

    readonly IHost host;
    readonly AppSettings cfg;
    readonly List<Row> rows = new();
    readonly List<UiButton> buttons = new();
    readonly List<Floater> floaters = new();
    readonly BallSprites sprites = new();
    readonly Random rng = new();
    int selected = -1;
    int hoverRow = -1;
    bool showHelp;
    double time;
    float u = 1;
    Size size;

    public MenuScreen(IHost host)
    {
        this.host = host;
        cfg = host.Settings;

        rows.Add(new Row
        {
            Label = "GAME MODE",
            Value = () => GameRules.Create(cfg.Mode).Name,
            Change = d => cfg.Mode = Modes[(Array.IndexOf(Modes, cfg.Mode) + d + Modes.Length) % Modes.Length],
        });
        rows.Add(new Row
        {
            Label = "OPPONENT",
            Value = () => cfg.Mode == GameMode.Practice
                ? (cfg.Opponent == OpponentType.CpuVsCpu ? "Watch the CPU" : "Solo")
                : cfg.Opponent switch { OpponentType.Cpu => "You vs CPU", OpponentType.TwoPlayer => "2 Players (local)", _ => "CPU vs CPU" },
            Change = d => cfg.Opponent = (OpponentType)(((int)cfg.Opponent + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "CPU LEVEL",
            Value = () => cfg.Level switch
            {
                AILevel.Rookie => "Rookie",
                AILevel.Amateur => "Amateur",
                AILevel.Pro => "Pro",
                _ => "Master",
            },
            Change = d => cfg.Level = (AILevel)(((int)cfg.Level + d + 4) % 4),
            Enabled = () => cfg.Opponent != OpponentType.TwoPlayer || cfg.Mode == GameMode.Practice,
        });
        rows.Add(new Row
        {
            Label = "MATCH",
            Value = () => cfg.Mode switch
            {
                GameMode.StraightPool => $"First to {cfg.TargetScore}",
                GameMode.Rotation => "First to 61",
                GameMode.Practice => "Endless",
                _ => cfg.RaceTo == 1 ? "Single rack" : $"Race to {cfg.RaceTo}",
            },
            Change = d =>
            {
                if (cfg.Mode == GameMode.StraightPool) cfg.TargetScore = Cycle(TargetOptions, cfg.TargetScore, d);
                else cfg.RaceTo = Cycle(RaceOptions, cfg.RaceTo, d);
            },
            Enabled = () => cfg.Mode is not (GameMode.Rotation or GameMode.Practice),
        });
        rows.Add(new Row
        {
            Label = "AIM GUIDE",
            Value = () => cfg.Guide switch { AimGuide.Off => "Off (pro)", AimGuide.GhostBall => "Ghost ball", _ => "Full prediction" },
            Change = d => cfg.Guide = (AimGuide)(((int)cfg.Guide + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "SOUND",
            Value = () => cfg.Sound ? "On" : "Off",
            Change = _ => { cfg.Sound = !cfg.Sound; host.Sound.Enabled = cfg.Sound; },
        });

        buttons.Add(new UiButton { Text = "PLAY", Primary = true, OnClick = Play });
        buttons.Add(new UiButton { Text = "HOW TO PLAY", OnClick = () => showHelp = true });
        buttons.Add(new UiButton { Text = "QUIT", Accent = Theme.Magenta, OnClick = host.Quit });
    }

    static int Cycle(int[] options, int current, int d)
    {
        int i = Array.IndexOf(options, current);
        if (i < 0) i = 0;
        return options[(i + d + options.Length) % options.Length];
    }

    void Play()
    {
        host.Sound.Click();
        host.StartGame();
    }

    public void Update(double dt)
    {
        time += dt;
        foreach (var f in floaters)
        {
            f.Pos = new PointF(f.Pos.X + f.Vel.X * (float)dt, f.Pos.Y + f.Vel.Y * (float)dt);
            if (f.Pos.X < 0 || f.Pos.X > size.Width) f.Vel = new PointF(-f.Vel.X, f.Vel.Y);
            if (f.Pos.Y < 0 || f.Pos.Y > size.Height) f.Vel = new PointF(f.Vel.X, -f.Vel.Y);
        }
    }

    void Layout(Size s)
    {
        size = s;
        u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.75f, 2.2f);
        float cx = s.Width / 2f;
        float w = 620 * u, h = 50 * u, gap = 8 * u, y = 214 * u;
        for (int i = 0; i < rows.Count; i++)
            rows[i].Rect = new RectangleF(cx - w / 2, y + i * (h + gap), w, h);
        float by = y + rows.Count * (h + gap) + 54 * u;
        buttons[0].Rect = new RectangleF(cx - 150 * u, by, 300 * u, 60 * u);
        buttons[1].Rect = new RectangleF(cx - 200 * u, by + 76 * u, 190 * u, 44 * u);
        buttons[2].Rect = new RectangleF(cx + 10 * u, by + 76 * u, 190 * u, 44 * u);

        if (floaters.Count == 0)
        {
            for (int i = 0; i < 14; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2, sp = 25 + rng.NextDouble() * 45;
                floaters.Add(new Floater
                {
                    Pos = new PointF((float)(rng.NextDouble() * s.Width), (float)(rng.NextDouble() * s.Height)),
                    Vel = new PointF((float)(Math.Cos(a) * sp), (float)(Math.Sin(a) * sp)),
                    Number = 1 + i % 15,
                });
            }
        }
    }

    public void Draw(Graphics g, Size s)
    {
        Layout(s);
        Theme.Setup(g);
        var full = new Rectangle(Point.Empty, s);
        using (var bg = new LinearGradientBrush(full, Theme.BgTop, Theme.Bg, 90f)) g.FillRectangle(bg, full);

        sprites.Ensure(26 * u);
        foreach (var f in floaters) sprites.Draw(g, f.Number, f.Pos);
        using (var veil = new SolidBrush(Color.FromArgb(185, 7, 6, 15))) g.FillRectangle(veil, full);

        // Scanlines + glow spot.
        using (var hp = new GraphicsPath())
        {
            hp.AddEllipse(s.Width / 2f - 520 * u, -160 * u, 1040 * u, 520 * u);
            using var hb = new PathGradientBrush(hp) { CenterColor = Color.FromArgb(60, 255, 43, 214), SurroundColors = [Color.FromArgb(0, 0, 0, 0)] };
            g.FillPath(hb, hp);
        }
        using (var sl = new Pen(Color.FromArgb(10, 255, 255, 255)))
            for (int y = 0; y < s.Height; y += 4) g.DrawLine(sl, 0, y, s.Width, y);

        float cx = s.Width / 2f;
        double pulse = 0.5 + 0.5 * Math.Sin(time * 2.2);
        Gfx.GlowText(g, "NEON CUE", Theme.F(98 * u, FontStyle.Bold), Theme.Magenta, Theme.Mix(Color.White, Theme.Magenta, 0.25),
            new PointF(cx, 108 * u), spread: (float)(4 + pulse * 2) * u, layers: 5, alpha: 30);
        Gfx.Text(g, "SMART BILLIARDS  •  6 GAME MODES  •  4 AI LEVELS", Theme.F(17 * u, FontStyle.Bold), Theme.Cyan,
            new RectangleF(0, 160 * u, s.Width, 30 * u), StringAlignment.Center);

        for (int i = 0; i < rows.Count; i++) DrawRow(g, rows[i], i);

        var rules = GameRules.Create(cfg.Mode);
        var last = rows[^1].Rect;
        Gfx.Text(g, rules.Tagline, Theme.F(15 * u), Theme.TextDim, new RectangleF(0, last.Bottom + 10 * u, s.Width, 30 * u), StringAlignment.Center);

        foreach (var b in buttons) b.Draw(g, u);

        Gfx.Text(g, "↑↓ select   ←→ change   Enter play   F1 help", Theme.F(13 * u), Theme.A(Theme.TextDim, 170),
            new RectangleF(0, s.Height - 34 * u, s.Width, 24 * u), StringAlignment.Center);
        Gfx.Text(g, "v1.0", Theme.F(12 * u), Theme.A(Theme.TextDim, 120), new RectangleF(0, s.Height - 34 * u, s.Width - 16 * u, 24 * u), StringAlignment.Far);

        if (showHelp) DrawHelp(g, s);
    }

    void DrawRow(Graphics g, Row row, int index)
    {
        bool enabled = row.Enabled();
        bool active = enabled && (index == selected || index == hoverRow);
        var r = row.Rect;
        using var path = Gfx.RoundRect(r, 12 * u);
        using (var br = new SolidBrush(Theme.A(active ? Theme.PanelLight : Theme.Panel, 225))) g.FillPath(br, path);
        if (active) Gfx.GlowPath(g, path, Theme.Cyan, 1.3f * u, 2.5f * u, 3, 34);
        else using (var pen = new Pen(Theme.A(Theme.Violet, 70), 1f * u)) g.DrawPath(pen, path);

        int alpha = enabled ? 255 : 90;
        Gfx.Text(g, row.Label, Theme.F(15 * u, FontStyle.Bold), Theme.A(Theme.TextDim, alpha), new RectangleF(r.X + 22 * u, r.Y, 220 * u, r.Height));

        float vx = r.X + 270 * u, vw = r.Right - 20 * u - vx;
        Gfx.Text(g, row.Value(), Theme.F(19 * u, FontStyle.Bold), Theme.A(Theme.Text, alpha), new RectangleF(vx, r.Y, vw, r.Height), StringAlignment.Center);
        if (enabled)
        {
            Color ac = active ? Theme.Cyan : Theme.A(Theme.Cyan, 140);
            Gfx.Triangle(g, new PointF(vx + 10 * u, r.Y + r.Height / 2), 12 * u, false, ac);
            Gfx.Triangle(g, new PointF(vx + vw - 10 * u, r.Y + r.Height / 2), 12 * u, true, ac);
        }
    }

    void DrawHelp(Graphics g, Size s)
    {
        using (var dim = new SolidBrush(Color.FromArgb(200, 4, 3, 10))) g.FillRectangle(dim, new Rectangle(Point.Empty, s));
        var r = new RectangleF(s.Width / 2f - 430 * u, s.Height / 2f - 330 * u, 860 * u, 660 * u);
        using var path = Gfx.RoundRect(r, 20 * u);
        using (var br = new SolidBrush(Theme.A(Theme.Panel, 245))) g.FillPath(br, path);
        Gfx.GlowPath(g, path, Theme.Cyan, 1.5f * u, 3 * u, 4, 30);

        Gfx.GlowText(g, "HOW TO PLAY", Theme.F(34 * u, FontStyle.Bold), Theme.Cyan, Color.White, new PointF(r.X + r.Width / 2, r.Y + 42 * u));

        string controls =
            "Aim — move the mouse; the cue follows it.\n" +
            "Shoot — press and drag back from the target, release. Further = harder.\n" +
            "Fine aim — mouse wheel or ← →  (hold Shift for big steps).\n" +
            "Keyboard shot — ↑ ↓ set power, Space shoots.\n" +
            "Spin — click the cue ball widget (bottom right) or W A S D. R resets.\n" +
            "Ball in hand — move the cue ball, click to place; click it again to pick up.\n" +
            "H — ask the AI coach for a shot.   G — aim guide.   F — fast forward.\n" +
            "M — sound.   Esc — pause menu.";
        Gfx.Text(g, "CONTROLS", Theme.F(16 * u, FontStyle.Bold), Theme.Magenta, new RectangleF(r.X + 40 * u, r.Y + 80 * u, 300 * u, 24 * u));
        Gfx.WrappedText(g, controls, Theme.F(15 * u), Theme.Text, new RectangleF(r.X + 40 * u, r.Y + 108 * u, r.Width - 80 * u, 220 * u));

        Gfx.Text(g, "GAME MODES", Theme.F(16 * u, FontStyle.Bold), Theme.Magenta, new RectangleF(r.X + 40 * u, r.Y + 330 * u, 300 * u, 24 * u));
        float y = r.Y + 360 * u;
        foreach (var m in Modes)
        {
            var rules = GameRules.Create(m);
            Gfx.Text(g, rules.Name, Theme.F(15 * u, FontStyle.Bold), Theme.Cyan, new RectangleF(r.X + 40 * u, y, 150 * u, 24 * u));
            Gfx.Text(g, rules.Tagline, Theme.F(14 * u), Theme.TextDim, new RectangleF(r.X + 190 * u, y, r.Width - 230 * u, 24 * u));
            y += 30 * u;
        }
        Gfx.Text(g, "Fouls give your opponent ball in hand anywhere on the table.", Theme.F(14 * u), Theme.Amber,
            new RectangleF(r.X + 40 * u, y + 6 * u, r.Width - 80 * u, 24 * u));
        Gfx.Text(g, "Click anywhere or press Esc to close", Theme.F(13 * u), Theme.TextDim, new RectangleF(r.X, r.Bottom - 40 * u, r.Width, 24 * u), StringAlignment.Center);
    }

    public void MouseMove(MouseEventArgs e)
    {
        if (showHelp) return;
        UiButton.UpdateHover(buttons, e.Location);
        hoverRow = rows.FindIndex(r => r.Rect.Contains(e.Location));
    }

    public void MouseDown(MouseEventArgs e)
    {
        if (showHelp) { showHelp = false; return; }
        if (UiButton.Hit(buttons, e.Location)) return;
        int i = rows.FindIndex(r => r.Rect.Contains(e.Location));
        if (i < 0 || !rows[i].Enabled()) return;
        selected = i;
        var r = rows[i].Rect;
        float mid = r.X + 270 * u + (r.Right - 20 * u - (r.X + 270 * u)) / 2;
        int d = e.Button == MouseButtons.Right || e.X < mid ? -1 : 1;
        rows[i].Change(d);
        host.Sound.Click();
    }

    public void MouseWheel(MouseEventArgs e)
    {
        int i = rows.FindIndex(r => r.Rect.Contains(e.Location));
        if (i >= 0 && rows[i].Enabled()) rows[i].Change(e.Delta > 0 ? 1 : -1);
    }

    public bool KeyDown(Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        if (showHelp)
        {
            if (k is Keys.Escape or Keys.Enter or Keys.F1 or Keys.Space) { showHelp = false; return true; }
            return false;
        }
        switch (k)
        {
            case Keys.Up:
                selected = NextEnabled(selected, -1);
                return true;
            case Keys.Down:
                selected = NextEnabled(selected, 1);
                return true;
            case Keys.Left:
            case Keys.Right:
                if (selected >= 0 && rows[selected].Enabled())
                {
                    rows[selected].Change(k == Keys.Right ? 1 : -1);
                    host.Sound.Click();
                }
                return true;
            case Keys.Enter:
            case Keys.Space:
                Play();
                return true;
            case Keys.F1:
                showHelp = true;
                return true;
        }
        return false;
    }

    int NextEnabled(int from, int dir)
    {
        int i = from;
        for (int n = 0; n < rows.Count; n++)
        {
            i = (i + dir + rows.Count) % rows.Count;
            if (rows[i].Enabled()) return i;
        }
        return from;
    }
}

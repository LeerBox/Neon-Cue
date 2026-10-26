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

    static readonly GameMode[] Modes = Enum.GetValues<GameMode>();
    static readonly int[] RaceOptions = [1, 3, 5, 7, 9];
    static readonly int[] TargetOptions = [25, 50, 75, 100, 150];

    readonly IHost host;
    readonly AppSettings cfg;
    readonly List<Row> rows = new();
    readonly List<UiButton> buttons = new();
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
        Look.Snooker = false;

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
                GameMode.Snooker => cfg.RaceTo == 1 ? "Single frame" : $"Best of {2 * cfg.RaceTo - 1} frames",
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
            Label = "AIM ASSIST",
            Value = () => cfg.Assist switch { AimAssist.Off => "Off (free aim)", AimAssist.Light => "Light", _ => "Strong" },
            Change = d => cfg.Assist = (AimAssist)(((int)cfg.Assist + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "STROKE ERROR",
            Value = () => cfg.StrokeError ? "Realistic (cue wobble)" : "Off (exact)",
            Change = _ => cfg.StrokeError = !cfg.StrokeError,
        });
        rows.Add(new Row
        {
            Label = "TABLE STYLE",
            Value = () => Look.Name(cfg.Style),
            Change = d =>
            {
                cfg.Style = (TableStyle)(((int)cfg.Style + d + 4) % 4);
                Look.Style = cfg.Style;
            },
        });
        rows.Add(new Row
        {
            Label = "SOUND",
            Value = () => cfg.Sound ? "On" : "Off",
            Change = _ => { cfg.Sound = !cfg.Sound; host.Sound.Enabled = cfg.Sound; },
        });

        buttons.Add(new UiButton { Text = "PLAY", Primary = true, OnClick = Play });
        buttons.Add(new UiButton { Text = "CUE SHOP", OnClick = host.ShowShop });
        buttons.Add(new UiButton { Text = "HOW TO PLAY", OnClick = () => showHelp = true });
        buttons.Add(new UiButton { Text = "QUIT", OnClick = host.Quit });
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

    public void Update(double dt) => time += dt;

    const float ValueX = 230;

    void Layout(Size s)
    {
        size = s;
        u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.75f, 2.2f);
        float x = 72 * u, w = 560 * u, h = 40 * u, y = 236 * u;
        for (int i = 0; i < rows.Count; i++)
            rows[i].Rect = new RectangleF(x, y + i * h, w, h);
        float by = y + rows.Count * h + 54 * u;
        float bw = (w - 16 * u) / 3;
        buttons[0].Rect = new RectangleF(x, by, w, 54 * u);
        for (int i = 1; i < 4; i++)
            buttons[i].Rect = new RectangleF(x + (i - 1) * (bw + 8 * u), by + 64 * u, bw, 40 * u);
    }

    public void Draw(Graphics g, Size s)
    {
        Layout(s);
        Theme.Setup(g);
        var full = new Rectangle(Point.Empty, s);
        using (var bg = new LinearGradientBrush(full, Theme.Hex("#0E1114"), Ui.Bg, 90f)) g.FillRectangle(bg, full);

        // A soft pool of cloth-coloured light on the right, like a table under a lamp.
        using (var hp = new GraphicsPath())
        {
            var spot = new RectangleF(s.Width * 0.42f, s.Height * 0.08f, s.Width * 0.7f, s.Height * 0.84f);
            hp.AddEllipse(spot);
            using var hb = new PathGradientBrush(hp) { CenterColor = Color.FromArgb(70, 30, 92, 70), SurroundColors = [Color.FromArgb(0, 0, 0, 0)] };
            g.FillPath(hb, hp);
        }
        Ui.Hairline(g, 0, 64 * u, s.Width, 64 * u);

        float x = 72 * u;
        Ui.Tracked(g, "NEON CUE", Ui.Head(64 * u), Ui.Text, x, 128 * u, 8 * u);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, x, 170 * u, 80 * u, 2 * u);
        Ui.Tracked(g, "PROFESSIONAL BILLIARDS", Ui.Head(14 * u), Ui.Text2, x, 194 * u, 4 * u);

        var first = rows[0].Rect;
        var card = new RectangleF(first.X - 1, first.Y - 1, first.Width + 2, rows.Count * first.Height + 2);
        Ui.Shadow(g, card, 6 * u, 10 * u);
        Ui.Card(g, card, 6 * u);
        for (int i = 0; i < rows.Count; i++) DrawRow(g, rows[i], i);

        var rules = GameRules.Create(cfg.Mode);
        Ui.Write(g, rules.Tagline, Ui.Body(14 * u), Ui.Text2, new RectangleF(x, card.Bottom + 10 * u, card.Width, 24 * u));

        foreach (var b in buttons) b.Draw(g, u);

        var cue = cfg.Equipped;
        Ui.Tracked(g, "COINS", Ui.Head(12 * u), Ui.Text3, s.Width - 260 * u, 32 * u, 2.5f * u);
        Ui.Write(g, cfg.Coins.ToString("N0"), Ui.Num(20 * u), Ui.Gold, new RectangleF(s.Width - 200 * u, 18 * u, 170 * u, 28 * u), StringAlignment.Far);
        Ui.Write(g, cue.Name, Ui.BodyStrong(14 * u), CueArt.TierColor(cue.Tier), new RectangleF(s.Width - 460 * u, 18 * u, 180 * u, 28 * u), StringAlignment.Far);

        Ui.Hairline(g, 0, s.Height - 44 * u, s.Width, s.Height - 44 * u);
        Ui.Tracked(g, "↑↓ SELECT    ←→ CHANGE    ENTER PLAY    C CUE SHOP    F1 HELP", Ui.Head(11 * u), Ui.Text3, x, s.Height - 22 * u, 1.8f * u);
        Ui.Tracked(g, "V1.11", Ui.Head(11 * u), Ui.Text3, s.Width - 24 * u, s.Height - 22 * u, 1.8f * u, StringAlignment.Far);

        if (showHelp) DrawHelp(g, s);
    }

    void DrawRow(Graphics g, Row row, int index)
    {
        bool enabled = row.Enabled();
        bool active = enabled && (index == selected || index == hoverRow);
        var r = row.Rect;
        if (active)
        {
            using var br = new SolidBrush(Ui.Panel2);
            g.FillRectangle(br, r.X + 1, r.Y, r.Width - 2, r.Height);
            using var gb = new SolidBrush(Ui.Gold);
            g.FillRectangle(gb, r.X + 1, r.Y, 3 * u, r.Height);
        }
        if (index > 0) Ui.Hairline(g, r.X + 16 * u, r.Y, r.Right - 16 * u, r.Y);

        float cy = r.Y + r.Height / 2;
        Ui.Tracked(g, row.Label, Ui.Head(12 * u), enabled ? (active ? Ui.Text2 : Ui.Text3) : Color.FromArgb(90, Ui.Text3), r.X + 22 * u, cy, 2.2f * u);

        float vx = r.X + ValueX * u, vw = r.Right - 18 * u - vx;
        Color vc = enabled ? (active ? Color.White : Ui.Text) : Color.FromArgb(80, Ui.Text);
        Ui.Write(g, row.Value(), Ui.BodyStrong(15 * u), vc, new RectangleF(vx, r.Y, vw, r.Height), StringAlignment.Center);
        if (enabled)
        {
            Color ac = active ? Ui.Gold : Ui.Text3;
            Ui.Chevron(g, new PointF(vx + 8 * u, cy), 10 * u, false, ac, 1.4f * u);
            Ui.Chevron(g, new PointF(vx + vw - 8 * u, cy), 10 * u, true, ac, 1.4f * u);
        }
    }

    void DrawHelp(Graphics g, Size s)
    {
        Ui.Scrim(g, s, 200);
        var r = new RectangleF(s.Width / 2f - 430 * u, s.Height / 2f - 330 * u, 860 * u, 660 * u);
        Ui.Shadow(g, r, 6 * u, 14 * u);
        Ui.Card(g, r, 6 * u, 250);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, r.X, r.Y, r.Width, 3 * u);
        Ui.Heading(g, "How to play", r.X + r.Width / 2, r.Y + 44 * u, u, 26);

        string controls =
            "Aim — move the mouse; the cue follows it.\n" +
            "Shoot — press (on the cue ball or the target), drag back, release. Further = harder.\n" +
            "Fine aim — mouse wheel or ← →  (hold Shift for big steps); hold Ctrl and move for precise aim.\n" +
            "Keyboard shot — ↑ ↓ set power, Space shoots.\n" +
            "Spin — click the cue ball widget (bottom right) or W A S D. R resets.\n" +
            "Ball in hand — move the cue ball, click to place; click it again to pick up.\n" +
            "H — ask the AI coach for a shot.   G — aim guide.   F — fast forward.\n" +
            "M — sound.   Esc — pause menu.   C (in menu) — cue shop: better cues hit harder, spin more, wobble less.";
        Ui.Tracked(g, "CONTROLS", Ui.Head(13 * u), Ui.Gold, r.X + 40 * u, r.Y + 96 * u, 2.5f * u);
        Gfx.WrappedText(g, controls, Ui.Body(14 * u), Ui.Text, new RectangleF(r.X + 40 * u, r.Y + 110 * u, r.Width - 80 * u, 220 * u));

        Ui.Tracked(g, "GAME MODES", Ui.Head(13 * u), Ui.Gold, r.X + 40 * u, r.Y + 344 * u, 2.5f * u);
        float y = r.Y + 360 * u;
        foreach (var m in Modes)
        {
            var rules = GameRules.Create(m);
            Ui.Write(g, rules.Name, Ui.BodyStrong(14 * u), Ui.Text, new RectangleF(r.X + 40 * u, y, 150 * u, 24 * u));
            Ui.Write(g, rules.Tagline, Ui.Body(14 * u), Ui.Text2, new RectangleF(r.X + 190 * u, y, r.Width - 230 * u, 24 * u));
            y += 30 * u;
        }
        Ui.Write(g, "Fouls give your opponent ball in hand anywhere on the table.", Ui.Body(14 * u), Ui.Warn,
            new RectangleF(r.X + 40 * u, y + 6 * u, r.Width - 80 * u, 24 * u));
        Ui.Write(g, "Click anywhere or press Esc to close", Ui.Body(13 * u), Ui.Text3, new RectangleF(r.X, r.Bottom - 40 * u, r.Width, 24 * u), StringAlignment.Center);
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
        float mid = r.X + ValueX * u + (r.Right - 18 * u - (r.X + ValueX * u)) / 2;
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
            case Keys.C:
                host.ShowShop();
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

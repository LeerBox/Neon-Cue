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
    readonly BallRenderer sprites = new();
    readonly Mat3[] heroOri;
    static readonly int[] RackOrder = [1, 9, 2, 10, 8, 3, 11, 7, 14, 4, 5, 13, 15, 6, 12];
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
        var seed = new Random(11);
        heroOri = Enumerable.Range(0, 16).Select(_ => Mat3.RandomResting(seed)).ToArray();

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
                ? (cfg.Opponent == OpponentType.CpuVsCpu ? "Exhibition" : "Solo practice")
                : cfg.Opponent switch { OpponentType.Cpu => "Versus computer", OpponentType.TwoPlayer => "Two players", _ => "Exhibition" },
            Change = d => cfg.Opponent = (OpponentType)(((int)cfg.Opponent + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "SKILL",
            Value = () => AppSettings.SkillName(cfg.Level),
            Change = d => cfg.Level = (AILevel)(((int)cfg.Level + d + 4) % 4),
            Enabled = () => cfg.Opponent != OpponentType.TwoPlayer || cfg.Mode == GameMode.Practice,
        });
        rows.Add(new Row
        {
            Label = "MATCH",
            Value = () => cfg.Mode switch
            {
                GameMode.StraightPool => $"To {cfg.TargetScore} points",
                GameMode.Rotation => "To 61 points",
                GameMode.Practice => "Open session",
                GameMode.Snooker => cfg.RaceTo == 1 ? "One frame" : $"Best of {2 * cfg.RaceTo - 1}",
                _ => cfg.RaceTo == 1 ? "One rack" : $"Race to {cfg.RaceTo}",
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
            Value = () => cfg.Guide switch { AimGuide.Off => "Off", AimGuide.GhostBall => "Ghost ball", _ => "Full path" },
            Change = d => cfg.Guide = (AimGuide)(((int)cfg.Guide + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "AIM ASSIST",
            Value = () => cfg.Assist switch { AimAssist.Off => "Off", AimAssist.Light => "Standard", _ => "Strong" },
            Change = d => cfg.Assist = (AimAssist)(((int)cfg.Assist + d + 3) % 3),
        });
        rows.Add(new Row
        {
            Label = "STROKE ERROR",
            Value = () => cfg.StrokeError ? "Natural" : "Exact",
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
        buttons.Add(new UiButton { Text = "CUES", OnClick = host.ShowShop });
        buttons.Add(new UiButton { Text = "RULES", OnClick = () => showHelp = true });
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
        float x = 72 * u, w = 560 * u, h = 38 * u, y = 224 * u;
        for (int i = 0; i < rows.Count; i++)
            rows[i].Rect = new RectangleF(x, y + i * h, w, h);
        float by = y + rows.Count * h + 48 * u;
        float bw = (w - 16 * u) / 3;
        buttons[0].Rect = new RectangleF(x, by, w, buttons[0].Height(u));
        float rowY = by + buttons[0].Height(u) + UiButton.Gap * u;
        for (int i = 1; i < 4; i++)
            buttons[i].Rect = new RectangleF(x + (i - 1) * (bw + 8 * u), rowY, bw, buttons[i].Height(u));
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
        DrawRack(g, s);

        float x = 72 * u;
        Ui.Tracked(g, "NEON CUE", Ui.Head(64 * u), Ui.Text, x, 128 * u, 3.2f * u);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, x, 170 * u, 64 * u, 2 * u);
        Ui.Tracked(g, "PROFESSIONAL BILLIARDS", Ui.Head(14 * u), Ui.Text2, x, 194 * u, 2.2f * u);

        var first = rows[0].Rect;
        var card = new RectangleF(first.X - 1, first.Y - 1, first.Width + 2, rows.Count * first.Height + 2);
        Ui.Shadow(g, card, 6 * u, 10 * u);
        Ui.Card(g, card, 6 * u);
        for (int i = 0; i < rows.Count; i++) DrawRow(g, rows[i], i);

        var rules = GameRules.Create(cfg.Mode);
        Ui.Write(g, rules.Tagline, Ui.Body(14 * u), Ui.Text2, new RectangleF(x, card.Bottom + 10 * u, card.Width, 24 * u));

        foreach (var b in buttons) b.Draw(g, u);

        var cue = cfg.Equipped;
        float icon = 15 * u;
        float rx = s.Width - 32 * u;
        string coins = cfg.Coins.ToString("N0");
        var coinFont = Ui.Num(20 * u);
        float coinW = Ui.TrackedWidth(g, coins, coinFont, 0.5f * u);
        rx -= coinW;
        Ui.Tracked(g, coins, coinFont, Ui.Gold, rx, 33 * u, 0.5f * u);
        Glyphs.Draw(g, Glyph.Coin, rx - 8 * u - icon / 2, 33 * u, icon, Ui.Gold);
        rx -= 8 * u + icon + 18 * u;
        Ui.Hairline(g, rx, 22 * u, rx, 44 * u);
        rx -= 18 * u;
        var nameFont = Ui.Head(13 * u);
        float nameW = Ui.TrackedWidth(g, cue.Name.ToUpperInvariant(), nameFont, 1.8f * u);
        rx -= nameW;
        Ui.Tracked(g, cue.Name.ToUpperInvariant(), nameFont, CueArt.TierColor(cue.Tier), rx, 33 * u, 1.8f * u);
        Glyphs.Draw(g, Glyph.Cue, rx - 8 * u - icon / 2, 33 * u, icon, CueArt.TierColor(cue.Tier));

        Ui.Hairline(g, 0, s.Height - 48 * u, s.Width, s.Height - 48 * u);
        Ui.Legend(g, x, s.Height - 24 * u, s.Width - 88 * u, u,
            (Glyph.UpDown, "", "Select"),
            (Glyph.LeftRight, "", "Change"),
            (Glyph.Play, "Enter", "Play"),
            (Glyph.Cue, "C", "Cues"),
            (Glyph.Rack, "F1", "Rules"),
            (Glyph.Corners, "F11", "Screen"));
        Ui.Tracked(g, "V1.12", Ui.Head(11 * u), Ui.Text3, s.Width - 28 * u, s.Height - 24 * u, 1.6f * u, StringAlignment.Far);

        if (showHelp) DrawHelp(g, s);
    }

    /// <summary>A racked set of balls under the lamp on the right — the menu's hero image.</summary>
    void DrawRack(Graphics g, Size s)
    {
        float r = 26 * u, gap = 1.04f;
        float ax = s.Width * 0.70f, ay = s.Height * 0.50f;
        if (ax - 6 * r < 72 * u + 600 * u) return;
        var spots = new List<(int n, PointF p)> { (0, new PointF(ax - 7.5f * r, ay)) };
        int k = 0;
        for (int row = 0; row < 5; row++)
            for (int j = 0; j <= row; j++)
                spots.Add((RackOrder[k++], new PointF(ax + row * 1.732f * r * gap, ay + (j - row / 2f) * 2 * r * gap)));
        using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            foreach (var (_, p) in spots) g.FillEllipse(sh, p.X - r * 0.9f, p.Y - r * 0.55f + r * 0.75f, r * 1.9f, r * 0.9f);
        foreach (var (n, p) in spots) sprites.Draw(g, n, heroOri[n], r, p, 1f);
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
            Glyphs.Draw(g, Glyph.ChevronLeft, vx + 10 * u, cy, 12 * u, ac);
            Glyphs.Draw(g, Glyph.ChevronRight, vx + vw - 10 * u, cy, 12 * u, ac);
        }
    }

    void DrawHelp(Graphics g, Size s)
    {
        Ui.Scrim(g, s, 200);
        var r = new RectangleF(s.Width / 2f - 430 * u, s.Height / 2f - 310 * u, 860 * u, 620 * u);
        Ui.Shadow(g, r, 6 * u, 14 * u);
        Ui.Card(g, r, 6 * u, 250);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, r.X, r.Y, r.Width, 3 * u);
        Ui.Heading(g, "Match guide", r.X + r.Width / 2, r.Y + 42 * u, u, 24);

        float col = (r.Width - 96 * u) / 2;
        float left = r.X + 36 * u, right = left + col + 24 * u;
        Section(g, left, r.Y + 96 * u, "The stroke");
        Section(g, right, r.Y + 96 * u, "The table");
        (string t, string b)[] stroke =
        [
            ("Aim", "The cue points at the pointer."),
            ("Shot", "Draw back and release. Further is harder."),
            ("Fine aim", "Wheel or arrows. Alt is the finer tick."),
            ("Power", "Up and Down set it. Space plays it."),
            ("Spin", "The marker, or W A S D. R centres it."),
        ];
        (string t, string b)[] table =
        [
            ("Ball in hand", "Click to place. Click again to lift."),
            ("Coach", "H requests a shot. G sets the guide."),
            ("Restore", "Backspace returns up to eight shots."),
            ("Practice", "Right-click lifts a ball. Click sets it."),
            ("Session", "M sound. Esc pause. F11 full screen."),
        ];
        float y = r.Y + 122 * u;
        for (int i = 0; i < stroke.Length; i++)
        {
            GuideRow(g, left, y, col, stroke[i].t, stroke[i].b);
            GuideRow(g, right, y, col, table[i].t, table[i].b);
            y += 34 * u;
        }

        y += 8 * u;
        Ui.Hairline(g, left, y, r.Right - 36 * u, y);
        y += 22 * u;
        Section(g, left, y, "Formats");
        y += 26 * u;
        foreach (var m in Modes)
        {
            var rules = GameRules.Create(m);
            Ui.Write(g, rules.Name, Ui.BodyStrong(13.5f * u), Ui.Text, new RectangleF(left, y, 132 * u, 22 * u));
            Ui.Write(g, rules.Tagline, Ui.Body(13 * u), Ui.Text2, new RectangleF(left + 140 * u, y, r.Width - 212 * u, 22 * u));
            y += 26 * u;
        }
        Ui.Write(g, "A standard foul awards ball in hand to the incoming player.", Ui.Body(13 * u), Ui.Warn,
            new RectangleF(left, y + 2 * u, r.Width - 72 * u, 22 * u));
        Ui.Write(g, "Click anywhere, or press Esc, to close", Ui.Body(12.5f * u), Ui.Text3, new RectangleF(r.X, y + 32 * u, r.Width, 20 * u), StringAlignment.Center);
    }

    void Section(Graphics g, float x, float y, string title)
    {
        Ui.Tracked(g, title.ToUpperInvariant(), Ui.Head(11 * u), Ui.Gold, x, y, 1.8f * u);
    }

    void GuideRow(Graphics g, float x, float y, float w, string title, string body)
    {
        Ui.Write(g, title, Ui.BodyStrong(13 * u), Ui.Text, new RectangleF(x, y, 108 * u, 22 * u));
        Ui.Write(g, body, Ui.Body(12.5f * u), Ui.Text2, new RectangleF(x + 112 * u, y, w - 112 * u, 22 * u));
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

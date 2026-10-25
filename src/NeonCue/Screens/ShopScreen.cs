using System.Drawing.Drawing2D;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue.Screens;

/// <summary>Browse, compare, buy and equip cues.</summary>
sealed class ShopScreen : IScreen
{
    readonly IHost host;
    readonly AppSettings cfg;
    readonly List<RectangleF> cards = new();
    readonly UiButton action = new() { Primary = true };
    readonly UiButton back = new() { Text = "BACK", Accent = Theme.Cyan };
    int selected;
    int hover = -1;
    double time, flash;
    string flashText = "";
    Color flashColor;
    float u = 1;

    public ShopScreen(IHost host)
    {
        this.host = host;
        cfg = host.Settings;
        selected = Math.Max(0, CueCatalog.All.ToList().FindIndex(c => c.Id == cfg.Equipped.Id));
        back.OnClick = host.ShowMenu;
        action.OnClick = Act;
    }

    CueSpec Selected => CueCatalog.All[selected];
    CueSpec Pick => CueCatalog.CoachPick(cfg.Mode, cfg.Coins, cfg.OwnedCues);

    void Act()
    {
        var c = Selected;
        if (cfg.Owns(c))
        {
            if (cfg.EquippedCue == c.Id) return;
            cfg.EquippedCue = c.Id;
            cfg.Save();
            Flash($"{c.Name} equipped", Theme.Cyan);
            host.Sound.Click();
        }
        else if (cfg.TryBuy(c))
        {
            Flash($"{c.Name} is yours!", CueArt.TierColor(c.Tier));
            host.Sound.Win();
        }
        else Flash($"You need {c.Price - cfg.Coins:N0} more coins", Theme.Red);
    }

    void Flash(string text, Color color)
    {
        flashText = text;
        flashColor = color;
        flash = 2.2;
    }

    public void Update(double dt)
    {
        time += dt;
        flash = Math.Max(0, flash - dt);
    }

    public void Draw(Graphics g, Size s)
    {
        u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.75f, 2.2f);
        Theme.Setup(g);
        var full = new Rectangle(Point.Empty, s);
        using (var bg = new LinearGradientBrush(full, Theme.BgTop, Theme.Bg, 90f)) g.FillRectangle(bg, full);
        using (var grid = new Pen(Color.FromArgb(12, 140, 120, 255)))
        {
            for (int x = 0; x < s.Width; x += 48) g.DrawLine(grid, x, 0, x, s.Height);
            for (int y = 0; y < s.Height; y += 48) g.DrawLine(grid, 0, y, s.Width, y);
        }

        Gfx.GlowText(g, "CUE SHOP", Theme.F(44 * u, FontStyle.Bold), Theme.Magenta, Color.White, new PointF(40 * u, 52 * u), StringAlignment.Near, spread: 4 * u);
        Gfx.Text(g, $"COINS  {cfg.Coins:N0}", Theme.F(24 * u, FontStyle.Bold), Theme.Amber, new RectangleF(s.Width - 440 * u, 30 * u, 400 * u, 44 * u), StringAlignment.Far);

        var pick = Pick;
        cards.Clear();
        float cw = 300 * u, ch = 118 * u, gap = 14 * u, x0 = 40 * u, y0 = 104 * u;
        for (int i = 0; i < CueCatalog.All.Count; i++)
        {
            var r = new RectangleF(x0 + (i % 2) * (cw + gap), y0 + (i / 2) * (ch + gap), cw, ch);
            cards.Add(r);
            DrawCard(g, CueCatalog.All[i], r, i == selected, i == hover, CueCatalog.All[i] == pick);
        }

        var detail = new RectangleF(x0 + 2 * cw + gap + 26 * u, y0, s.Width - (x0 + 2 * cw + gap + 26 * u) - 40 * u, 4 * ch + 3 * gap);
        DrawDetail(g, detail, pick);

        back.Rect = new RectangleF(40 * u, s.Height - 74 * u, 160 * u, 46 * u);
        back.Draw(g, u);
        Gfx.Text(g, "Earn coins by potting balls and winning racks against the CPU — tougher CPUs pay more.",
            Theme.F(14 * u), Theme.TextDim, new RectangleF(220 * u, s.Height - 74 * u, s.Width - 260 * u, 46 * u));

        if (flash > 0)
        {
            int a = (int)(255 * Math.Min(1, flash / 0.4));
            Gfx.GlowText(g, flashText, Theme.F(28 * u, FontStyle.Bold), Theme.A(flashColor, a), Theme.A(Color.White, a),
                new PointF(s.Width / 2f, 64 * u), spread: 3 * u, alpha: (int)(34 * a / 255.0));
        }
    }

    void DrawCard(Graphics g, CueSpec c, RectangleF r, bool sel, bool hov, bool isPick)
    {
        Color tier = CueArt.TierColor(c.Tier);
        using var path = Gfx.RoundRect(r, 14 * u);
        using (var br = new SolidBrush(Theme.A(sel || hov ? Theme.PanelLight : Theme.Panel, 235))) g.FillPath(br, path);
        if (sel) Gfx.GlowPath(g, path, tier, 1.6f * u, 3 * u, 4, 40);
        else using (var pen = new Pen(Theme.A(tier, hov ? 160 : 80), 1.2f * u)) g.DrawPath(pen, path);

        Gfx.Text(g, c.Name, Theme.F(18 * u, FontStyle.Bold), Theme.Text, new RectangleF(r.X + 16 * u, r.Y + 10 * u, r.Width - 32 * u, 26 * u));
        Gfx.Text(g, c.Tier.ToString().ToUpperInvariant(), Theme.F(11 * u, FontStyle.Bold), tier, new RectangleF(r.X + 16 * u, r.Y + 36 * u, 120 * u, 18 * u));

        string price = cfg.EquippedCue == c.Id ? "EQUIPPED" : cfg.Owns(c) ? "OWNED" : $"{c.Price:N0}";
        Color pc = cfg.EquippedCue == c.Id ? Theme.Lime : cfg.Owns(c) ? Theme.Cyan : cfg.Coins >= c.Price ? Theme.Amber : Theme.A(Theme.Amber, 120);
        Gfx.Text(g, price, Theme.F(14 * u, FontStyle.Bold), pc, new RectangleF(r.Right - 136 * u, r.Y + 34 * u, 120 * u, 22 * u), StringAlignment.Far);
        if (isPick)
            Gfx.Text(g, "★ COACH PICK", Theme.F(11 * u, FontStyle.Bold), Theme.Lime, new RectangleF(r.Right - 136 * u, r.Y + 12 * u, 120 * u, 18 * u), StringAlignment.Far);

        var tip = new PointF(r.Right - 18 * u, r.Bottom - 30 * u);
        var butt = new PointF(r.X + 18 * u, r.Bottom - 30 * u);
        CueArt.Draw(g, tip, butt, 2.2f * u, 6f * u, c, time, u * 0.6f, shadow: false);
        Gfx.Text(g, $"OVR {c.Overall}", Theme.F(11 * u, FontStyle.Bold), Theme.TextDim, new RectangleF(r.X + 16 * u, r.Bottom - 20 * u, 100 * u, 16 * u));
    }

    void DrawDetail(Graphics g, RectangleF r, CueSpec pick)
    {
        var c = Selected;
        var eq = cfg.Equipped;
        Color tier = CueArt.TierColor(c.Tier);
        using (var path = Gfx.RoundRect(r, 18 * u))
        {
            using var br = new SolidBrush(Theme.A(Theme.Panel, 240));
            g.FillPath(br, path);
            Gfx.GlowPath(g, path, tier, 1.4f * u, 3 * u, 4, 26);
        }

        float x = r.X + 28 * u, w = r.Width - 56 * u, y = r.Y + 20 * u;
        Gfx.GlowText(g, c.Name, Theme.F(32 * u, FontStyle.Bold), tier, Color.White, new PointF(x, y + 20 * u), StringAlignment.Near, spread: 3 * u, layers: 3);
        y += 46 * u;
        Gfx.Text(g, $"{c.Tier.ToString().ToUpperInvariant()}  •  by {c.Maker}", Theme.F(14 * u, FontStyle.Bold), tier, new RectangleF(x, y, w, 22 * u));
        y += 26 * u;
        Gfx.WrappedText(g, c.Story, Theme.F(14 * u), Theme.TextDim, new RectangleF(x, y, w, 40 * u));
        y += 46 * u;

        CueArt.Draw(g, new PointF(x + w, y + 14 * u), new PointF(x, y + 14 * u), 3f * u, 9f * u, c, time, u);
        y += 46 * u;

        (string label, int v, int cur)[] stats =
        [
            ("POWER", c.PowerRating, eq.PowerRating),
            ("SPIN", c.SpinRating, eq.SpinRating),
            ("ACCURACY", c.AccuracyRating, eq.AccuracyRating),
            ("CONTROL", c.ControlRating, eq.ControlRating),
            ("AIM VISION", c.VisionRating, eq.VisionRating),
        ];
        foreach (var (label, v, cur) in stats)
        {
            Gfx.Text(g, label, Theme.F(13 * u, FontStyle.Bold), Theme.TextDim, new RectangleF(x, y, 120 * u, 20 * u));
            var bar = new RectangleF(x + 124 * u, y + 5 * u, w - 190 * u, 10 * u);
            using (var bp = Gfx.RoundRect(bar, 5 * u)) using (var bb = new SolidBrush(Theme.A(Theme.PanelLight, 255))) g.FillPath(bb, bp);
            if (cur > 0)
            {
                var cb = new RectangleF(bar.X, bar.Y, bar.Width * cur / 100f, bar.Height);
                using var cp = Gfx.RoundRect(cb, 5 * u);
                using var cbr = new SolidBrush(Theme.A(Color.White, 40));
                g.FillPath(cbr, cp);
            }
            if (v > 0)
            {
                var fb = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * v / 100f), bar.Height);
                using var fp = Gfx.RoundRect(fb, 5 * u);
                using var fbr = new LinearGradientBrush(bar, Theme.Cyan, tier, 0f);
                g.FillPath(fbr, fp);
            }
            int delta = v - cur;
            string dt = c.Id == eq.Id ? $"{v}" : delta == 0 ? $"{v}" : $"{v} ({(delta > 0 ? "+" : "")}{delta})";
            Gfx.Text(g, dt, Theme.F(12 * u, FontStyle.Bold), delta > 0 && c.Id != eq.Id ? Theme.Lime : delta < 0 ? Theme.Red : Theme.Text,
                new RectangleF(bar.Right + 6 * u, y, 70 * u, 20 * u));
            y += 26 * u;
        }

        // Real numbers behind the bars.
        string facts = $"Speed ×{c.Power:0.00}  •  Spin ×{c.Spin:0.00}  •  Wobble ±{c.AimError:0.00}°  •  Deflection {c.Squirt:0.0}°";
        Gfx.Text(g, facts, Theme.F(12 * u), Theme.A(Theme.Text, 190), new RectangleF(x, y + 2 * u, w, 18 * u));
        y += 26 * u;

        var perks = new List<string>();
        if (c.Has(CuePerk.BreakBooster)) perks.Add($"BREAK BOOST +{(c.BreakBoost - 1) * 100:0}%");
        if (c.Has(CuePerk.LowDeflection)) perks.Add("LOW DEFLECTION");
        if (c.Has(CuePerk.EagleEye)) perks.Add("EAGLE EYE (bank lines)");
        float px = x;
        foreach (var p in perks)
        {
            var sz = g.MeasureString(p, Theme.F(12 * u, FontStyle.Bold));
            var chip = new RectangleF(px, y, sz.Width + 18 * u, 24 * u);
            using var cp = Gfx.RoundRect(chip, 12 * u);
            using (var cb = new SolidBrush(Theme.A(tier, 40))) g.FillPath(cb, cp);
            using (var pen = new Pen(Theme.A(tier, 180), 1f * u)) g.DrawPath(pen, cp);
            Gfx.Text(g, p, Theme.F(12 * u, FontStyle.Bold), Color.White, chip, StringAlignment.Center);
            px += chip.Width + 8 * u;
        }
        if (perks.Count == 0) Gfx.Text(g, "No special perks", Theme.F(12 * u), Theme.TextDim, new RectangleF(x, y, w, 24 * u));
        y += 34 * u;

        string modeName = GameRules.Create(cfg.Mode).Name;
        string advice = c == pick
            ? $"★ Coach: the best cue you can get for {modeName}. Great for {CueCatalog.BestFor(c)}."
            : $"Best for {CueCatalog.BestFor(c)}.  Coach pick for {modeName}: {pick.Name}.";
        Gfx.Text(g, advice, Theme.F(13 * u, FontStyle.Bold), c == pick ? Theme.Lime : Theme.Cyan, new RectangleF(x, y, w, 20 * u));

        bool owns = cfg.Owns(c);
        action.Text = cfg.EquippedCue == c.Id ? "EQUIPPED" : owns ? "EQUIP" : cfg.Coins >= c.Price ? $"BUY  •  {c.Price:N0}" : $"NEED {c.Price - cfg.Coins:N0} MORE";
        action.Rect = new RectangleF(r.Right - 28 * u - 280 * u, r.Bottom - 74 * u, 280 * u, 52 * u);
        action.Draw(g, u);
    }

    public void MouseMove(MouseEventArgs e)
    {
        hover = cards.FindIndex(r => r.Contains(e.Location));
        action.Hover = action.Contains(e.Location);
        back.Hover = back.Contains(e.Location);
    }

    public void MouseDown(MouseEventArgs e)
    {
        int i = cards.FindIndex(r => r.Contains(e.Location));
        if (i >= 0)
        {
            selected = i;
            host.Sound.CuePreview(Selected.Id);
            if (e.Clicks >= 2 && cfg.Owns(Selected)) Act();
            return;
        }
        if (action.Contains(e.Location)) { action.OnClick(); return; }
        if (back.Contains(e.Location)) back.OnClick();
    }

    public bool KeyDown(Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        int n = CueCatalog.All.Count;
        switch (k)
        {
            case Keys.Escape: host.ShowMenu(); return true;
            case Keys.Left: selected = (selected + n - 1) % n; host.Sound.CuePreview(Selected.Id); return true;
            case Keys.Right: selected = (selected + 1) % n; host.Sound.CuePreview(Selected.Id); return true;
            case Keys.Up: selected = (selected + n - 2) % n; host.Sound.CuePreview(Selected.Id); return true;
            case Keys.Down: selected = (selected + 2) % n; host.Sound.CuePreview(Selected.Id); return true;
            case Keys.Enter: case Keys.Space: Act(); return true;
        }
        return false;
    }
}

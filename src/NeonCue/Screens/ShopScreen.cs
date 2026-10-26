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
    readonly UiButton back = new() { Text = "BACK" };
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
            Flash($"{c.Name} equipped", Ui.Gold);
            host.Sound.Click();
        }
        else if (cfg.TryBuy(c))
        {
            Flash($"{c.Name} is yours!", CueArt.TierColor(c.Tier));
            host.Sound.Win();
        }
        else Flash($"You need {c.Price - cfg.Coins:N0} more coins", Ui.Bad);
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
        using (var bg = new LinearGradientBrush(full, Theme.Hex("#0E1114"), Ui.Bg, 90f)) g.FillRectangle(bg, full);

        Ui.Tracked(g, "CUE SHOP", Ui.Head(34 * u), Ui.Text, 40 * u, 46 * u, 5 * u);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, 40 * u, 70 * u, 56 * u, 2 * u);
        Ui.Tracked(g, "COINS", Ui.Head(12 * u), Ui.Text3, s.Width - 230 * u, 46 * u, 2.5f * u);
        Ui.Write(g, cfg.Coins.ToString("N0"), Ui.Num(26 * u), Ui.Gold, new RectangleF(s.Width - 200 * u, 28 * u, 160 * u, 36 * u), StringAlignment.Far);
        Ui.Hairline(g, 0, 88 * u, s.Width, 88 * u);

        var pick = Pick;
        cards.Clear();
        float cw = 300 * u, ch = 118 * u, gap = 12 * u, x0 = 40 * u, y0 = 108 * u;
        for (int i = 0; i < CueCatalog.All.Count; i++)
        {
            var r = new RectangleF(x0 + (i % 2) * (cw + gap), y0 + (i / 2) * (ch + gap), cw, ch);
            cards.Add(r);
            DrawCard(g, CueCatalog.All[i], r, i == selected, i == hover, CueCatalog.All[i] == pick);
        }

        var detail = new RectangleF(x0 + 2 * cw + gap + 24 * u, y0, s.Width - (x0 + 2 * cw + gap + 24 * u) - 40 * u, 4 * ch + 3 * gap);
        DrawDetail(g, detail, pick);

        back.Rect = new RectangleF(40 * u, s.Height - 70 * u, 150 * u, 42 * u);
        back.Draw(g, u);
        Ui.Write(g, "Earn coins by potting balls and winning racks against the CPU — tougher CPUs pay more.",
            Ui.Body(13 * u), Ui.Text3, new RectangleF(210 * u, s.Height - 70 * u, s.Width - 250 * u, 42 * u));

        if (flash > 0)
        {
            int a = (int)(255 * Math.Min(1, flash / 0.4));
            var f = Ui.BodyStrong(16 * u);
            float tw = g.MeasureString(flashText, f).Width + 40 * u;
            var plate = new RectangleF(s.Width / 2f - tw / 2, 30 * u, tw, 40 * u);
            using (var pb = new SolidBrush(Color.FromArgb(a * 240 / 255, Ui.Panel2))) g.FillRectangle(pb, plate);
            using (var eb = new SolidBrush(Color.FromArgb(a, flashColor))) g.FillRectangle(eb, plate.X, plate.Y, 3 * u, plate.Height);
            Ui.Write(g, flashText, f, Color.FromArgb(a, Ui.Text), plate, StringAlignment.Center);
        }
    }

    void DrawCard(Graphics g, CueSpec c, RectangleF r, bool sel, bool hov, bool isPick)
    {
        Color tier = CueArt.TierColor(c.Tier);
        Ui.Card(g, r, 5 * u, sel || hov ? 250 : 220, true, sel ? Ui.Gold : hov ? Ui.LineStrong : Ui.Line);
        if (sel)
        {
            using var pen = new Pen(Ui.Gold, 1.5f * u);
            using var path = Gfx.RoundRect(r, 5 * u);
            g.DrawPath(pen, path);
        }

        Ui.Write(g, c.Name, Ui.BodyStrong(16 * u), Ui.Text, new RectangleF(r.X + 16 * u, r.Y + 10 * u, r.Width - 140 * u, 26 * u));
        Ui.Tracked(g, c.Tier.ToString().ToUpperInvariant(), Ui.Head(10.5f * u), tier, r.X + 16 * u, r.Y + 45 * u, 2 * u);

        bool equipped = cfg.EquippedCue == c.Id, owned = cfg.Owns(c);
        string price = equipped ? "EQUIPPED" : owned ? "OWNED" : $"{c.Price:N0}";
        Color pc = equipped ? Ui.Good : owned ? Ui.Text2 : cfg.Coins >= c.Price ? Ui.Gold : Color.FromArgb(130, Ui.Gold);
        if (equipped || owned) Ui.Tracked(g, price, Ui.Head(11 * u), pc, r.Right - 16 * u, r.Y + 45 * u, 1.8f * u, StringAlignment.Far);
        else Ui.Write(g, price, Ui.Num(16 * u), pc, new RectangleF(r.Right - 136 * u, r.Y + 34 * u, 120 * u, 22 * u), StringAlignment.Far);
        if (isPick)
            Ui.Tracked(g, "COACH PICK", Ui.Head(10 * u), Ui.Good, r.Right - 16 * u, r.Y + 22 * u, 1.8f * u, StringAlignment.Far);

        var tip = new PointF(r.Right - 18 * u, r.Bottom - 30 * u);
        var butt = new PointF(r.X + 18 * u, r.Bottom - 30 * u);
        CueArt.Draw(g, tip, butt, 2.2f * u, 6f * u, c, time, u * 0.6f, shadow: false);
        Ui.Tracked(g, $"OVR {c.Overall}", Ui.Head(10 * u), Ui.Text3, r.X + 16 * u, r.Bottom - 12 * u, 1.5f * u);
    }

    void DrawDetail(Graphics g, RectangleF r, CueSpec pick)
    {
        var c = Selected;
        var eq = cfg.Equipped;
        Color tier = CueArt.TierColor(c.Tier);
        Ui.Shadow(g, r, 6 * u, 10 * u);
        Ui.Card(g, r, 6 * u, 245);
        using (var tb = new SolidBrush(tier)) g.FillRectangle(tb, r.X, r.Y, r.Width, 3 * u);

        float x = r.X + 28 * u, w = r.Width - 56 * u, y = r.Y + 22 * u;
        Ui.Write(g, c.Name, Ui.Head(30 * u), Ui.Text, new RectangleF(x, y, w, 40 * u));
        y += 44 * u;
        Ui.Tracked(g, $"{c.Tier.ToString().ToUpperInvariant()}   ·   {c.Maker.ToUpperInvariant()}", Ui.Head(12 * u), tier, x, y + 10 * u, 2.2f * u);
        y += 26 * u;
        Gfx.WrappedText(g, c.Story, Ui.Body(14 * u), Ui.Text2, new RectangleF(x, y, w, 40 * u));
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
            Ui.Tracked(g, label, Ui.Head(11 * u), Ui.Text3, x, y + 10 * u, 2 * u);
            var bar = new RectangleF(x + 124 * u, y + 8 * u, w - 200 * u, 4 * u);
            using (var bb = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillRectangle(bb, bar);
            if (v > 0)
            {
                var fb = new RectangleF(bar.X, bar.Y, bar.Width * v / 100f, bar.Height);
                using var fbr = new LinearGradientBrush(bar, Ui.Gold, Ui.GoldBright, 0f);
                g.FillRectangle(fbr, fb);
            }
            if (cur > 0 && c.Id != eq.Id)
            {
                // Marker for the cue you have now.
                float mx = bar.X + bar.Width * cur / 100f;
                using var mp = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f * u);
                g.DrawLine(mp, mx, bar.Y - 4 * u, mx, bar.Bottom + 4 * u);
            }
            int delta = v - cur;
            Ui.Write(g, v.ToString(), Ui.Num(15 * u), Ui.Text, new RectangleF(bar.Right + 10 * u, y, 34 * u, 20 * u));
            if (c.Id != eq.Id && delta != 0)
                Ui.Write(g, $"{(delta > 0 ? "+" : "")}{delta}", Ui.Num(12 * u), delta > 0 ? Ui.Good : Ui.Bad, new RectangleF(bar.Right + 42 * u, y, 40 * u, 20 * u));
            y += 26 * u;
        }

        // Real numbers behind the bars.
        string facts = $"Speed ×{c.Power:0.00}   ·   Spin ×{c.Spin:0.00}   ·   Wobble ±{c.AimError:0.00}°   ·   Deflection {c.Squirt:0.0}°";
        Ui.Write(g, facts, Ui.Body(12 * u), Ui.Text3, new RectangleF(x, y + 2 * u, w, 18 * u));
        y += 28 * u;

        var perks = new List<string>();
        if (c.Has(CuePerk.BreakBooster)) perks.Add($"BREAK BOOST +{(c.BreakBoost - 1) * 100:0}%");
        if (c.Has(CuePerk.LowDeflection)) perks.Add("LOW DEFLECTION");
        if (c.Has(CuePerk.EagleEye)) perks.Add("EAGLE EYE");
        float px = x;
        var pf = Ui.Head(11 * u);
        foreach (var p in perks)
        {
            float tw = Ui.TrackedWidth(g, p, pf, 1.6f * u);
            var chip = new RectangleF(px, y, tw + 22 * u, 24 * u);
            using (var pen = new Pen(Ui.LineStrong, 1f)) g.DrawRectangle(pen, chip.X, chip.Y, chip.Width, chip.Height);
            Ui.Tracked(g, p, pf, Ui.Text, chip.X + 11 * u, chip.Y + chip.Height / 2, 1.6f * u);
            px += chip.Width + 8 * u;
        }
        if (perks.Count == 0) Ui.Write(g, "No special perks", Ui.Body(12 * u), Ui.Text3, new RectangleF(x, y, w, 24 * u));
        y += 36 * u;

        string modeName = GameRules.Create(cfg.Mode).Name;
        string advice = c == pick
            ? $"Coach: the best cue you can get for {modeName}. Great for {CueCatalog.BestFor(c)}."
            : $"Best for {CueCatalog.BestFor(c)}.  Coach pick for {modeName}: {pick.Name}.";
        Ui.Write(g, advice, Ui.BodyStrong(13 * u), c == pick ? Ui.Good : Ui.Text2, new RectangleF(x, y, w, 20 * u));

        bool owns = cfg.Owns(c);
        action.Text = cfg.EquippedCue == c.Id ? "EQUIPPED" : owns ? "EQUIP" : cfg.Coins >= c.Price ? $"BUY  ·  {c.Price:N0}" : $"NEED {c.Price - cfg.Coins:N0} MORE";
        action.Rect = new RectangleF(r.Right - 28 * u - 280 * u, r.Bottom - 74 * u, 280 * u, 50 * u);
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

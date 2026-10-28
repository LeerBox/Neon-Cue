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
    readonly UiButton dress = new() { Text = "CUSTOMIZE" };
    readonly UiButton back = new() { Text = "MENU" };
    int selected;
    int hover = -1;
    double time, flash;
    string flashText = "";
    Color flashColor;
    float u = 1;
    float scroll, maxScroll, listTop, listBottom;

    public ShopScreen(IHost host)
    {
        this.host = host;
        cfg = host.Settings;
        selected = Math.Max(0, CueCatalog.All.ToList().FindIndex(c => c.Id == cfg.Equipped.Id));
        back.OnClick = host.ShowMenu;
        action.OnClick = Act;
        dress.OnClick = () => { if (cfg.Owns(Selected)) host.ShowDress(Selected.Id); };
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
            Flash($"{c.Name} is now equipped.", Ui.Gold);
            host.Sound.CueEquip(c.Id);
        }
        else if (cfg.TryBuy(c))
        {
            Flash($"{c.Name} added to your collection.", CueArt.TierColor(c.Tier));
            host.Sound.Win();
        }
        else Flash($"Balance short by {c.Price - cfg.Coins:N0} coins.", Ui.Bad);
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

        Ui.Tracked(g, "CUE ROOM", Ui.Head(34 * u), Ui.Text, 40 * u, 46 * u, 2.4f * u);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, 40 * u, 70 * u, 56 * u, 2 * u);
        string coins = cfg.Coins.ToString("N0");
        var coinFont = Ui.Num(26 * u);
        float coinW = g.MeasureString(coins, coinFont).Width;
        float coinRight = s.Width - 36 * u;
        Ui.Write(g, coins, coinFont, Ui.Gold, new RectangleF(coinRight - coinW - 4, 28 * u, coinW + 8, 36 * u), StringAlignment.Far);
        Glyphs.Draw(g, Glyph.Coin, coinRight - coinW - 18 * u, 46 * u, 16 * u, Ui.Gold);
        Ui.Hairline(g, 0, 88 * u, s.Width, 88 * u);

        var pick = Pick;
        cards.Clear();
        float cw = 300 * u, ch = 112 * u, gap = 10 * u, x0 = 40 * u, y0 = 108 * u;
        listTop = y0;
        listBottom = s.Height - 96 * u;
        int rows = (CueCatalog.All.Count + 1) / 2;
        float contentH = rows * (ch + gap) - gap;
        maxScroll = Math.Max(0, contentH - (listBottom - listTop));
        scroll = Math.Clamp(scroll, 0, maxScroll);
        var clip = g.Save();
        g.SetClip(new RectangleF(0, y0, x0 + 2 * (cw + gap), listBottom - y0));
        for (int i = 0; i < CueCatalog.All.Count; i++)
        {
            var r = new RectangleF(x0 + (i % 2) * (cw + gap), y0 + (i / 2) * (ch + gap) - scroll, cw, ch);
            cards.Add(r);
            if (r.Bottom < y0 || r.Y > listBottom) continue;
            DrawCard(g, CueCatalog.All[i], r, i == selected, i == hover, CueCatalog.All[i] == pick);
        }
        g.Restore(clip);

        var detail = new RectangleF(x0 + 2 * cw + gap + 24 * u, y0, s.Width - (x0 + 2 * cw + gap + 24 * u) - 40 * u, listBottom - y0);
        DrawDetail(g, detail, pick);

        float bh = back.Height(u);
        back.Rect = new RectangleF(40 * u, s.Height - 28 * u - bh, 160 * u, bh);
        back.Draw(g, u);
        float noteY = back.Rect.Y + bh / 2f;
        Glyphs.Draw(g, Glyph.Coin, 228 * u, noteY, 16 * u, Ui.Gold);
        Ui.Write(g, "Match purse", Ui.BodyStrong(13 * u), Ui.Text, new RectangleF(246 * u, noteY - 18 * u, 240 * u, 18 * u));
        Ui.Write(g, "Awarded for balls pocketed and racks won. Higher skill pays more.",
            Ui.Body(12.5f * u), Ui.Text3, new RectangleF(246 * u, noteY, s.Width - 290 * u, 18 * u));

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

        float nameW = isPick ? r.Width - 148 * u : r.Width - 32 * u;
        Ui.Write(g, c.Name, Ui.BodyStrong(15.5f * u), Ui.Text, new RectangleF(r.X + 16 * u, r.Y + 10 * u, nameW, 24 * u));
        Ui.Tracked(g, c.Tier.ToString().ToUpperInvariant(), Ui.Head(10.5f * u), tier, r.X + 16 * u, r.Y + 45 * u, 2 * u);

        bool equipped = cfg.EquippedCue == c.Id, owned = cfg.Owns(c);
        string price = equipped ? "EQUIPPED" : owned ? "OWNED" : $"{c.Price:N0}";
        Color pc = equipped ? Ui.Good : owned ? Ui.Text2 : cfg.Coins >= c.Price ? Ui.Gold : Color.FromArgb(130, Ui.Gold);
        if (equipped || owned) Ui.Tracked(g, price, Ui.Head(11 * u), pc, r.Right - 16 * u, r.Y + 45 * u, 1.8f * u, StringAlignment.Far);
        else Ui.Write(g, price, Ui.Num(16 * u), pc, new RectangleF(r.Right - 136 * u, r.Y + 34 * u, 120 * u, 22 * u), StringAlignment.Far);
        if (isPick)
            Ui.Tracked(g, "RECOMMENDED", Ui.Head(10 * u), Ui.Good, r.Right - 16 * u, r.Y + 22 * u, 1.8f * u, StringAlignment.Far);

        var tip = new PointF(r.Right - 18 * u, r.Bottom - 30 * u);
        var butt = new PointF(r.X + 18 * u, r.Bottom - 30 * u);
        var look = cfg.Owns(c) ? cfg.DressFor(c.Id) : null;
        CueArt.Draw(g, tip, butt, 2.2f * u, 6f * u, c, time, u * 0.6f, shadow: false, dress: look is { IsBlank: false } ? look : null);
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

        var look = cfg.Owns(c) ? cfg.DressFor(c.Id) : null;
        CueArt.Draw(g, new PointF(x + w, y + 14 * u), new PointF(x, y + 14 * u), 3f * u, 9f * u, c, time, u, dress: look is { IsBlank: false } ? look : null);
        y += 46 * u;

        (string label, int v, int cur)[] stats =
        [
            ("POWER", c.PowerRating, eq.PowerRating),
            ("SPIN", c.SpinRating, eq.SpinRating),
            ("ACCURACY", c.AccuracyRating, eq.AccuracyRating),
            ("CONTROL", c.ControlRating, eq.ControlRating),
            ("VISION", c.VisionRating, eq.VisionRating),
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
        string facts = $"Speed ×{c.Power:0.00}   ·   Spin ×{c.Spin:0.00}   ·   Stroke error ±{c.AimError:0.00}°   ·   Deflection {c.Squirt:0.0}°";
        Ui.Write(g, facts, Ui.Body(12 * u), Ui.Text3, new RectangleF(x, y + 2 * u, w, 18 * u));
        y += 22 * u;
        Ui.Write(g, c.Trait, Ui.BodyStrong(13 * u), Ui.Text, new RectangleF(x, y, w, 36 * u));
        y += 34 * u;
        Ui.Write(g, $"{c.DrawName}   ·   {c.Tone}", Ui.Body(12.5f * u), tier, new RectangleF(x, y, w, 18 * u));
        y += 24 * u;

        var perks = new List<string>();
        if (c.Has(CuePerk.BreakBooster)) perks.Add($"BREAK +{(c.BreakBoost - 1) * 100:0}%");
        if (c.Has(CuePerk.LowDeflection)) perks.Add("LOW DEFLECTION");
        if (c.Has(CuePerk.EagleEye)) perks.Add("EXTENDED GUIDE");
        if (c.Has(CuePerk.FeatherTouch)) perks.Add("FEATHER TOUCH");
        perks.Add(c.DrawName.ToUpperInvariant());
        float px = x;
        var pf = Ui.Head(11 * u);
        foreach (var p in perks)
        {
            float track = pf.Size * 0.12f;
            float tw = Ui.TrackedWidth(g, p, pf, track);
            var chip = new RectangleF(px, y, tw + pf.Size * 1.6f, 26 * u);
            using (var pen = new Pen(Ui.LineStrong, 1f)) g.DrawRectangle(pen, chip.X, chip.Y, chip.Width, chip.Height);
            Ui.PlaceCentered(g, p, pf, Ui.Text, chip, track);
            px += chip.Width + 8 * u;
        }
        if (perks.Count == 0) Ui.Write(g, "Standard specification", Ui.Body(12 * u), Ui.Text3, new RectangleF(x, y, w, 24 * u));
        y += 36 * u;

        string modeName = GameRules.Create(cfg.Mode).Name;
        string advice = c == pick
            ? $"Recommended for {modeName}. Specified for {CueCatalog.BestFor(c)}."
            : $"Specified for {CueCatalog.BestFor(c)}. Recommended for {modeName}: {pick.Name}.";
        Ui.Write(g, advice, Ui.BodyStrong(13 * u), c == pick ? Ui.Good : Ui.Text2, new RectangleF(x, y, w, 20 * u));

        bool owns = cfg.Owns(c);
        action.Text = cfg.EquippedCue == c.Id ? "EQUIPPED" : owns ? "EQUIP" : cfg.Coins >= c.Price ? $"ACQUIRE  ·  {c.Price:N0}" : $"SHORT {c.Price - cfg.Coins:N0}";
        float ah = action.Height(u);
        action.Rect = new RectangleF(r.Right - 28 * u - 280 * u, r.Bottom - 24 * u - ah, 280 * u, ah);
        action.Draw(g, u);
        if (owns)
        {
            dress.Rect = new RectangleF(action.Rect.X - 168 * u, action.Rect.Y, 156 * u, ah);
            dress.Draw(g, u);
        }
    }

    public void MouseMove(MouseEventArgs e)
    {
        hover = cards.FindIndex(r => r.Contains(e.Location));
        action.Hover = action.Contains(e.Location);
        dress.Hover = dress.Contains(e.Location);
        back.Hover = back.Contains(e.Location);
    }

    public void MouseWheel(MouseEventArgs e)
    {
        scroll = Math.Clamp(scroll - Math.Sign(e.Delta) * 56 * u, 0, maxScroll);
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
        if (cfg.Owns(Selected) && dress.Contains(e.Location)) { dress.OnClick(); return; }
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
            case Keys.Left: selected = (selected + n - 1) % n; Reveal(); return true;
            case Keys.Right: selected = (selected + 1) % n; Reveal(); return true;
            case Keys.Up: selected = (selected + n - 2) % n; Reveal(); return true;
            case Keys.Down: selected = (selected + 2) % n; Reveal(); return true;
            case Keys.Enter: case Keys.Space: Act(); return true;
        }
        return false;
    }

    void Reveal()
    {
        host.Sound.CuePreview(Selected.Id);
        if (cards.Count != CueCatalog.All.Count || listBottom <= listTop) return;
        var r = cards[selected];
        if (r.Y < listTop) scroll = Math.Max(0, scroll - (listTop - r.Y));
        else if (r.Bottom > listBottom) scroll += r.Bottom - listBottom;
    }
}

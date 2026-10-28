using System.Drawing.Drawing2D;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue.Screens;

/// <summary>
/// Cosmetic room for one owned cue. Marks, wrap, trim, finish, a butt ornament, and a short
/// signature. None of it changes the stroke.
/// </summary>
sealed class DressScreen : IScreen
{
    readonly IHost host;
    readonly AppSettings cfg;
    readonly CueSpec cue;
    readonly CueDress look;
    readonly UiButton done = new() { Text = "DONE", Primary = true };
    readonly UiButton reset = new() { Text = "RESET" };
    readonly List<(RectangleF rect, Action act)> hits = new();
    string zone = "forearm";
    float angle = -8f;
    bool turning;
    Point turnAt;
    float u = 1;
    double time;
    string note = "";
    double noteT;

    public DressScreen(IHost host, string cueId)
    {
        this.host = host;
        cfg = host.Settings;
        cue = CueCatalog.Get(cueId);
        look = cfg.Owns(cue) ? cfg.DressFor(cue.Id).Clone() : new CueDress();
        done.OnClick = host.ShowShop;
        reset.OnClick = () => { Clear(); Say("Restored to the cue's own design."); };
    }

    public void Update(double dt) { time += dt; noteT = Math.Max(0, noteT - dt); }

    public void Draw(Graphics g, Size s)
    {
        u = Math.Clamp(Math.Min(s.Width / 1280f, s.Height / 800f), 0.75f, 2.2f);
        Theme.Setup(g);
        var full = new Rectangle(Point.Empty, s);
        using (var bg = new LinearGradientBrush(full, Theme.Hex("#0E1114"), Ui.Bg, 90f)) g.FillRectangle(bg, full);
        hits.Clear();

        Ui.Tracked(g, "CUE ATELIER", Ui.Head(28 * u), Ui.Text, 40 * u, 42 * u, 2.2f * u);
        using (var gb = new SolidBrush(Ui.Gold)) g.FillRectangle(gb, 40 * u, 64 * u, 48 * u, 2 * u);
        Ui.Write(g, "Cosmetic only. The stroke does not change.", Ui.Body(13 * u), Ui.Text3, new RectangleF(40 * u, 74 * u, 520 * u, 20 * u));

        var stage = new RectangleF(36 * u, 108 * u, s.Width * 0.46f, s.Height - 196 * u);
        Ui.Card(g, stage, 6 * u, 240);
        Ui.Write(g, cue.Name, Ui.Head(26 * u), Ui.Text, new RectangleF(stage.X + 22 * u, stage.Y + 16 * u, stage.Width - 44 * u, 34 * u));
        Ui.Tracked(g, cue.Tier.ToString().ToUpperInvariant(), Ui.Head(11 * u), CueArt.TierColor(cue.Tier), stage.X + 22 * u, stage.Y + 56 * u, 1.8f * u);
        DrawCue(g, stage);
        Ui.Write(g, "Drag the cue to turn it.", Ui.Body(12.5f * u), Ui.Text3, new RectangleF(stage.X + 22 * u, stage.Bottom - 28 * u, stage.Width - 44 * u, 18 * u));

        float x = stage.Right + 28 * u;
        float w = s.Width - x - 40 * u;
        float y = 108 * u;
        Section(g, x, y, "MARK");
        y += 28 * u;
        y = Zones(g, x, y, w);
        y += 14 * u;
        y = Marks(g, x, y, w);
        y += 18 * u;
        Section(g, x, y, "FINISH");
        y += 28 * u;
        y = Choice(g, x, y, w, "TRIM", look.Trim, ["", "silver", "gold", "rose"], v => look.Trim = v, LabelTrim);
        y = Choice(g, x, y, w, "WRAP", look.WrapKind, ["", "linen", "leather", "plain"], v => look.WrapKind = v, LabelWrap);
        y = Choice(g, x, y, w, "SHEEN", look.Finish, ["", "gloss", "glow"], v => look.Finish = v, LabelFinish);
        y = Choice(g, x, y, w, "ORNAMENT", look.Charm, ["", "disc", "charmring", "charmstar"], v => look.Charm = v, LabelCharm);
        y += 8 * u;
        Ui.Tracked(g, "SIGNATURE", Ui.Head(11 * u), Ui.Text3, x, y + 8 * u, 1.6f * u);
        string sig = look.Signature.Length == 0 ? "Type a name" : look.Signature;
        Ui.Write(g, sig, Ui.BodyStrong(16 * u), look.Signature.Length == 0 ? Ui.Text3 : Ui.Text, new RectangleF(x + 140 * u, y, w - 140 * u, 24 * u));
        y += 36 * u;
        Ui.Write(g, "The mark sits on the forearm, the wrap, or the sleeve. The tip stays clear.", Ui.Body(12.5f * u), Ui.Text3, new RectangleF(x, y, w, 36 * u));

        float bh = done.Height(u);
        done.Rect = new RectangleF(s.Width - 40 * u - 200 * u, s.Height - 28 * u - bh, 200 * u, bh);
        reset.Rect = new RectangleF(done.Rect.X - 150 * u, done.Rect.Y, 140 * u, bh);
        done.Draw(g, u);
        reset.Draw(g, u);

        if (noteT > 0)
            Ui.Write(g, note, Ui.BodyStrong(14 * u), Ui.Text, new RectangleF(40 * u, s.Height - 36 * u - bh, 420 * u, 22 * u));
    }

    void DrawCue(Graphics g, RectangleF stage)
    {
        float cx = stage.X + stage.Width / 2, cy = stage.Y + stage.Height / 2 + 10 * u;
        float len = Math.Min(stage.Width - 80 * u, 460 * u);
        float rad = angle * MathF.PI / 180f;
        var tip = new PointF(cx + MathF.Cos(rad) * len / 2, cy + MathF.Sin(rad) * len / 2);
        var butt = new PointF(cx - MathF.Cos(rad) * len / 2, cy - MathF.Sin(rad) * len / 2);
        CueArt.Draw(g, tip, butt, 4f * u, 11f * u, cue, time, u, dress: look.IsBlank ? null : look);
    }

    void Section(Graphics g, float x, float y, string title) =>
        Ui.Tracked(g, title, Ui.Head(12 * u), Ui.Gold, x, y, 1.8f * u);

    float Zones(Graphics g, float x, float y, float w)
    {
        foreach (var (id, label) in new[] { ("forearm", "FOREARM"), ("wrap", "WRAP"), ("sleeve", "SLEEVE") })
        {
            bool on = zone == id;
            var chip = Chip(g, x, y, label, on ? Ui.Gold : Ui.Text2, on);
            string z = id;
            hits.Add((chip, () => zone = z));
            x += chip.Width + 8 * u;
        }
        return y + 36 * u;
    }

    float Marks(Graphics g, float x, float y, float w)
    {
        float rowX = x;
        foreach (var m in CueMarks.All)
        {
            bool owned = cfg.OwnsMark(m.Id);
            bool on = look.MarkAt(zone) == m.Id;
            string label = m.Name.ToUpperInvariant();
            // Measure first, so a chip that does not fit is drawn once, on the next row.
            if (rowX > x && rowX + ChipWidth(g, label) > x + w)
            {
                rowX = x;
                y += 36 * u;
            }
            var chip = Chip(g, rowX, y, label, on ? Ui.Gold : owned ? Ui.Text : Ui.Text3, on);
            var mark = m;
            hits.Add((chip, () => Toggle(mark)));
            rowX += chip.Width + 8 * u;
        }
        return y + 36 * u;
    }

    float Choice(Graphics g, float x, float y, float w, string label, string value, string[] options, Action<string> set, Func<string, string> name)
    {
        Ui.Tracked(g, label, Ui.Head(11 * u), Ui.Text3, x, y + 10 * u, 1.6f * u);
        var chip = Chip(g, x + 140 * u, y, name(value), Ui.Text, false);
        hits.Add((chip, () =>
        {
            int i = Array.IndexOf(options, value);
            set(options[(i + 1) % options.Length]);
            Save();
        }));
        return y + 36 * u;
    }

    float ChipWidth(Graphics g, string text)
    {
        var font = Ui.Head(11 * u);
        return Ui.TrackedWidth(g, text, font, font.Size * 0.12f) + 22 * u;
    }

    RectangleF Chip(Graphics g, float x, float y, string text, Color color, bool on)
    {
        var font = Ui.Head(11 * u);
        var r = new RectangleF(x, y, ChipWidth(g, text), 28 * u);
        if (on)
        {
            using var fill = new SolidBrush(Color.FromArgb(28, Ui.Gold));
            g.FillRectangle(fill, r);
        }
        using (var pen = new Pen(on ? Ui.Gold : Ui.LineStrong, 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        Ui.PlaceCentered(g, text, font, color, r, font.Size * 0.12f);
        return r;
    }

    void Toggle(CueMarks.Mark mark)
    {
        if (!cfg.OwnsMark(mark.Id))
        {
            if (mark.PackPrice == 0) cfg.TryBuyPack(mark.Set);
            else if (!cfg.TryBuyPack(mark.Set)) { Say($"The {mark.Set} set is {mark.PackPrice:N0} coins."); return; }
            else Say($"{mark.Set} set added.");
        }
        look.SetMark(zone, look.MarkAt(zone) == mark.Id ? "" : mark.Id);
        Save();
    }

    void Clear()
    {
        look.Forearm = look.WrapMark = look.Sleeve = look.Trim = look.WrapKind = look.Finish = look.Charm = look.Signature = "";
        Save();
    }

    void Save() => cfg.SaveDress(cue.Id, look);

    void Say(string text) { note = text; noteT = 2.4; }

    static string LabelTrim(string v) => v switch { "silver" => "SILVER", "gold" => "GOLD", "rose" => "ROSE", _ => "CUE'S OWN" };
    static string LabelWrap(string v) => v switch { "linen" => "LINEN", "leather" => "LEATHER", "plain" => "BARE", _ => "CUE'S OWN" };
    static string LabelFinish(string v) => v switch { "gloss" => "GLOSS", "glow" => "LAMP GLOW", _ => "CUE'S OWN" };
    static string LabelCharm(string v) => v switch { "disc" => "DISC", "charmring" => "RING", "charmstar" => "STAR", _ => "NONE" };

    public void MouseMove(MouseEventArgs e)
    {
        done.Hover = done.Contains(e.Location);
        reset.Hover = reset.Contains(e.Location);
        if (!turning) return;
        angle += (e.X - turnAt.X) * 0.4f;
        turnAt = e.Location;
    }

    public void MouseDown(MouseEventArgs e)
    {
        if (done.Contains(e.Location)) { done.OnClick(); return; }
        if (reset.Contains(e.Location)) { reset.OnClick(); return; }
        foreach (var (rect, act) in hits)
            if (rect.Contains(e.Location)) { act(); return; }
        turning = true;
        turnAt = e.Location;
    }

    public void MouseUp(MouseEventArgs e) => turning = false;

    public bool KeyDown(Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        if (k == Keys.Escape) { host.ShowShop(); return true; }
        if (k == Keys.Back)
        {
            if (look.Signature.Length > 0) look.Signature = look.Signature[..^1];
            Save();
            return true;
        }
        char ch = k switch
        {
            >= Keys.A and <= Keys.Z => (char)('A' + (k - Keys.A)),
            >= Keys.D0 and <= Keys.D9 => (char)('0' + (k - Keys.D0)),
            Keys.Space => ' ',
            _ => '\0',
        };
        if (ch != '\0' && look.Signature.Length < 10)
        {
            look.Signature += ch;
            Save();
            return true;
        }
        return false;
    }
}

namespace NeonCue.Core;

public enum CueTier { Common, Rare, Epic, Legendary, Mythic }

[Flags]
public enum CuePerk
{
    None = 0,
    /// <summary>Extra speed on the break shot.</summary>
    BreakBooster = 1,
    /// <summary>Carbon / low-deflection shaft: side spin barely pushes the cue ball off line.</summary>
    LowDeflection = 2,
    /// <summary>Aim guide shows the bounce after the first cushion.</summary>
    EagleEye = 4,
    /// <summary>Soft strokes stay truer. Full-power shots still wander.</summary>
    FeatherTouch = 8,
}

public enum PullCurve { Even, Soft, Heavy }

/// <summary>
/// A playable cue. Every stat changes the physics, not just a number on a card:
/// <see cref="Power"/> scales top speed, <see cref="Spin"/> scales english,
/// <see cref="AimError"/> is stroke wobble in degrees, <see cref="Squirt"/> is how far
/// (degrees per full english) side spin deflects the cue ball, and <see cref="Vision"/>
/// lengthens the aim guide.
/// </summary>
public sealed record CueSpec(
    string Id,
    string Name,
    string Maker,
    CueTier Tier,
    int Price,
    double Power,
    double Spin,
    double AimError,
    double Squirt,
    double Vision,
    double BreakBoost,
    CuePerk Perks,
    string Shaft,
    string Accent,
    string Butt,
    string Glow,
    string Story,
    PullCurve Draw = PullCurve.Even,
    string Trait = "",
    string Tone = "")
{
    static int Rate(double v, double lo, double hi) => (int)Math.Round(Math.Clamp((v - lo) / (hi - lo), 0, 1) * 100);

    public int PowerRating => Rate(Power * (1 + (BreakBoost - 1) * 0.5), 0.86, 1.2);
    public int SpinRating => Rate(Spin, 0.7, 1.4);
    public int AccuracyRating => Rate(0.3 - AimError, 0, 0.3);
    public int ControlRating => Rate(1.7 - Squirt, 0, 1.5);
    public int VisionRating => Rate(Vision, 0.6, 1.4);
    public int Overall => (PowerRating + SpinRating + AccuracyRating + ControlRating + VisionRating) / 5;

    public bool Has(CuePerk perk) => (Perks & perk) != 0;

    /// <summary>Stroke wobble (degrees, 1 sigma). Feather cues stay truer on a soft stroke and still wander at full power.</summary>
    public double WobbleDeg(double power)
    {
        double p = Math.Clamp(power, 0, 1);
        double k = Has(CuePerk.FeatherTouch) ? 0.1 + 0.9 * p * p : 0.3 + 0.7 * p * p;
        return AimError * k;
    }

    /// <summary>Maps a 0–1 draw onto shot power. Soft cues stay gentle longer. Heavy cues arrive at speed sooner.</summary>
    public double ShapePull(double fraction)
    {
        double t = Math.Clamp(fraction, 0, 1);
        return Draw switch
        {
            PullCurve.Soft => Math.Pow(t, 1.45),
            PullCurve.Heavy => Math.Pow(t, 0.68),
            _ => t,
        };
    }

    public string DrawName => Draw switch
    {
        PullCurve.Soft => "Soft draw",
        PullCurve.Heavy => "Heavy draw",
        _ => "Even draw",
    };
}

public static class CueCatalog
{
    /// <summary>Neutral stats (used when no cue is given).</summary>
    public static readonly CueSpec Standard = new("standard", "Standard", "", CueTier.Common, 0,
        1.0, 1.0, 0, 0, 1.0, 1.0, CuePerk.None, "#C9A26B", "#00F0FF", "#2A1E14", "#00F0FF", "");

    public static readonly IReadOnlyList<CueSpec> All =
    [
        new("house", "House Cue", "Club Standard", CueTier.Common, 0,
            Power: 0.9, Spin: 0.7, AimError: 0.25, Squirt: 1.6, Vision: 0.6, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#B08A5A", Accent: "#5A6B7D", Butt: "#3A2A1C", Glow: "#8E89BC",
            Story: "The house cue. A maple shaft and a familiar hit, issued with every new account.",
            Trait: "No specialty. A plain stroke, and the full deflection of a bar cue.",
            Tone: "Dull maple knock"),
        new("starter", "Neon Starter", "Neon Cue Works", CueTier.Common, 500,
            Power: 0.95, Spin: 0.85, AimError: 0.18, Squirt: 1.35, Vision: 0.75, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#D9B985", Accent: "#00F0FF", Butt: "#14243A", Glow: "#00F0FF",
            Story: "A straight maple shaft with a marked joint. Specified for daily practice.",
            Trait: "A cleaner daily cue. More spin than the house cue, still an honest miss.",
            Tone: "Clean maple tock"),
        new("linen", "Ash Linen", "Northlight Cues", CueTier.Common, 800,
            Power: 0.88, Spin: 0.95, AimError: 0.12, Squirt: 1.2, Vision: 0.9, BreakBoost: 1.0, CuePerk.FeatherTouch,
            Shaft: "#E7D7B4", Accent: "#C9BBA4", Butt: "#6B5344", Glow: "#E7D7B4",
            Story: "Pale ash, a linen wrap, and a soft tip. Made for the shot you barely mean to play.",
            Draw: PullCurve.Soft, Trait: "Feather touch. Soft strokes stay straight. It will not break a rack.",
            Tone: "Quiet linen tap"),
        new("viper", "Violet Viper", "Serpent Custom Cues", CueTier.Rare, 1500,
            Power: 1.0, Spin: 1.08, AimError: 0.15, Squirt: 1.05, Vision: 0.88, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#E2C79A", Accent: "#8A5CFF", Butt: "#24123F", Glow: "#8A5CFF",
            Story: "Snakeskin wrap and a lively tip. The specification used in league play.",
            Trait: "League spin. A lively tip, and ordinary deflection when you use it.",
            Tone: "Snappy maple crack"),
        new("ivory", "Bank Ivory", "Harbour & Points", CueTier.Rare, 2400,
            Power: 0.97, Spin: 0.92, AimError: 0.09, Squirt: 0.95, Vision: 1.08, BreakBoost: 1.0, CuePerk.EagleEye,
            Shaft: "#F3EFE4", Accent: "#8C5A3C", Butt: "#4A2A1C", Glow: "#F3EFE4",
            Story: "Ivory points and a long forearm. Built for the ball that has to leave a cushion on the right line.",
            Trait: "Rail reading. The guide follows the first cushion. Power is ordinary.",
            Tone: "Dry ivory click"),
        new("thunder", "Thunder Breaker", "Storm Forge", CueTier.Rare, 3200,
            Power: 1.14, Spin: 0.82, AimError: 0.2, Squirt: 1.25, Vision: 0.78, BreakBoost: 1.24, CuePerk.BreakBooster,
            Shaft: "#C7C9D1", Accent: "#FFC23D", Butt: "#1C1F2B", Glow: "#FFC23D",
            Story: "A phenolic tip and a weighted butt, built for the opening break.",
            Draw: PullCurve.Heavy, Trait: "Break weight. The opening shot is faster. Soft position is coarse.",
            Tone: "Hard phenolic crack"),
        new("phantom", "Carbon Phantom", "Black Laboratory", CueTier.Epic, 6000,
            Power: 1.02, Spin: 1.12, AimError: 0.08, Squirt: 0.32, Vision: 1.0, BreakBoost: 1.0, CuePerk.LowDeflection,
            Shaft: "#2B2D33", Accent: "#00F0FF", Butt: "#101114", Glow: "#00F0FF",
            Story: "A carbon low-deflection shaft. Side spin leaves the cue ball on the intended line.",
            Trait: "Low deflection. Side spin stays near the line. It is not a power cue.",
            Tone: "Glassy carbon tick"),
        new("helix", "Helix Carbon", "Black Laboratory", CueTier.Epic, 7800,
            Power: 1.0, Spin: 1.16, AimError: 0.07, Squirt: 0.26, Vision: 0.96, BreakBoost: 1.0, CuePerk.LowDeflection,
            Shaft: "#1A1C22", Accent: "#3DFFE8", Butt: "#07080C", Glow: "#3DFFE8",
            Story: "A helical carbon weave and a cyan joint. The shaft that refuses to squirt.",
            Trait: "The straightest side spin in the room. Less speed, and a shorter guide.",
            Tone: "High carbon ring"),
        new("silk", "Silk Road Inlay", "Atelier Damascus", CueTier.Epic, 12000,
            Power: 1.03, Spin: 1.28, AimError: 0.05, Squirt: 0.72, Vision: 1.12, BreakBoost: 1.0, CuePerk.FeatherTouch,
            Shaft: "#EAD3A8", Accent: "#16B864", Butt: "#3B1020", Glow: "#16B864",
            Story: "Mother-of-pearl points, cut by hand. Built for controlled spin and position.",
            Draw: PullCurve.Soft, Trait: "Position play. Spin on a soft draw, without a wild stroke.",
            Tone: "Warm pearl knock"),
        new("aurora", "Aurora Crystal", "Limited Atelier", CueTier.Legendary, 18000,
            Power: 0.98, Spin: 1.2, AimError: 0.04, Squirt: 0.5, Vision: 1.34, BreakBoost: 1.0, CuePerk.EagleEye,
            Shaft: "#F7F1FF", Accent: "#9AD7FF", Butt: "#243044", Glow: "#C9B6FF",
            Story: "A short run. Crystal points and a pale shaft, made to show the whole table.",
            Draw: PullCurve.Soft, Trait: "Table vision. The longest read of a cushion, and no break speed.",
            Tone: "Crystal chime"),
        new("dragon", "Golden Dragon", "Imperial Cue House", CueTier.Legendary, 25000,
            Power: 1.06, Spin: 1.3, AimError: 0.03, Squirt: 0.42, Vision: 1.22, BreakBoost: 1.06, CuePerk.EagleEye | CuePerk.LowDeflection,
            Shaft: "#F2DDAF", Accent: "#FFC23D", Butt: "#5A0E12", Glow: "#FFC23D",
            Story: "Gold rings and a carved forearm. Reserved for title competition.",
            Trait: "Title balance. Guide, spin, and a quiet stroke. Thunder still breaks harder.",
            Tone: "Gold-fitted knock"),
        new("royale", "Black Diamond Royale", "Maison Noire", CueTier.Mythic, 50000,
            Power: 1.05, Spin: 1.26, AimError: 0.02, Squirt: 0.38, Vision: 1.28, BreakBoost: 1.04, CuePerk.EagleEye | CuePerk.LowDeflection,
            Shaft: "#1A1A22", Accent: "#FF2BD6", Butt: "#050508", Glow: "#FF2BD6",
            Story: "One of nine. Ebony, a diamond sleeve, and a stroke that asks you to play the shot.",
            Draw: PullCurve.Soft, Trait: "Collector's cue. Touch and vision. It does not replace the break cue or the helix.",
            Tone: "Deep diamond ring"),
    ];

    public static CueSpec Get(string? id) => All.FirstOrDefault(c => c.Id == id) ?? All[0];

    /// <summary>The cue each CPU level brings to the table.</summary>
    public static CueSpec ForAI(AILevel level) => level switch
    {
        AILevel.Rookie => Get("house"),
        AILevel.Amateur => Get("viper"),
        AILevel.Pro => Get("phantom"),
        _ => Get("dragon"),
    };

    /// <summary>What matters most in each mode: power, spin, accuracy, control, vision.</summary>
    static (double p, double s, double a, double c, double v) Weights(GameMode mode) => mode switch
    {
        GameMode.NineBall or GameMode.TenBall => (1.6, 1.0, 1.3, 1.0, 1.0),
        GameMode.StraightPool => (0.6, 1.0, 1.6, 1.1, 1.2),
        GameMode.Rotation => (1.0, 1.2, 1.4, 1.0, 1.0),
        GameMode.Practice => (0.8, 1.3, 1.2, 1.0, 1.3),
        GameMode.Snooker => (0.5, 1.2, 1.7, 1.3, 1.2),
        _ => (1.0, 1.0, 1.4, 1.0, 1.0),
    };

    public static double FitScore(CueSpec c, GameMode mode)
    {
        var w = Weights(mode);
        return w.p * c.PowerRating + w.s * c.SpinRating + w.a * c.AccuracyRating + w.c * c.ControlRating + w.v * c.VisionRating;
    }

    /// <summary>Best cue for this mode that the player owns or can afford right now.</summary>
    public static CueSpec CoachPick(GameMode mode, int coins, IEnumerable<string> owned)
    {
        var have = owned.ToHashSet();
        return All.Where(c => have.Contains(c.Id) || c.Price <= coins)
            .OrderByDescending(c => FitScore(c, mode))
            .First();
    }

    public static string BestFor(CueSpec c)
    {
        if (c.Overall < 15) return "fundamentals";
        var scores = new (string label, int v)[]
        {
            ("the break", c.PowerRating), ("cue-ball position", c.SpinRating), ("potting", c.AccuracyRating),
            ("side spin", c.ControlRating), ("reading the table", c.VisionRating),
        };
        return scores.OrderByDescending(x => x.v).First().label;
    }
}

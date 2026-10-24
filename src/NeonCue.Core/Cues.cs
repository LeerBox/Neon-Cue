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
}

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
    string Story)
{
    static int Rate(double v, double lo, double hi) => (int)Math.Round(Math.Clamp((v - lo) / (hi - lo), 0, 1) * 100);

    public int PowerRating => Rate(Power * (1 + (BreakBoost - 1) * 0.5), 0.86, 1.2);
    public int SpinRating => Rate(Spin, 0.7, 1.4);
    public int AccuracyRating => Rate(0.5 - AimError, 0, 0.5);
    public int ControlRating => Rate(1.7 - Squirt, 0, 1.5);
    public int VisionRating => Rate(Vision, 0.6, 1.4);
    public int Overall => (PowerRating + SpinRating + AccuracyRating + ControlRating + VisionRating) / 5;

    public bool Has(CuePerk perk) => (Perks & perk) != 0;
}

public static class CueCatalog
{
    /// <summary>Neutral stats (used when no cue is given).</summary>
    public static readonly CueSpec Standard = new("standard", "Standard", "", CueTier.Common, 0,
        1.0, 1.0, 0, 0, 1.0, 1.0, CuePerk.None, "#C9A26B", "#00F0FF", "#2A1E14", "#00F0FF", "");

    public static readonly IReadOnlyList<CueSpec> All =
    [
        new("house", "House Cue", "Bar rack", CueTier.Common, 0,
            Power: 0.9, Spin: 0.7, AimError: 0.5, Squirt: 1.6, Vision: 0.6, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#B08A5A", Accent: "#5A6B7D", Butt: "#3A2A1C", Glow: "#8E89BC",
            Story: "Warped a little, chalked a lot. Every legend started with one of these."),
        new("starter", "Neon Starter", "Neon Cue Works", CueTier.Common, 500,
            Power: 0.95, Spin: 0.85, AimError: 0.36, Squirt: 1.35, Vision: 0.75, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#D9B985", Accent: "#00F0FF", Butt: "#14243A", Glow: "#00F0FF",
            Story: "A straight maple shaft and a glowing ring. Honest and reliable."),
        new("viper", "Violet Viper", "Serpent Custom", CueTier.Rare, 1500,
            Power: 1.0, Spin: 1.05, AimError: 0.25, Squirt: 1.05, Vision: 0.88, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#E2C79A", Accent: "#8A5CFF", Butt: "#24123F", Glow: "#8A5CFF",
            Story: "Snake-skin wrap and a quick, lively hit. Loved by league players."),
        new("thunder", "Thunder Breaker", "Storm Forge", CueTier.Rare, 3000,
            Power: 1.12, Spin: 0.85, AimError: 0.3, Squirt: 1.2, Vision: 0.8, BreakBoost: 1.22, CuePerk.BreakBooster,
            Shaft: "#C7C9D1", Accent: "#FFC23D", Butt: "#1C1F2B", Glow: "#FFC23D",
            Story: "Phenolic tip, heavy butt. Built for one job: exploding the rack."),
        new("phantom", "Carbon Phantom", "Black Lab", CueTier.Epic, 6000,
            Power: 1.02, Spin: 1.15, AimError: 0.13, Squirt: 0.3, Vision: 1.0, BreakBoost: 1.0, CuePerk.LowDeflection,
            Shaft: "#2B2D33", Accent: "#00F0FF", Butt: "#101114", Glow: "#00F0FF",
            Story: "Carbon-fibre low-deflection shaft. Use english without aiming around it."),
        new("silk", "Silk Road Inlay", "Atelier Damascus", CueTier.Epic, 12000,
            Power: 1.04, Spin: 1.25, AimError: 0.09, Squirt: 0.7, Vision: 1.12, BreakBoost: 1.0, CuePerk.None,
            Shaft: "#EAD3A8", Accent: "#16B864", Butt: "#3B1020", Glow: "#16B864",
            Story: "Hand-cut mother-of-pearl inlays. Buttery feel, surgical spin."),
        new("dragon", "Golden Dragon", "Imperial Cue House", CueTier.Legendary, 25000,
            Power: 1.08, Spin: 1.35, AimError: 0.04, Squirt: 0.4, Vision: 1.25, BreakBoost: 1.08, CuePerk.EagleEye | CuePerk.LowDeflection,
            Shaft: "#F2DDAF", Accent: "#FFC23D", Butt: "#5A0E12", Glow: "#FFC23D",
            Story: "24k gold rings and a dragon carved down the butt. Champions only."),
        new("royale", "Black Diamond Royale", "Maison Noire", CueTier.Mythic, 50000,
            Power: 1.1, Spin: 1.4, AimError: 0.0, Squirt: 0.2, Vision: 1.4, BreakBoost: 1.15, CuePerk.EagleEye | CuePerk.LowDeflection | CuePerk.BreakBooster,
            Shaft: "#1A1A22", Accent: "#FF2BD6", Butt: "#050508", Glow: "#FF2BD6",
            Story: "One of nine ever made. Ebony, diamonds and a stroke that never misses."),
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
        if (c.Overall < 15) return "learning the basics";
        var scores = new (string label, int v)[]
        {
            ("breaking", c.PowerRating), ("spin & position", c.SpinRating), ("precision potting", c.AccuracyRating),
            ("english without deflection", c.ControlRating), ("reading the table", c.VisionRating),
        };
        return scores.OrderByDescending(x => x.v).First().label;
    }
}

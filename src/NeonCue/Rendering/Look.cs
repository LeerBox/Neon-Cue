namespace NeonCue.Rendering;

public enum TableStyle { Classic, Tournament, Royal, Neon }

/// <summary>The active visual style and its materials.</summary>
static class Look
{
    public static TableStyle Style { get; set; } = TableStyle.Classic;
    public static bool Realistic => Style != TableStyle.Neon;

    /// <summary>True while a snooker frame is on screen: plain balls, snooker colours.</summary>
    public static bool Snooker { get; set; }

    /// <summary>Snooker set: 1-15 reds, 16 yellow, 17 green, 18 brown, 19 blue, 20 pink, 21 black.</summary>
    public static Rgb SnookerColor(int n) => n switch
    {
        0 => Rgb.Hex("#F6F3EA"),
        <= 15 => Rgb.Hex("#C8102E"),
        16 => Rgb.Hex("#F3C300"),
        17 => Rgb.Hex("#0B7A3E"),
        18 => Rgb.Hex("#6E3B1F"),
        19 => Rgb.Hex("#1546B5"),
        20 => Rgb.Hex("#F49AC1"),
        _ => Rgb.Hex("#0B0B0C"),
    };

    public static string Name(TableStyle s) => s switch
    {
        TableStyle.Classic => "Classic green",
        TableStyle.Tournament => "Tournament blue",
        TableStyle.Royal => "Royal burgundy",
        _ => "Neon",
    };

    public static Rgb Felt => Style switch
    {
        TableStyle.Classic => Rgb.Hex("#1F7A3C"),
        TableStyle.Tournament => Rgb.Hex("#1E5DA6"),
        TableStyle.Royal => Rgb.Hex("#7C1C2A"),
        _ => Rgb.Hex("#0E4A78"),
    };

    public static WoodKind Rail => Style switch
    {
        TableStyle.Tournament => WoodKind.BlackLacquer,
        TableStyle.Royal => WoodKind.Mahogany,
        _ => WoodKind.Walnut,
    };

    public static WoodKind Floor => WoodKind.Oak;

    /// <summary>Realistic ball colours (phenolic resin set).</summary>
    public static Rgb BallColor(int n) => (n <= 8 ? n : n - 8) switch
    {
        0 => Rgb.Hex("#F4F1E6"),
        1 => Rgb.Hex("#F6BE00"),
        2 => Rgb.Hex("#1043B0"),
        3 => Rgb.Hex("#D01F1F"),
        4 => Rgb.Hex("#4A1D80"),
        5 => Rgb.Hex("#F26A0A"),
        6 => Rgb.Hex("#0A7A3A"),
        7 => Rgb.Hex("#7D1416"),
        _ => Rgb.Hex("#0B0B0C"),
    };
}

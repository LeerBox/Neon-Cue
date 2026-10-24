namespace NeonCue.Rendering;

public enum TableStyle { Classic, Tournament, Royal, Neon }

/// <summary>The active visual style and its materials.</summary>
static class Look
{
    public static TableStyle Style { get; set; } = TableStyle.Classic;
    public static bool Realistic => Style != TableStyle.Neon;

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

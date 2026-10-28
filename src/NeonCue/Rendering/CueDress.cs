using NeonCue.Core;

namespace NeonCue.Rendering;

/// <summary>
/// A saved cosmetic look for one owned cue. It never changes power, spin, or the stroke.
/// Marks sit in three zones: the forearm, the wrap, and the sleeve. The tip is never covered.
/// </summary>
public sealed class CueDress
{
    public string Forearm { get; set; } = "";
    public string WrapMark { get; set; } = "";
    public string Sleeve { get; set; } = "";
    public string Trim { get; set; } = "";
    public string WrapKind { get; set; } = "";
    public string Finish { get; set; } = "";
    public string Charm { get; set; } = "";
    public string Signature { get; set; } = "";

    public bool IsBlank =>
        Forearm.Length == 0 && WrapMark.Length == 0 && Sleeve.Length == 0
        && Trim.Length == 0 && WrapKind.Length == 0 && Finish.Length == 0
        && Charm.Length == 0 && Signature.Length == 0;

    public string Key => string.Join('\u001f', Forearm, WrapMark, Sleeve, Trim, WrapKind, Finish, Charm, Signature);

    public CueDress Clone() => new()
    {
        Forearm = Forearm, WrapMark = WrapMark, Sleeve = Sleeve,
        Trim = Trim, WrapKind = WrapKind, Finish = Finish, Charm = Charm, Signature = Signature,
    };

    public string MarkAt(string zone) => zone switch
    {
        "wrap" => WrapMark,
        "sleeve" => Sleeve,
        _ => Forearm,
    };

    public void SetMark(string zone, string id)
    {
        switch (zone)
        {
            case "wrap": WrapMark = id; break;
            case "sleeve": Sleeve = id; break;
            default: Forearm = id; break;
        }
    }
}

/// <summary>Stickers and packs. A mark is a decal, not a stat.</summary>
public static class CueMarks
{
    public sealed record Mark(string Id, string Name, string Set, int PackPrice, string Note);

    public static readonly Mark[] All =
    [
        new("stripe", "Centre stripe", "Studio", 0, "One quiet band."),
        new("crest", "Club crest", "Studio", 0, "A ring and a core."),
        new("dot", "Index dot", "Studio", 0, "A single point."),
        new("ring", "Fine rings", "Studio", 0, "Two hairlines."),
        new("bolt", "Storm bolt", "Night", 450, "A sharp mark."),
        new("diamond", "Cut diamond", "Night", 450, "A small stone."),
        new("wave", "Tide line", "Night", 450, "A low curve."),
        new("seal", "Atelier seal", "Atelier", 900, "A pressed circle."),
        new("laurel", "Laurel", "Atelier", 900, "Two leaves."),
        new("star", "North star", "Atelier", 900, "A five-point star."),
    ];

    public static Mark? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    public static IEnumerable<string> Sets => All.Select(m => m.Set).Distinct();

    public static int PackPrice(string set) => All.First(m => m.Set == set).PackPrice;

    public static IEnumerable<Mark> In(string set) => All.Where(m => m.Set == set);
}

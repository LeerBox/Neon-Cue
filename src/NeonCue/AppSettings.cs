using System.Text.Json;
using System.Text.Json.Serialization;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue;

public enum AimGuide { Off, GhostBall, Full }
public enum AimAssist { Off, Light, Strong }

/// <summary>Menu choices, remembered between sessions in %AppData%\NeonCue.</summary>
public sealed class AppSettings
{
    public GameMode Mode { get; set; } = GameMode.EightBall;
    public OpponentType Opponent { get; set; } = OpponentType.Cpu;
    public AILevel Level { get; set; } = AILevel.Pro;
    public int RaceTo { get; set; } = 3;
    public int TargetScore { get; set; } = 50;
    public AimGuide Guide { get; set; } = AimGuide.Full;
    public AimAssist Assist { get; set; } = AimAssist.Light;
    /// <summary>Off: the ball goes exactly where the guide shows. On: the cue's stroke wobble applies.</summary>
    public bool StrokeError { get; set; }
    public bool Sound { get; set; } = true;
    public bool Fullscreen { get; set; }
    public TableStyle Style { get; set; } = TableStyle.Classic;

    public int Coins { get; set; } = 300;
    public List<string> OwnedCues { get; set; } = ["house"];
    public string EquippedCue { get; set; } = "house";
    /// <summary>Cosmetic looks, keyed by cue id. Empty means the cue's own design.</summary>
    public Dictionary<string, CueDress> Looks { get; set; } = new();
    /// <summary>Sticker sets the player may apply. The Studio set is free.</summary>
    public List<string> OwnedMarks { get; set; } = ["stripe", "crest", "dot", "ring"];

    [JsonIgnore]
    public CueSpec Equipped => CueCatalog.Get(OwnedCues.Contains(EquippedCue) ? EquippedCue : "house");
    public bool Owns(CueSpec cue) => cue.Price == 0 || OwnedCues.Contains(cue.Id);

    public static string SkillName(AILevel level) => level switch
    {
        AILevel.Rookie => "Rookie",
        AILevel.Amateur => "Club",
        AILevel.Pro => "Professional",
        _ => "Master",
    };

    public CueDress DressFor(string cueId) =>
        Looks.TryGetValue(cueId, out var d) && d != null ? d : new CueDress();

    public bool OwnsMark(string id) => OwnedMarks.Contains(id);

    public bool TryBuyPack(string set)
    {
        int price = CueMarks.PackPrice(set);
        if (price > 0 && Coins < price) return false;
        if (price > 0) Coins -= price;
        foreach (var m in CueMarks.In(set))
            if (!OwnedMarks.Contains(m.Id)) OwnedMarks.Add(m.Id);
        Save();
        return true;
    }

    public void SaveDress(string cueId, CueDress dress)
    {
        if (dress.IsBlank) Looks.Remove(cueId);
        else Looks[cueId] = dress;
        Save();
    }

    public bool TryBuy(CueSpec cue)
    {
        if (Owns(cue) || Coins < cue.Price) return false;
        Coins -= cue.Price;
        OwnedCues.Add(cue.Id);
        EquippedCue = cue.Id;
        Save();
        return true;
    }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeonCue", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: start from defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save is not fatal.
        }
    }
}

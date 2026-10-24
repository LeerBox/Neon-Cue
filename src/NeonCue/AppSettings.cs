using System.Text.Json;
using System.Text.Json.Serialization;
using NeonCue.Core;
using NeonCue.Rendering;

namespace NeonCue;

public enum AimGuide { Off, GhostBall, Full }

/// <summary>Menu choices, remembered between sessions in %AppData%\NeonCue.</summary>
public sealed class AppSettings
{
    public GameMode Mode { get; set; } = GameMode.EightBall;
    public OpponentType Opponent { get; set; } = OpponentType.Cpu;
    public AILevel Level { get; set; } = AILevel.Pro;
    public int RaceTo { get; set; } = 3;
    public int TargetScore { get; set; } = 50;
    public AimGuide Guide { get; set; } = AimGuide.Full;
    public bool Sound { get; set; } = true;
    public TableStyle Style { get; set; } = TableStyle.Classic;

    public int Coins { get; set; } = 300;
    public List<string> OwnedCues { get; set; } = ["house"];
    public string EquippedCue { get; set; } = "house";

    [JsonIgnore]
    public CueSpec Equipped => CueCatalog.Get(OwnedCues.Contains(EquippedCue) ? EquippedCue : "house");
    public bool Owns(CueSpec cue) => cue.Price == 0 || OwnedCues.Contains(cue.Id);

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

using System.Text.Json;
using NeonCue.Core;

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

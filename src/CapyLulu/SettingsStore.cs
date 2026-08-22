using System.IO;
using System.Text.Json;

namespace CapyLulu;

internal sealed class PetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    public bool Topmost { get; set; } = true;
    public string? SelectedCharacter { get; set; }
    public string Mood { get; set; } = "Happy";
    public string GazeMode { get; set; } = "Follow";
}

internal static class SettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CapyLulu");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static PetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new PetSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<PetSettings>(json) ?? new PetSettings();
        }
        catch
        {
            return new PetSettings();
        }
    }

    public static void Save(PetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 设置写入失败不应影响桌宠继续运行。
        }
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapyLulu;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
            return JsonSerializer.Deserialize<PetSettings>(json, JsonOptions) ?? new PetSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            PreserveUnreadableSettings();
            return new PetSettings();
        }
    }

    public static void Save(PetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 设置写入失败不应影响桌宠继续运行。
        }
    }

    private static void PreserveUnreadableSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var backupPath = Path.Combine(
                SettingsDirectory,
                $"settings.invalid-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Move(SettingsPath, backupPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 无法备份时仍回退到默认设置。
        }
    }
}

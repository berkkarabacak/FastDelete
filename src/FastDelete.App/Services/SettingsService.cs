using System.IO;
using System.Text.Json;

namespace FastDelete.App.Services;

/// <summary>User preferences: last folder, theme, window geometry. Survives restarts.</summary>
public sealed record AppSettings(
    string? LastPath = null,
    bool DarkTheme = false,
    double Width = 1280,
    double Height = 800,
    double Left = double.NaN,
    double Top = double.NaN,
    bool Maximized = false);

public static class SettingsService
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastDelete", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt or locked settings must never block startup */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch { /* best effort */ }
    }
}

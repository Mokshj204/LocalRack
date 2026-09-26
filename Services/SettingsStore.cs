using System.IO;
using System.Text.Json;
using LocalRack.Models;

namespace LocalRack.Services;

/// <summary>App-wide preferences stored in settings.json. (Start-with-Windows lives in the registry instead.)</summary>
public static class SettingsStore
{
    public static AppSettings Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Current = JsonSerializer.Deserialize(json, AppJsonContext.Readable.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Unreadable preferences fall back to defaults; nothing critical is stored here.
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(Current, AppJsonContext.Readable.AppSettings);
        var temp = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        Changed?.Invoke();
    }
}

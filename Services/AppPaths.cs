using System.IO;

namespace LocalRack.Services;

public static class AppPaths
{
    public const string AppName = "LocalRack";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ProjectsFile => Path.Combine(DataDirectory, "projects.json");
    public static string UiStateFile => Path.Combine(DataDirectory, "ui-state.json");
    public static string ErrorLogFile => Path.Combine(DataDirectory, "error.log");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Passed by the Windows startup entry so LocalRack can start quietly in the tray.</summary>
    public const string BackgroundArgument = "--background";

    /// <summary>Moves data saved under the app's previous name so existing projects carry over.</summary>
    public static void MigrateLegacyData()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsServiceManager");
        if (!Directory.Exists(legacy) || Directory.Exists(DataDirectory)) return;

        try
        {
            Directory.Move(legacy, DataDirectory);
        }
        catch (IOException)
        {
            // Another instance may hold a file open; the old data stays put and nothing is lost.
        }
    }
}

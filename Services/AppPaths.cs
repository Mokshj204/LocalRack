using System.IO;

namespace LocalRack.Services;

public static class AppPaths
{
    public const string AppName = "LocalRack";

    /// <summary>
    /// Keys everything a running copy owns: data folder, single-instance lock, startup entry.
    /// Debug builds get their own so developing LocalRack never touches the installed copy.
    /// </summary>
#if DEBUG
    public const string InstanceId = "LocalRack-Dev";
#else
    public const string InstanceId = AppName;
#endif

    /// <summary>Held while any release copy runs, so the installer can ask the user to close it first.</summary>
    public const string RunningMutexName = InstanceId + "-AppRunning";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), InstanceId);

    public static string ProjectsFile => Path.Combine(DataDirectory, "projects.json");
    public static string UiStateFile => Path.Combine(DataDirectory, "ui-state.json");
    public static string ErrorLogFile => Path.Combine(DataDirectory, "error.log");
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Passed by the Windows startup entry so LocalRack can start quietly in the tray.</summary>
    public const string BackgroundArgument = "--background";

    /// <summary>Moves data saved under the app's previous name so existing projects carry over.</summary>
    public static void MigrateLegacyData()
    {
        // Dev builds keep their own data; the legacy folder belongs to the installed copy.
#if !DEBUG
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
#endif
    }
}

using Microsoft.Win32;

namespace LocalRack.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key (no admin rights needed).
/// The registry is the source of truth, so the setting always reflects what Windows will actually do.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ExecutablePath => Environment.ProcessPath ?? string.Empty;
    private static string LaunchCommand => $"\"{ExecutablePath}\" {AppPaths.BackgroundArgument}";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppPaths.InstanceId) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(AppPaths.InstanceId, LaunchCommand);
        }
        else
        {
            key.DeleteValue(AppPaths.InstanceId, throwOnMissingValue: false);
        }
    }

    /// <summary>If startup is enabled but the exe has since moved, point Windows at the current location.</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(AppPaths.InstanceId) is string current && current != LaunchCommand)
            {
                key.SetValue(AppPaths.InstanceId, LaunchCommand);
            }
        }
        catch (Exception)
        {
            // Non-critical; the Settings page still shows and fixes the state.
        }
    }
}

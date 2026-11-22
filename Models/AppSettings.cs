using System.Text.Json.Serialization;

namespace LocalRack.Models;

/// <summary>Which color scheme the UI uses. System follows the Windows "app mode" setting live.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed class AppSettings
{
    /// <summary>Minimizing or closing the window hides it to the tray; services keep running.</summary>
    public bool KeepRunningInTray { get; set; }

    /// <summary>Look for a newer GitHub release each time LocalRack starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Light, Dark, or follow Windows (default).</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}

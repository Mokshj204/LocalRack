namespace LocalRack.Models;

public sealed class AppSettings
{
    /// <summary>Minimizing or closing the window hides it to the tray; services keep running.</summary>
    public bool KeepRunningInTray { get; set; }

    /// <summary>Look for a newer GitHub release each time LocalRack starts.</summary>
    public bool CheckForUpdates { get; set; } = true;
}

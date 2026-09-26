using System.Windows;
using WinForms = System.Windows.Forms;

namespace LocalRack.Services;

/// <summary>
/// System tray icon. Wraps WinForms' NotifyIcon, which WPF lacks; WinForms is only loaded
/// once this class is first used, i.e. when the tray setting is on.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private bool _balloonShown;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        var open = menu.Items.Add("Open LocalRack", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit LocalRack (stops all services)", null, (_, _) => ExitRequested?.Invoke());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppPaths.AppName,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public void SetRunningCount(int running)
    {
        // NotifyIcon tooltips are limited to 127 characters.
        _icon.Text = running == 0
            ? $"{AppPaths.AppName} — no services running"
            : $"{AppPaths.AppName} — {running} service{(running == 1 ? "" : "s")} running";
    }

    /// <summary>Tells the user once per session that the app is still running after the window disappears.</summary>
    public void NotifyHiddenOnce()
    {
        if (_balloonShown) return;
        _balloonShown = true;
        _icon.ShowBalloonTip(
            3000,
            AppPaths.AppName,
            "LocalRack is still running in the tray. Your services keep running.",
            WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LocalRack.ico"));
        using var stream = resource!.Stream;
        return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }
}

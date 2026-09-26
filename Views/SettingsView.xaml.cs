using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LocalRack.Services;

namespace LocalRack.Views;

public partial class SettingsView : UserControl
{
    private bool _refreshing;

    public SettingsView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"{AppPaths.AppName} {version?.ToString(3)}  ·  Data: {AppPaths.DataDirectory}";
        Refresh();
    }

    /// <summary>Re-reads the real state (the registry can be changed outside the app, e.g. Task Manager's Startup tab).</summary>
    public void Refresh()
    {
        _refreshing = true;
        StartupToggle.IsChecked = StartupRegistration.IsEnabled();
        TrayToggle.IsChecked = SettingsStore.Current.KeepRunningInTray;
        _refreshing = false;
        var path = Environment.ProcessPath ?? string.Empty;
        var looksLikeDevBuild = path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        StartupNoteText.Text = $"Launches: {path}" +
            (looksLikeDevBuild ? "\nThis is a development build; enable this from the published LocalRack.exe instead." : string.Empty);
        StartupNoteText.Visibility = Visibility.Visible;
    }

    private void TrayToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        SettingsStore.Current.KeepRunningInTray = TrayToggle.IsChecked == true;
        try
        {
            SettingsStore.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!, $"Couldn't save the setting: {ex.Message}",
                AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Checked/Unchecked (not Click) so mouse, keyboard and accessibility tools all apply the change.
    private void StartupToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        var enable = StartupToggle.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enable);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!, $"Couldn't change the startup setting: {ex.Message}",
                AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }
}

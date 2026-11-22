using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LocalRack.Models;
using LocalRack.Services;

namespace LocalRack.Views;

public partial class SettingsView : UserControl
{
    private readonly Func<Task<string>> _checkForUpdates;
    private bool _refreshing;

    public SettingsView(Func<Task<string>> checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;
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
        UpdateToggle.IsChecked = SettingsStore.Current.CheckForUpdates;
        ThemeSystemRadio.IsChecked = SettingsStore.Current.Theme == ThemeMode.System;
        ThemeLightRadio.IsChecked = SettingsStore.Current.Theme == ThemeMode.Light;
        ThemeDarkRadio.IsChecked = SettingsStore.Current.Theme == ThemeMode.Dark;
        _refreshing = false;
        var path = Environment.ProcessPath ?? string.Empty;
        var looksLikeDevBuild = path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        StartupNoteText.Text = $"Launches: {path}" +
            (looksLikeDevBuild ? "\nThis is a development build; enable this from the published LocalRack.exe instead." : string.Empty);
        StartupNoteText.Visibility = Visibility.Visible;
    }

    private void ThemeRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        SettingsStore.Current.Theme = ReferenceEquals(sender, ThemeLightRadio) ? ThemeMode.Light
            : ReferenceEquals(sender, ThemeDarkRadio) ? ThemeMode.Dark
            : ThemeMode.System;
        try
        {
            SettingsStore.Save(); // raises Changed, which ThemeManager applies immediately
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!, $"Couldn't save the setting: {ex.Message}",
                AppPaths.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        SettingsStore.Current.CheckForUpdates = UpdateToggle.IsChecked == true;
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

    private async void CheckNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking...";
        try
        {
            UpdateStatusText.Text = await _checkForUpdates();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Couldn't check for updates: {ex.Message}";
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
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

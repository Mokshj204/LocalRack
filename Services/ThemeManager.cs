using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LocalRack.Models;
using Microsoft.Win32;

namespace LocalRack.Services;

/// <summary>
/// Switches the app between light and dark by replacing the brushes declared in Themes/Theme.xaml.
/// Controls reference them with DynamicResource, so the whole UI restyles live.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Dictionary<string, string> Light = new()
    {
        ["WindowBackgroundBrush"] = "#FAFAF7",
        ["PanelBackgroundBrush"] = "#F2F2EE",
        ["CardBackgroundBrush"] = "#FFFFFD",
        ["TextBrush"] = "#1F1F1F",
        ["SecondaryTextBrush"] = "#6B6B6B",
        ["ConsoleBackgroundBrush"] = "#FDFDFB",
        ["ConsoleTextBrush"] = "#1F1F1F",
        ["ErrorTextBrush"] = "#C62828",
        ["StatusRunningBrush"] = "#2E9E4F",
        ["StatusStoppedBrush"] = "#9AA0A6",
        ["BorderBrush"] = "#DADAD5",
        ["HoverBrush"] = "#E8E8E3",
        ["SelectedBrush"] = "#E1E7F0",
    };

    private static readonly Dictionary<string, string> Dark = new()
    {
        ["WindowBackgroundBrush"] = "#1B1B1D",
        ["PanelBackgroundBrush"] = "#262629",
        ["CardBackgroundBrush"] = "#2B2B2F",
        ["TextBrush"] = "#ECECEC",
        ["SecondaryTextBrush"] = "#A3A3AA",
        ["ConsoleBackgroundBrush"] = "#131315",
        ["ConsoleTextBrush"] = "#E6E6E6",
        ["ErrorTextBrush"] = "#FF7B7B",
        ["StatusRunningBrush"] = "#3FB765",
        ["StatusStoppedBrush"] = "#6B7076",
        ["BorderBrush"] = "#3C3C42",
        ["HoverBrush"] = "#36363B",
        ["SelectedBrush"] = "#2F3E58",
    };

    /// <summary>True when the dark palette is currently applied.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised on the UI thread after the palette changed.</summary>
    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ApplyTitleBar((Window)s)));
        SettingsStore.Changed += Apply;
        // Only matters while the theme is set to System, but cheap to always listen.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                Application.Current?.Dispatcher.BeginInvoke(Apply);
            }
        };
        Apply();
    }

    /// <summary>Resolves the current setting to dark/light and recolors the shared brushes.</summary>
    public static void Apply()
    {
        var dark = SettingsStore.Current.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark()
        };

        var changed = dark != IsDark;
        IsDark = dark;
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in dark ? Dark : Light)
        {
            // Brushes from a compiled dictionary are frozen, so swap in new instances;
            // every consumer binds with DynamicResource and picks the new brush up.
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        if (changed)
        {
            ThemeChanged?.Invoke();
        }
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Windows 10 20H1+ / Windows 11: makes the native title bar follow the theme.
    private const int DwmUseImmersiveDarkMode = 20;

    private static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var value = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (Exception)
        {
            // purely cosmetic
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

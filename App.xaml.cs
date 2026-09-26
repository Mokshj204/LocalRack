using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LocalRack.Services;

namespace LocalRack;

public partial class App : Application
{
    // Per-user names: one LocalRack per signed-in user.
    private static readonly string InstanceName = $"LocalRack-{Environment.UserName}";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(e.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second launch (e.g. opening the exe while LocalRack sits in the tray) just brings
        // the running window forward instead of starting another copy with its own processes.
        _instanceMutex = new Mutex(true, InstanceName + "-mutex", out var isFirstInstance);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-activate");
        if (!isFirstInstance)
        {
            _activateSignal.Set();
            Shutdown();
            return;
        }

        AppPaths.MigrateLegacyData();
        SettingsStore.Load();
        StartupRegistration.RefreshPathIfEnabled();

        var window = new MainWindow();
        MainWindow = window;

        var startInBackground = e.Args.Contains(AppPaths.BackgroundArgument, StringComparer.OrdinalIgnoreCase)
                                && SettingsStore.Current.KeepRunningInTray;
        if (!startInBackground)
        {
            window.Show();
        }

        var signal = _activateSignal;
        var listener = new Thread(() =>
        {
            while (signal.WaitOne())
            {
                Dispatcher.BeginInvoke(window.RestoreFromTray);
            }
        })
        {
            IsBackground = true,
            Name = "LocalRack activation listener"
        };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            $"Unexpected error: {e.Exception.Message}\n\nDetails were written to:\n{AppPaths.ErrorLogFile}",
            AppPaths.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogError(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.ErrorLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // logging must never throw
        }
    }
}

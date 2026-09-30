using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using LocalRack.Models;

namespace LocalRack.Services;

public sealed partial class ServiceProcessManager
{
    private const int MaxLogLines = 2000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Lazy<string> PowerShellExe = new(ResolvePowerShell);

    private readonly List<Service> _runningServices = new();

    /// <summary>Raised on the UI thread whenever a service starts or stops, with the new running count.</summary>
    public event Action<int>? RunningCountChanged;

    public int RunningCount => _runningServices.Count;
    private readonly DispatcherTimer _flushTimer;
    // 1 while a flush is queued or pending on the timer; set from reader threads, cleared on the UI thread.
    private int _flushScheduled;

    public ServiceProcessManager()
    {
        // One-shot: armed only when output arrives, so a quiet service costs no wakeups.
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FlushInterval
        };
        _flushTimer.Tick += (_, _) =>
        {
            _flushTimer.Stop();
            // Clear before draining: lines that arrive mid-drain schedule the next flush.
            Volatile.Write(ref _flushScheduled, 0);
            FlushPendingLines();
        };
    }

    public void Start(Service service)
    {
        if (service.Status == ServiceStatus.Running) return;

        if (!Directory.Exists(service.DirectoryPath))
        {
            AppendDirect(service, new LogLine($"[ERR] Directory not found: {service.DirectoryPath}", true));
            return;
        }

        var startInfo = BuildStartInfo(service);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // One parser per stream: color state carries across lines within a stream, like a terminal.
        var stdoutParser = new AnsiParser();
        var stderrParser = new AnsiParser();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Enqueue(service, stdoutParser.Parse(e.Data, false, null, null));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            // Many tools (npm notices, uvicorn/Python logging) write normal output to stderr,
            // so only lines that actually look like errors are flagged.
            var isError = ErrorPattern().IsMatch(AnsiParser.StripAnsi(e.Data));
            Enqueue(service, isError
                ? stderrParser.Parse(e.Data, true, "[ERR] ", ErrorColor)
                : stderrParser.Parse(e.Data, false, null, null));
        };
        process.Exited += (_, _) => OnProcessExited(service, process);

        AppendDirect(service, new LogLine($"[INFO] Starting ({ShellLabel(service.Shell)}): {service.Command}", false));

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            AppendDirect(service, new LogLine($"[ERR] Failed to start: {ex.Message}", true));
            process.Dispose();
            return;
        }

        JobObject? job = null;
        try
        {
            job = new JobObject();
            if (!job.TryAssign(process))
            {
                job.Dispose();
                job = null;
            }
        }
        catch
        {
            job?.Dispose();
            job = null;
        }
        if (job is null)
        {
            AppendDirect(service, new LogLine("[INFO] Could not attach a job object; falling back to process-tree kill.", false));
        }
        process.Exited += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => job?.Dispose());

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        service.RuntimeJob = job;
        service.RuntimeProcess = process;
        service.Status = ServiceStatus.Running;
        _runningServices.Add(service);
        RunningCountChanged?.Invoke(_runningServices.Count);
    }

    public void Stop(Service service)
    {
        if (service.Status != ServiceStatus.Running || service.RuntimeProcess is null) return;

        var process = service.RuntimeProcess;
        if (service.RuntimeJob is { } job)
        {
            job.Terminate();
            job.Dispose();
            service.RuntimeJob = null;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // process may have already raced to exit, or the job already killed it
        }

        service.Status = ServiceStatus.Stopped;
        RemoveFromRunning(service);
    }

    public void StopAll(IEnumerable<Project> projects)
    {
        foreach (var project in projects)
        {
            foreach (var service in project.Services)
            {
                if (service.Status == ServiceStatus.Running)
                {
                    Stop(service);
                }
            }
        }
    }

    public void ClearLogs(Service service)
    {
        service.LogBuffer.Clear();
        while (service.PendingLines.TryDequeue(out _))
        {
            // discard anything still mid-flight
        }
    }

    private static ProcessStartInfo BuildStartInfo(Service service)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = service.DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        // Python block-buffers stdout when piped, which would delay logs; also force UTF-8 output.
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        // Output is piped, not a terminal, so most CLIs would drop colors. Ask them to keep them;
        // the log view interprets the ANSI color codes.
        startInfo.Environment["FORCE_COLOR"] = "1";
        startInfo.Environment["npm_config_color"] = "always";
        startInfo.Environment["CLICOLOR_FORCE"] = "1";
        startInfo.Environment.Remove("NO_COLOR");

        if (service.Shell == ShellKind.PowerShell)
        {
            startInfo.FileName = PowerShellExe.Value;
            var script =
                "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                "$OutputEncoding = [System.Text.Encoding]::UTF8; " +
                service.Command;
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" })
            {
                startInfo.ArgumentList.Add(arg);
            }
            // EncodedCommand sidesteps all command-line quoting issues with the user's command.
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        }
        else
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(service.Command);
        }

        return startInfo;
    }

    private static string ResolvePowerShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "pwsh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // malformed PATH entry
            }
        }
        return "powershell.exe";
    }

    private static string ShellLabel(ShellKind shell) =>
        shell == ShellKind.PowerShell ? Path.GetFileNameWithoutExtension(PowerShellExe.Value) : "cmd";

    private static readonly System.Windows.Media.Color ErrorColor = System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28);

    [GeneratedRegex(@"\b(?:error|exception|traceback|fatal|failed|failure|critical|panic(?:ked)?)\b|\bERR!", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorPattern();

    private void OnProcessExited(Service service, Process process)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            DrainInto(service);
            int exitCode;
            try
            {
                exitCode = process.ExitCode;
            }
            catch
            {
                exitCode = -1;
            }

            // A stale exit from an earlier run must not flip the state of a newer run.
            if (ReferenceEquals(service.RuntimeProcess, process))
            {
                AppendDirect(service, new LogLine($"[INFO] Process exited (code {exitCode}).", exitCode != 0 && service.Status == ServiceStatus.Running));
                service.Status = ServiceStatus.Stopped;
                service.RuntimeProcess = null;
                service.RuntimeJob = null;
                RemoveFromRunning(service);
            }
            process.Dispose();
        });
    }

    private void RemoveFromRunning(Service service)
    {
        if (_runningServices.Remove(service))
        {
            RunningCountChanged?.Invoke(_runningServices.Count);
        }
    }

    private static void AppendDirect(Service service, LogLine line)
    {
        service.LogBuffer.Add(line);
        TrimRingBuffer(service.LogBuffer);
    }

    /// <summary>Called on stdout/stderr reader threads. Batches lines and arms the flush timer at most once per batch.</summary>
    private void Enqueue(Service service, LogLine line)
    {
        service.PendingLines.Enqueue(line);
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            _flushTimer.Dispatcher.BeginInvoke(_flushTimer.Start, DispatcherPriority.Background);
        }
    }

    private void FlushPendingLines()
    {
        foreach (var service in _runningServices.ToArray())
        {
            DrainInto(service);
        }
    }

    private static void DrainInto(Service service)
    {
        var any = false;
        while (service.PendingLines.TryDequeue(out var line))
        {
            service.LogBuffer.Add(line);
            any = true;
        }
        if (any)
        {
            TrimRingBuffer(service.LogBuffer);
        }
    }

    private static void TrimRingBuffer(ObservableCollection<LogLine> buffer)
    {
        while (buffer.Count > MaxLogLines)
        {
            buffer.RemoveAt(0);
        }
    }
}

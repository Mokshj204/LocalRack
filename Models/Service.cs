using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LocalRack.Models;

public enum ServiceStatus
{
    Stopped,
    Running
}

public enum ShellKind
{
    Cmd,
    PowerShell
}

public sealed class Service : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    private string _directoryPath = string.Empty;
    public string DirectoryPath
    {
        get => _directoryPath;
        set => SetField(ref _directoryPath, value);
    }

    private string _command = string.Empty;
    public string Command
    {
        get => _command;
        set => SetField(ref _command, value);
    }

    private ShellKind _shell = ShellKind.Cmd;
    [JsonConverter(typeof(JsonStringEnumConverter<ShellKind>))]
    public ShellKind Shell
    {
        get => _shell;
        set => SetField(ref _shell, value);
    }

    [JsonIgnore]
    public ObservableCollection<LogLine> LogBuffer { get; } = new();

    [JsonIgnore]
    internal ConcurrentQueue<LogLine> PendingLines { get; } = new();

    [JsonIgnore]
    internal Process? RuntimeProcess { get; set; }

    [JsonIgnore]
    internal Services.JobObject? RuntimeJob { get; set; }

    private ServiceStatus _status = ServiceStatus.Stopped;

    [JsonIgnore]
    public ServiceStatus Status
    {
        get => _status;
        internal set => SetField(ref _status, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

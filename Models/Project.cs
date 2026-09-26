using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LocalRack.Models;

public sealed class Project : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public ObservableCollection<Service> Services { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
}

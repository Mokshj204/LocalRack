using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalRack.Dialogs;
using LocalRack.Models;

namespace LocalRack.Views;

public partial class HomeView : UserControl
{
    private readonly ObservableCollection<Project> _projects;
    private readonly ObservableCollection<ProjectCardViewModel> _cards = new();

    public event Action<Project>? OpenProjectRequested;
    public event Action<Project>? ProjectDeleting;
    public event Action? ProjectsChanged;
    public event Action? SettingsRequested;

    public event Action<Project>? StartAllRequested;
    public event Action<Project>? StopAllRequested;

    private void StartAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) StartAllRequested?.Invoke(card.Project);
    }

    private void StopAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) StopAllRequested?.Invoke(card.Project);
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    public HomeView(ObservableCollection<Project> projects)
    {
        InitializeComponent();
        _projects = projects;
        foreach (var project in _projects)
        {
            _cards.Add(new ProjectCardViewModel(project));
        }
        ProjectsListBox.ItemsSource = _cards;
        UpdateEmptyState();
    }

    public void RefreshCounts()
    {
        foreach (var card in _cards)
        {
            card.Refresh();
        }
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        var project = new Project { Name = dialog.ProjectName };
        _projects.Add(project);
        _cards.Add(new ProjectCardViewModel(project));
        UpdateEmptyState();
        ProjectsChanged?.Invoke();
    }

    private void Open(ProjectCardViewModel card) => OpenProjectRequested?.Invoke(card.Project);

    private void Rename(ProjectCardViewModel card)
    {
        var dialog = new ProjectDialog(card.Project.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        card.Project.Name = dialog.ProjectName;
        ProjectsChanged?.Invoke();
    }

    private void Delete(ProjectCardViewModel card)
    {
        var result = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Delete project '{card.Project.Name}' and all its services? Any running services will be stopped.",
            "Delete Project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        ProjectDeleting?.Invoke(card.Project);
        _projects.Remove(card.Project);
        _cards.Remove(card);
        UpdateEmptyState();
        ProjectsChanged?.Invoke();
    }

    private static ProjectCardViewModel? CardOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as ProjectCardViewModel;

    private void ProjectCard_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CardOf(sender) is { } card) Open(card);
    }

    private void OpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Open(card);
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Rename(card);
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Delete(card);
    }

    private void OpenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Open(card);
    }

    private void RenameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Rename(card);
    }

    private void DeleteMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) Delete(card);
    }
}

public sealed class ProjectCardViewModel : INotifyPropertyChanged
{
    public Project Project { get; }

    public ProjectCardViewModel(Project project)
    {
        Project = project;
        foreach (var service in project.Services) service.PropertyChanged += OnServiceChanged;
        project.Services.CollectionChanged += OnServicesChanged;
        Refresh();
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Service.Status)) Refresh();
    }

    private void OnServicesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (Service s in e.OldItems) s.PropertyChanged -= OnServiceChanged;
        if (e.NewItems is not null)
            foreach (Service s in e.NewItems) s.PropertyChanged += OnServiceChanged;
        Refresh();
    }

    public bool CanStartAll => RunningCount < ServiceCount;
    public bool CanStopAll => RunningCount > 0;

    private int _serviceCount;
    public int ServiceCount
    {
        get => _serviceCount;
        private set
        {
            if (_serviceCount == value) return;
            _serviceCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ServiceCount)));
        }
    }

    private int _runningCount;
    public int RunningCount
    {
        get => _runningCount;
        private set
        {
            if (_runningCount == value) return;
            _runningCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningCount)));
        }
    }

    public void Refresh()
    {
        ServiceCount = Project.Services.Count;
        RunningCount = Project.Services.Count(s => s.Status == ServiceStatus.Running);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStartAll)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanStopAll)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

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
    private readonly ObservableCollection<ProjectGroup> _groups;
    private readonly ObservableCollection<ProjectCardViewModel> _cards = new();
    private readonly ObservableCollection<GroupCardViewModel> _groupCards = new();

    public event Action<Project>? OpenProjectRequested;
    public event Action<Project>? ProjectDeleting;
    public event Action? ProjectsChanged;
    public event Action? SettingsRequested;

    public event Action<Project>? StartAllRequested;
    public event Action<Project>? StopAllRequested;

    public event Action? GroupsChanged;
    public event Action<ProjectGroup>? GroupStartRequested;
    public event Action<ProjectGroup>? GroupStopRequested;
    public event Action<ProjectGroup>? GroupDeleting;

    private void StartAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) StartAllRequested?.Invoke(card.Project);
    }

    private void StopAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card) StopAllRequested?.Invoke(card.Project);
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    public HomeView(ObservableCollection<Project> projects, ObservableCollection<ProjectGroup> groups)
    {
        InitializeComponent();
        _projects = projects;
        _groups = groups;

        // Pinned items always lead; this also tidies lists saved before pinning existed.
        Ordering.SortPinnedFirst(_projects, p => p.IsPinned);
        Ordering.SortPinnedFirst(_groups, g => g.IsPinned);

        foreach (var project in _projects)
        {
            _cards.Add(new ProjectCardViewModel(project));
        }
        foreach (var group in _groups)
        {
            _groupCards.Add(new GroupCardViewModel(group, () => _projects));
        }
        ProjectsListBox.ItemsSource = _cards;
        GroupsListBox.ItemsSource = _groupCards;
        RefreshMoveState();
        UpdateMode();
    }

    /// <summary>Re-reads running state for every card (also picks up renamed projects in group summaries).</summary>
    public void RefreshCounts()
    {
        foreach (var card in _cards)
        {
            card.Refresh();
        }
        RefreshGroups();
    }

    public void RefreshGroups()
    {
        foreach (var card in _groupCards)
        {
            card.Refresh();
        }
    }

    private bool GroupsMode => GroupsModeRadio.IsChecked == true;

    private void ModeRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        UpdateMode();
    }

    private void UpdateMode()
    {
        // Radios raise Checked while InitializeComponent runs, before every element exists.
        if (GroupsListBox is null || EmptyText is null || AddButton is null) return;

        var groups = GroupsMode;
        ProjectsListBox.Visibility = groups ? Visibility.Collapsed : Visibility.Visible;
        GroupsListBox.Visibility = groups ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Content = groups ? "Add Group" : "Add Project";
        SubtitleText.Text = groups
            ? "Start several projects together, in the order you choose."
            : "Click a project to open it in a tab.";
        UpdateEmptyState();
        RefreshGroups();
    }

    private void UpdateEmptyState()
    {
        if (GroupsMode)
        {
            EmptyText.Text = _projects.Count == 0
                ? "Add a project first, then group projects here to start them together."
                : "No groups yet. Use \"Add Group\" to start several projects in one click.";
            EmptyText.Visibility = _groupCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            EmptyText.Text = "No projects yet. Use \"Add Project\" to create one.";
            EmptyText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshMoveState()
    {
        for (var i = 0; i < _cards.Count; i++)
        {
            _cards[i].SetMoveState(Ordering.CanMove(_cards, i, -1, c => c.Project.IsPinned),
                Ordering.CanMove(_cards, i, +1, c => c.Project.IsPinned));
        }
        for (var i = 0; i < _groupCards.Count; i++)
        {
            _groupCards[i].SetMoveState(Ordering.CanMove(_groupCards, i, -1, c => c.Group.IsPinned),
                Ordering.CanMove(_groupCards, i, +1, c => c.Group.IsPinned));
        }
    }

    // ----- project ordering and pinning -----

    private void MoveProject(ProjectCardViewModel card, int delta)
    {
        var index = _cards.IndexOf(card);
        if (!Ordering.CanMove(_cards, index, delta, c => c.Project.IsPinned)) return;
        _cards.Move(index, index + delta);
        _projects.Move(index, index + delta);
        RefreshMoveState();
        ProjectsChanged?.Invoke();
    }

    private void TogglePin(ProjectCardViewModel card)
    {
        card.Project.IsPinned = !card.Project.IsPinned;
        Ordering.SortPinnedFirst(_projects, p => p.IsPinned);
        Ordering.SortPinnedFirst(_cards, c => c.Project.IsPinned);
        RefreshMoveState();
        ProjectsChanged?.Invoke();
    }

    // ----- groups -----

    private void AddGroup()
    {
        if (_projects.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this)!, "Add at least one project before creating a group.",
                "Add Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new GroupDialog(_projects) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        var group = new ProjectGroup { Name = dialog.GroupName, Members = dialog.Members };
        _groups.Add(group);
        _groupCards.Add(new GroupCardViewModel(group, () => _projects));
        Ordering.SortPinnedFirst(_groups, g => g.IsPinned);
        Ordering.SortPinnedFirst(_groupCards, c => c.Group.IsPinned);
        RefreshMoveState();
        UpdateEmptyState();
        GroupsChanged?.Invoke();
    }

    private void EditGroup(GroupCardViewModel card)
    {
        var dialog = new GroupDialog(_projects, card.Group) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        card.Group.Name = dialog.GroupName;
        card.Group.Members = dialog.Members;
        card.Refresh();
        GroupsChanged?.Invoke();
    }

    private void DeleteGroup(GroupCardViewModel card)
    {
        var result = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Delete group '{card.Group.Name}'? Its projects and their services are not affected.",
            "Delete Group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        GroupDeleting?.Invoke(card.Group);
        _groups.Remove(card.Group);
        _groupCards.Remove(card);
        RefreshMoveState();
        UpdateEmptyState();
        GroupsChanged?.Invoke();
    }

    private void MoveGroup(GroupCardViewModel card, int delta)
    {
        var index = _groupCards.IndexOf(card);
        if (!Ordering.CanMove(_groupCards, index, delta, c => c.Group.IsPinned)) return;
        _groupCards.Move(index, index + delta);
        _groups.Move(index, index + delta);
        RefreshMoveState();
        GroupsChanged?.Invoke();
    }

    private void ToggleGroupPin(GroupCardViewModel card)
    {
        card.Group.IsPinned = !card.Group.IsPinned;
        Ordering.SortPinnedFirst(_groups, g => g.IsPinned);
        Ordering.SortPinnedFirst(_groupCards, c => c.Group.IsPinned);
        RefreshMoveState();
        GroupsChanged?.Invoke();
    }

    private static GroupCardViewModel? GroupOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as GroupCardViewModel;

    private void GroupStartButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) GroupStartRequested?.Invoke(g.Group); }
    private void GroupStopButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) GroupStopRequested?.Invoke(g.Group); }
    private void GroupEditButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) EditGroup(g); }
    private void GroupPinButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) ToggleGroupPin(g); }
    private void GroupMoveUpButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) MoveGroup(g, -1); }
    private void GroupMoveDownButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) MoveGroup(g, +1); }
    private void GroupDeleteButton_OnClick(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) DeleteGroup(g); }
    private void GroupStartMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupStartButton_OnClick(sender, e);
    private void GroupStopMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupStopButton_OnClick(sender, e);
    private void GroupEditMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupEditButton_OnClick(sender, e);
    private void GroupPinMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupPinButton_OnClick(sender, e);
    private void GroupMoveUpMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupMoveUpButton_OnClick(sender, e);
    private void GroupMoveDownMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupMoveDownButton_OnClick(sender, e);
    private void GroupDeleteMenuItem_OnClick(object sender, RoutedEventArgs e) => GroupDeleteButton_OnClick(sender, e);

    private void PinButton_OnClick(object sender, RoutedEventArgs e) { if (CardOf(sender) is { } c) TogglePin(c); }
    private void MoveUpButton_OnClick(object sender, RoutedEventArgs e) { if (CardOf(sender) is { } c) MoveProject(c, -1); }
    private void MoveDownButton_OnClick(object sender, RoutedEventArgs e) { if (CardOf(sender) is { } c) MoveProject(c, +1); }
    private void PinMenuItem_OnClick(object sender, RoutedEventArgs e) => PinButton_OnClick(sender, e);
    private void MoveUpMenuItem_OnClick(object sender, RoutedEventArgs e) => MoveUpButton_OnClick(sender, e);
    private void MoveDownMenuItem_OnClick(object sender, RoutedEventArgs e) => MoveDownButton_OnClick(sender, e);

    private void AddButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (GroupsMode) AddGroup();
        else AddProject();
    }

    private void AddProject()
    {
        var dialog = new ProjectDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        var project = new Project { Name = dialog.ProjectName };
        _projects.Add(project);
        _cards.Add(new ProjectCardViewModel(project));
        Ordering.SortPinnedFirst(_projects, p => p.IsPinned);
        Ordering.SortPinnedFirst(_cards, c => c.Project.IsPinned);
        RefreshMoveState();
        UpdateEmptyState();
        ProjectsChanged?.Invoke();
    }

    private void Open(ProjectCardViewModel card) => OpenProjectRequested?.Invoke(card.Project);

    private void Rename(ProjectCardViewModel card)
    {
        var dialog = new ProjectDialog(card.Project.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        card.Project.Name = dialog.ProjectName;
        RefreshGroups();
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
        RefreshMoveState();
        UpdateEmptyState();
        RefreshGroups();
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
        project.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Project.IsPinned))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinLabel)));
            }
        };
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

    public bool IsPinned => Project.IsPinned;
    public string PinLabel => Project.IsPinned ? "Unpin" : "Pin";

    private bool _canMoveUp;
    public bool CanMoveUp => _canMoveUp;
    private bool _canMoveDown;
    public bool CanMoveDown => _canMoveDown;

    public void SetMoveState(bool up, bool down)
    {
        _canMoveUp = up;
        _canMoveDown = down;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMoveUp)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanMoveDown)));
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

public sealed class GroupCardViewModel : INotifyPropertyChanged
{
    private readonly Func<IReadOnlyList<Project>> _projects;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public ProjectGroup Group { get; }

    public GroupCardViewModel(ProjectGroup group, Func<IReadOnlyList<Project>> projects)
    {
        Group = group;
        _projects = projects;
        group.PropertyChanged += (_, _) => Refresh();
    }

    public string Name => Group.Name;
    public bool IsPinned => Group.IsPinned;
    public string PinLabel => Group.IsPinned ? "Unpin" : "Pin";
    public string? Progress => Group.Progress;
    public bool HasProgress => Group.Progress is not null;
    public bool CanMoveUp => _canMoveUp;
    public bool CanMoveDown => _canMoveDown;

    private List<(GroupMember Member, Project Project, Service Service)> Resolved() => Group.Resolve(_projects());

    public int RunningServiceCount => Resolved().Count(r => r.Service.Status == ServiceStatus.Running);
    public string RunningLabel => $"{RunningServiceCount} of {Resolved().Count} running";

    public bool CanStart => !HasProgress && Resolved().Any(r => r.Service.Status != ServiceStatus.Running);
    public bool CanStop => HasProgress || RunningServiceCount > 0;

    /// <summary>"api (+10s) → web → worker": services in start order, with any wait before the next one.</summary>
    public string Summary
    {
        get
        {
            if (Group.Members.Count == 0) return "No services in this group.";
            var projects = _projects();
            var parts = Group.Members.Select((m, i) =>
            {
                var project = projects.FirstOrDefault(p => p.Id == m.ProjectId);
                var service = project?.Services.FirstOrDefault(s => s.Id == m.ServiceId);
                var name = service is null ? "(deleted service)" : $"{project!.Name} › {service.Name}";
                return m.DelaySeconds > 0 && i < Group.Members.Count - 1 ? $"{name} (+{m.DelaySeconds}s)" : name;
            });
            return string.Join("  →  ", parts);
        }
    }

    public void SetMoveState(bool up, bool down)
    {
        _canMoveUp = up;
        _canMoveDown = down;
        Refresh();
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Reordering rules shared by the project and group lists: pinned items always sit above unpinned ones.</summary>
internal static class Ordering
{
    public static void SortPinnedFirst<T>(ObservableCollection<T> list, Func<T, bool> isPinned)
    {
        var ordered = list.Where(isPinned).Concat(list.Where(item => !isPinned(item))).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = list.IndexOf(ordered[i]);
            if (current != i) list.Move(current, i);
        }
    }

    /// <summary>An item can only swap with a neighbour on the same side of the pinned divider.</summary>
    public static bool CanMove<T>(IList<T> list, int index, int delta, Func<T, bool> isPinned)
    {
        var target = index + delta;
        return index >= 0 && target >= 0 && target < list.Count && isPinned(list[index]) == isPinned(list[target]);
    }
}

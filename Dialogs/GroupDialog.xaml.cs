using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using LocalRack.Models;

namespace LocalRack.Dialogs;

public partial class GroupDialog : Window
{
    private readonly IReadOnlyList<Project> _allProjects;
    private readonly ObservableCollection<ProjectRow> _rows = new();

    public string GroupName { get; private set; } = string.Empty;

    /// <summary>The chosen services, flattened: projects top to bottom, each project's services top to bottom.</summary>
    public List<GroupMember> Members { get; private set; } = new();

    public GroupDialog(IReadOnlyList<Project> allProjects, ProjectGroup? existing = null)
    {
        InitializeComponent();
        _allProjects = allProjects;

        var isEdit = existing is not null;
        Title = isEdit ? "Edit Group" : "Add Group";
        OkButton.Content = isEdit ? "Save" : "Create";
        NameTextBox.Text = existing?.Name ?? string.Empty;

        if (existing is not null)
        {
            LoadExisting(existing);
        }

        ProjectsItemsControl.ItemsSource = _rows;
        _rows.CollectionChanged += (_, _) => Refresh();
        Refresh();

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    /// <summary>
    /// Projects appear in the order they first show up in the group. Inside a project the group's services come
    /// first, in their saved order, followed by the project's other services as unticked rows.
    /// </summary>
    private void LoadExisting(ProjectGroup group)
    {
        var resolved = group.Resolve(_allProjects);
        foreach (var project in resolved.Select(r => r.Project).Distinct())
        {
            var row = new ProjectRow(project);
            foreach (var (member, _, service) in resolved.Where(r => r.Project == project))
            {
                row.Services.Add(new ServiceRow(service, true, member.DelaySeconds));
            }
            foreach (var service in project.Services.Where(s => row.Services.All(r => r.Service.Id != s.Id)))
            {
                row.Services.Add(new ServiceRow(service, false, 0));
            }
            row.Renumber();
            _rows.Add(row);
        }
    }

    private void Refresh()
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].Position = i + 1;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var available = _allProjects.Where(p => _rows.All(r => r.Project.Id != p.Id)).ToList();
        AddProjectComboBox.ItemsSource = available;
        AddProjectComboBox.SelectedIndex = available.Count > 0 ? 0 : -1;
        AddProjectComboBox.IsEnabled = AddButton.IsEnabled = available.Count > 0;
    }

    private static T? DataOf<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    private void AddButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AddProjectComboBox.SelectedItem is not Project project) return;

        // A new project brings all its services, in the project's own order, and opens so they are visible.
        var row = new ProjectRow(project) { IsExpanded = true };
        foreach (var service in project.Services) row.Services.Add(new ServiceRow(service, true, 0));
        row.Renumber();
        _rows.Add(row);
    }

    private void ProjectRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataOf<ProjectRow>(sender) is { } row) _rows.Remove(row);
    }

    private void ProjectUpButton_OnClick(object sender, RoutedEventArgs e) => MoveProject(DataOf<ProjectRow>(sender), -1);

    private void ProjectDownButton_OnClick(object sender, RoutedEventArgs e) => MoveProject(DataOf<ProjectRow>(sender), 1);

    private void MoveProject(ProjectRow? row, int offset)
    {
        if (row is null) return;
        var index = _rows.IndexOf(row);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _rows.Count) return;
        _rows.Move(index, target);
    }

    private void ServiceUpButton_OnClick(object sender, RoutedEventArgs e) => MoveService(DataOf<ServiceRow>(sender), -1);

    private void ServiceDownButton_OnClick(object sender, RoutedEventArgs e) => MoveService(DataOf<ServiceRow>(sender), 1);

    private void MoveService(ServiceRow? service, int offset)
    {
        var project = _rows.FirstOrDefault(r => service is not null && r.Services.Contains(service));
        if (project is null || service is null) return;
        var index = project.Services.IndexOf(service);
        var target = index + offset;
        if (target < 0 || target >= project.Services.Count) return;
        project.Services.Move(index, target);
        project.Renumber();
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Group name cannot be empty.");
            return;
        }

        var members = new List<GroupMember>();
        foreach (var project in _rows)
        {
            foreach (var service in project.Services.Where(s => s.IsIncluded))
            {
                if (!service.TryGetDelay(out var delay))
                {
                    project.IsExpanded = true;
                    ShowError($"The wait for '{service.Name}' must be a whole number from 0 to {ProjectGroup.MaxDelaySeconds} seconds.");
                    return;
                }
                members.Add(new GroupMember { ProjectId = project.Project.Id, ServiceId = service.Service.Id, DelaySeconds = delay });
            }
        }

        if (members.Count == 0)
        {
            ShowError("Add at least one project with a ticked service.");
            return;
        }

        GroupName = name;
        Members = members;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private sealed class ProjectRow : INotifyPropertyChanged
    {
        public ProjectRow(Project project)
        {
            Project = project;
            Services.CollectionChanged += (_, _) => Raise(nameof(Summary));
        }

        public Project Project { get; }
        public string Name => Project.Name;
        public ObservableCollection<ServiceRow> Services { get; } = new();

        public string Summary
        {
            get
            {
                var included = Services.Count(s => s.IsIncluded);
                return $"{included} of {Services.Count} service{(Services.Count == 1 ? "" : "s")}";
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                Raise(nameof(IsExpanded));
                Raise(nameof(ServicesVisibility));
            }
        }

        public Visibility ServicesVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        private int _position;
        public int Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                _position = value;
                Raise(nameof(Position));
            }
        }

        /// <summary>Renumbers the services and lets the summary follow ticks.</summary>
        public void Renumber()
        {
            for (var i = 0; i < Services.Count; i++)
            {
                Services[i].Position = i + 1;
                Services[i].Owner = this;
            }
            Raise(nameof(Summary));
        }

        public void RaiseSummary() => Raise(nameof(Summary));

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class ServiceRow : INotifyPropertyChanged
    {
        public ServiceRow(Service service, bool isIncluded, int delaySeconds)
        {
            Service = service;
            _isIncluded = isIncluded;
            DelayText = delaySeconds.ToString();
        }

        public Service Service { get; }
        public string Name => Service.Name;
        public string DelayText { get; set; }
        internal ProjectRow? Owner { get; set; }

        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value) return;
                _isIncluded = value;
                Raise(nameof(IsIncluded));
                Raise(nameof(Opacity));
                Owner?.RaiseSummary();
            }
        }

        public double Opacity => IsIncluded ? 1.0 : 0.5;

        private int _position;
        public int Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                _position = value;
                Raise(nameof(Position));
            }
        }

        public bool TryGetDelay(out int delay) =>
            int.TryParse(DelayText.Trim(), out delay) && delay is >= 0 and <= ProjectGroup.MaxDelaySeconds;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

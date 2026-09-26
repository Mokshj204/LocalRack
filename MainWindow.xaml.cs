using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalRack.Models;
using LocalRack.Services;
using LocalRack.Views;

namespace LocalRack;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Project> _projects;
    private readonly ProjectRepository _projectRepository = new();
    private readonly UiStateRepository _uiStateRepository = new();
    private readonly ServiceProcessManager _processManager = new();
    private readonly HomeView _homeView;
    private readonly Dictionary<Guid, TabItem> _openTabs = new();
    private TabItem? _settingsTab;
    private TrayIcon? _trayIcon;
    private bool _exitRequested;
    private bool _shutDown;

    public MainWindow()
    {
        InitializeComponent();

        _projects = _projectRepository.Load();

        _homeView = new HomeView(_projects);
        _homeView.OpenProjectRequested += project => OpenOrFocusTab(project, selectAfterOpen: true);
        _homeView.ProjectDeleting += OnProjectDeleting;
        _homeView.ProjectsChanged += SaveProjects;
        _homeView.SettingsRequested += OpenSettings;
        HomeTabItem.Content = _homeView;

        foreach (var id in _uiStateRepository.Load().OpenProjectIds)
        {
            var project = _projects.FirstOrDefault(p => p.Id == id);
            if (project is not null)
            {
                OpenOrFocusTab(project, selectAfterOpen: false);
            }
        }

        RootTabControl.SelectedItem = HomeTabItem;

        _processManager.RunningCountChanged += count => _trayIcon?.SetRunningCount(count);
        SettingsStore.Changed += ApplyTraySetting;
        ApplyTraySetting();
        StateChanged += OnStateChanged;
        // Signing out or shutting down must really exit, not hide to the tray.
        Application.Current.SessionEnding += (_, _) => _exitRequested = true;
    }

    private void ApplyTraySetting()
    {
        if (SettingsStore.Current.KeepRunningInTray)
        {
            if (_trayIcon is not null) return;
            _trayIcon = new TrayIcon();
            _trayIcon.OpenRequested += RestoreFromTray;
            _trayIcon.ExitRequested += ExitApplication;
            _trayIcon.SetRunningCount(_processManager.RunningCount);
        }
        else if (_trayIcon is not null)
        {
            _trayIcon.Dispose();
            _trayIcon = null;
            if (!IsVisible) RestoreFromTray();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _trayIcon is not null)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        Hide();
        _trayIcon?.NotifyHiddenOnce();
    }

    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        ShutDownApplication();
    }

    /// <summary>Stops every service, saves state and exits. Safe to call more than once.</summary>
    private void ShutDownApplication()
    {
        if (_shutDown) return;
        _shutDown = true;

        _processManager.StopAll(_projects);
        SaveProjects();
        SaveUiState();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Application.Current.Shutdown();
    }

    private void OpenOrFocusTab(Project project, bool selectAfterOpen)
    {
        if (_openTabs.TryGetValue(project.Id, out var existingTab))
        {
            if (selectAfterOpen) RootTabControl.SelectedItem = existingTab;
            return;
        }

        var tabView = new ProjectTabView(project, _processManager);
        tabView.ProjectChanged += SaveProjects;

        var tabItem = new TabItem
        {
            Tag = project.Id,
            Style = (Style)FindResource("TabItemStyle"),
            Header = BuildTabHeader(BuildProjectTitle(project), () => CloseTab(project)),
            Content = tabView
        };

        _openTabs[project.Id] = tabItem;
        RootTabControl.Items.Add(tabItem);

        if (selectAfterOpen)
        {
            RootTabControl.SelectedItem = tabItem;
        }

        SaveUiState();
    }

    private void OpenSettings()
    {
        if (_settingsTab is null)
        {
            var title = new TextBlock { Text = "Settings", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _settingsTab = new TabItem
            {
                Style = (Style)FindResource("TabItemStyle"),
                Content = new SettingsView()
            };
            _settingsTab.Header = BuildTabHeader(title, () =>
            {
                RootTabControl.Items.Remove(_settingsTab);
                _settingsTab = null;
                RootTabControl.SelectedItem ??= HomeTabItem;
            });
            RootTabControl.Items.Add(_settingsTab);
        }
        else
        {
            ((SettingsView)_settingsTab.Content).Refresh();
        }
        RootTabControl.SelectedItem = _settingsTab;
    }

    private static TextBlock BuildProjectTitle(Project project)
    {
        var title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Project.Name)) { Source = project });
        return title;
    }

    private UIElement BuildTabHeader(UIElement title, Action onClose)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(title);

        var closeButton = new Button
        {
            Style = (Style)FindResource("ButtonStyle"),
            Content = "✕",
            ToolTip = "Close tab",
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeButton.Click += (_, _) => onClose();
        panel.Children.Add(closeButton);
        return panel;
    }

    private void CloseTab(Project project)
    {
        if (!_openTabs.Remove(project.Id, out var tabItem)) return;

        RootTabControl.Items.Remove(tabItem);
        if (RootTabControl.SelectedItem is null)
        {
            RootTabControl.SelectedItem = HomeTabItem;
        }

        SaveUiState();
    }

    private void OnProjectDeleting(Project project)
    {
        foreach (var service in project.Services)
        {
            _processManager.Stop(service);
        }
        CloseTab(project);
    }

    private void RootTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == RootTabControl && RootTabControl.SelectedItem == HomeTabItem)
        {
            _homeView.RefreshCounts();
        }
    }

    private void SaveProjects()
    {
        _projectRepository.Save(_projects);
    }

    private void SaveUiState()
    {
        var ids = RootTabControl.Items
            .OfType<TabItem>()
            .Where(t => t.Tag is Guid)
            .Select(t => (Guid)t.Tag)
            .ToList();
        _uiStateRepository.Save(new UiState { OpenProjectIds = ids });
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _trayIcon is not null)
        {
            // Tray mode: closing the window hides it; services keep running. Exit from the tray menu.
            e.Cancel = true;
            HideToTray();
            return;
        }

        ShutDownApplication();
    }
}

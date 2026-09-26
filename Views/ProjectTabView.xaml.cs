using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalRack.Dialogs;
using LocalRack.Models;
using LocalRack.Services;

namespace LocalRack.Views;

public partial class ProjectTabView : UserControl
{
    private readonly Project _project;
    private readonly ServiceProcessManager _processManager;
    private Service? _selectedService;
    private readonly Brush _errorBrush;

    public event Action? ProjectChanged;

    public ProjectTabView(Project project, ServiceProcessManager processManager)
    {
        InitializeComponent();
        _errorBrush = (Brush)FindResource("ErrorTextBrush");
        _project = project;
        _processManager = processManager;
        ServicesListBox.ItemsSource = _project.Services;
        _project.Services.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
        ShowService(null);
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _project.Services.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowService(Service? service)
    {
        if (_selectedService is not null)
        {
            _selectedService.LogBuffer.CollectionChanged -= LogBuffer_OnCollectionChanged;
        }

        _selectedService = service;
        LogHeader.DataContext = service;

        var hasService = service is not null;
        EditServiceButton.IsEnabled = hasService;
        DeleteServiceButton.IsEnabled = hasService;
        ClearLogsButton.IsEnabled = hasService;
        DetailsGrid.Visibility = hasService ? Visibility.Visible : Visibility.Collapsed;

        RebuildLog();
        if (service is not null)
        {
            service.LogBuffer.CollectionChanged += LogBuffer_OnCollectionChanged;
        }
        LogView.ScrollToEnd();
    }

    private void ServicesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowService(ServicesListBox.SelectedItem as Service);
    }

    // The document mirrors the service's ring buffer, which only ever appends at the end
    // and trims from the front, so incremental updates stay cheap.
    private void LogBuffer_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var wasAtBottom = LogView.VerticalOffset + LogView.ViewportHeight >= LogView.ExtentHeight - 2;
        var blocks = LogView.Document.Blocks;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (LogLine line in e.NewItems)
                {
                    blocks.Add(CreateParagraph(line));
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex == 0 && e.OldItems is not null:
                for (var i = 0; i < e.OldItems.Count && blocks.FirstBlock is not null; i++)
                {
                    blocks.Remove(blocks.FirstBlock);
                }
                break;
            default:
                RebuildLog();
                break;
        }

        // Follow new output only while already at the bottom, so reading or selecting older lines isn't interrupted.
        if (wasAtBottom)
        {
            LogView.ScrollToEnd();
        }
    }

    private void RebuildLog()
    {
        var blocks = LogView.Document.Blocks;
        blocks.Clear();
        if (_selectedService is null) return;

        foreach (var line in _selectedService.LogBuffer)
        {
            blocks.Add(CreateParagraph(line));
        }
    }

    private Paragraph CreateParagraph(LogLine line)
    {
        var paragraph = new Paragraph();
        if (line.IsError)
        {
            paragraph.Foreground = _errorBrush;
        }

        foreach (var segment in line.Segments)
        {
            var run = new Run(segment.Text);
            if (segment.Foreground is not null) run.Foreground = segment.Foreground;
            if (segment.Background is not null) run.Background = segment.Background;
            if (segment.Bold) run.FontWeight = FontWeights.Bold;
            if (segment.Italic) run.FontStyle = FontStyles.Italic;
            if (segment.Underline) run.TextDecorations = TextDecorations.Underline;
            paragraph.Inlines.Add(run);
        }
        return paragraph;
    }

    private void ServiceToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Service service) return;

        if (service.Status == ServiceStatus.Running)
        {
            _processManager.Stop(service);
        }
        else
        {
            _processManager.Start(service);
        }
    }

    private void AddServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ServiceDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        var service = new Service
        {
            Name = dialog.ServiceName,
            DirectoryPath = dialog.DirectoryPath,
            Command = dialog.Command,
            Shell = dialog.Shell
        };
        _project.Services.Add(service);
        ServicesListBox.SelectedItem = service;
        ProjectChanged?.Invoke();
    }

    private void EditService(Service service)
    {
        var dialog = new ServiceDialog(service) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        service.Name = dialog.ServiceName;
        service.DirectoryPath = dialog.DirectoryPath;
        service.Command = dialog.Command;
        service.Shell = dialog.Shell;
        ProjectChanged?.Invoke();
    }

    private void DeleteService(Service service)
    {
        var result = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Delete service '{service.Name}'? If it's running, it will be stopped first.",
            "Delete Service",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _processManager.Stop(service);
        if (ReferenceEquals(_selectedService, service))
        {
            ShowService(null);
        }
        _project.Services.Remove(service);
        ProjectChanged?.Invoke();
    }

    private void ServiceItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null) return;
        if (((FrameworkElement)sender).DataContext is Service service) EditService(service);
    }

    private void EditServiceMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Service service) EditService(service);
    }

    private void DeleteServiceMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Service service) DeleteService(service);
    }

    private void EditServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedService is not null) EditService(_selectedService);
    }

    private void DeleteServiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedService is not null) DeleteService(_selectedService);
    }

    private void ClearLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedService is not null) _processManager.ClearLogs(_selectedService);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}

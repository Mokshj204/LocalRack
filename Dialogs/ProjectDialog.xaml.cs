using System.Windows;

namespace LocalRack.Dialogs;

public partial class ProjectDialog : Window
{
    public string ProjectName { get; private set; } = string.Empty;

    public ProjectDialog(string? existingName = null)
    {
        InitializeComponent();
        var isEdit = existingName is not null;
        Title = isEdit ? "Rename Project" : "Add Project";
        OkButton.Content = isEdit ? "Save" : "Create";
        NameTextBox.Text = existingName ?? string.Empty;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorText.Text = "Project name cannot be empty.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ProjectName = name;
        DialogResult = true;
    }
}

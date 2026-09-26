using System.IO;
using System.Windows;
using Microsoft.Win32;
using LocalRack.Models;

namespace LocalRack.Dialogs;

public partial class ServiceDialog : Window
{
    public string ServiceName { get; private set; } = string.Empty;
    public string DirectoryPath { get; private set; } = string.Empty;
    public string Command { get; private set; } = string.Empty;
    public ShellKind Shell { get; private set; } = ShellKind.PowerShell;

    public ServiceDialog(Service? existing = null)
    {
        InitializeComponent();
        var isEdit = existing is not null;
        Title = isEdit ? "Edit Service" : "Add Service";
        OkButton.Content = isEdit ? "Save" : "Create";
        ShellComboBox.SelectedIndex = ShellToIndex(existing?.Shell ?? ShellKind.PowerShell);

        if (existing is not null)
        {
            NameTextBox.Text = existing.Name;
            DirectoryTextBox.Text = existing.DirectoryPath;
            CommandTextBox.Text = existing.Command;
            if (existing.Status == ServiceStatus.Running)
            {
                InfoText.Visibility = Visibility.Visible;
            }
        }

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select working directory" };
        if (Directory.Exists(DirectoryTextBox.Text))
        {
            dialog.InitialDirectory = DirectoryTextBox.Text;
        }
        if (dialog.ShowDialog(this) == true)
        {
            DirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var directory = DirectoryTextBox.Text.Trim();
        var command = CommandTextBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowError("Service name cannot be empty.");
            return;
        }
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            ShowError("Working directory must be an existing folder.");
            return;
        }
        if (string.IsNullOrEmpty(command))
        {
            ShowError("Command cannot be empty.");
            return;
        }

        ServiceName = name;
        DirectoryPath = directory;
        Command = command;
        Shell = IndexToShell(ShellComboBox.SelectedIndex);
        DialogResult = true;
    }

    private static int ShellToIndex(ShellKind shell) => shell == ShellKind.PowerShell ? 0 : 1;

    private static ShellKind IndexToShell(int index) => index == 1 ? ShellKind.Cmd : ShellKind.PowerShell;

    private void ShellComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CommandHintText.Text = IndexToShell(ShellComboBox.SelectedIndex) == ShellKind.PowerShell
            ? "PowerShell syntax: chain with ; — e.g. .\\venv\\Scripts\\Activate; uvicorn main:app --reload"
            : "cmd syntax: chain with && — e.g. venv\\Scripts\\activate && uvicorn main:app --reload";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

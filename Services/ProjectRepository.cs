using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using LocalRack.Models;

namespace LocalRack.Services;

public sealed class ProjectRepository
{
    private static readonly string ConfigDir = AppPaths.DataDirectory;
    private static readonly string ConfigPath = AppPaths.ProjectsFile;

    public ObservableCollection<Project> Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new ObservableCollection<Project>();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var list = JsonSerializer.Deserialize(json, AppJsonContext.Readable.ListProject);
            return new ObservableCollection<Project>(list ?? new List<Project>());
        }
        catch (Exception)
        {
            TryBackupCorruptFile();
            MessageBox.Show(
                "Your projects.json configuration could not be read and has been reset. A backup of the old file was saved next to it.",
                AppPaths.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return new ObservableCollection<Project>();
        }
    }

    public void Save(ObservableCollection<Project> projects)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(projects.ToList(), AppJsonContext.Readable.ListProject);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            var backupPath = Path.Combine(ConfigDir, $"projects.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backupPath, overwrite: true);
        }
        catch
        {
            // best-effort only
        }
    }
}

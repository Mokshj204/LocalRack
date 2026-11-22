using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using LocalRack.Models;

namespace LocalRack.Services;

public sealed class GroupRepository
{
    private static readonly string ConfigDir = AppPaths.DataDirectory;
    private static readonly string ConfigPath = AppPaths.GroupsFile;

    public ObservableCollection<ProjectGroup> Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new ObservableCollection<ProjectGroup>();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var list = JsonSerializer.Deserialize(json, AppJsonContext.Readable.ListProjectGroup);
            return new ObservableCollection<ProjectGroup>(list ?? new List<ProjectGroup>());
        }
        catch (Exception)
        {
            TryBackupCorruptFile();
            MessageBox.Show(
                "Your groups.json configuration could not be read and has been reset. A backup of the old file was saved next to it.",
                AppPaths.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return new ObservableCollection<ProjectGroup>();
        }
    }

    public void Save(ObservableCollection<ProjectGroup> groups)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(groups.ToList(), AppJsonContext.Readable.ListProjectGroup);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            var backupPath = Path.Combine(ConfigDir, $"groups.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backupPath, overwrite: true);
        }
        catch
        {
            // best-effort only
        }
    }
}

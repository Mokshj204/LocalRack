using System.IO;
using System.Text.Json;
using LocalRack.Models;

namespace LocalRack.Services;

public sealed class UiStateRepository
{
    private static readonly string ConfigDir = AppPaths.DataDirectory;
    private static readonly string ConfigPath = AppPaths.UiStateFile;

    public UiState Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new UiState();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var state = JsonSerializer.Deserialize(json, AppJsonContext.Readable.UiState);
            return state ?? new UiState();
        }
        catch (Exception)
        {
            TryBackupCorruptFile();
            return new UiState();
        }
    }

    public void Save(UiState state)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(state, AppJsonContext.Readable.UiState);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            var backupPath = Path.Combine(ConfigDir, $"ui-state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backupPath, overwrite: true);
        }
        catch
        {
            // best-effort only
        }
    }
}

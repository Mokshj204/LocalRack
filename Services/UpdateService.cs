using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalRack.Services;

/// <summary>A newer GitHub release than the running build.</summary>
public sealed record UpdateInfo(
    Version Version,
    string ReleaseUrl,
    string? InstallerName,
    string? InstallerUrl,
    string? ChecksumsUrl);

/// <summary>
/// Looks for a newer release on GitHub and downloads its installer. The installer is only
/// handed back after its SHA-256 matches the release's SHA256SUMS.txt.
/// </summary>
public static partial class UpdateService
{
    private const string Repo = "Mokshj204/LocalRack";
    private const string ChecksumsName = "SHA256SUMS.txt";
    private static readonly string DownloadPrefix = $"https://github.com/{Repo}/releases/download/";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    /// <summary>True when running from the installer's folder, so the installer can upgrade this copy in place.</summary>
    public static bool IsInstalledCopy()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return false;
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppPaths.AppName) + Path.DirectorySeparatorChar;
        return path.StartsWith(installDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the latest release if it is newer than this build, otherwise null. Throws on network errors.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var tagMatch = TagPattern().Match(tag);
        if (!tagMatch.Success || !Version.TryParse(tagMatch.Groups[1].Value, out var latest)) return null;
        if (latest <= CurrentVersion) return null;

        string? installerName = null, installerUrl = null, checksumsUrl = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var url = asset.GetProperty("browser_download_url").GetString();
                if (InstallerPattern().IsMatch(name)) (installerName, installerUrl) = (name, url);
                else if (name == ChecksumsName) checksumsUrl = url;
            }
        }

        var releaseUrl = root.TryGetProperty("html_url", out var html) && html.GetString() is { } h
            ? h
            : $"https://github.com/{Repo}/releases/latest";
        return new UpdateInfo(latest, releaseUrl, installerName, installerUrl, checksumsUrl);
    }

    /// <summary>Downloads the installer to a temp folder and verifies its checksum. Returns the verified file's path.</summary>
    public static async Task<string> DownloadInstallerAsync(
        UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (update.InstallerName is null || update.InstallerUrl is null || update.ChecksumsUrl is null)
        {
            throw new InvalidOperationException("This release has no installer or checksum file attached.");
        }
        // Only ever fetch from this repository's release assets, and only a file name we recognise.
        if (!update.InstallerUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal) ||
            !update.ChecksumsUrl.StartsWith(DownloadPrefix, StringComparison.Ordinal) ||
            !InstallerPattern().IsMatch(update.InstallerName))
        {
            throw new InvalidOperationException("The release points at an unexpected download location.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        var checksums = await Http.GetStringAsync(update.ChecksumsUrl, timeout.Token);
        var expected = FindChecksum(checksums, update.InstallerName)
                       ?? throw new InvalidDataException($"{ChecksumsName} has no entry for {update.InstallerName}.");

        var directory = Path.Combine(Path.GetTempPath(), "LocalRack-update");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, update.InstallerName);

        try
        {
            using var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    done += read;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }

            string actual;
            await using (var verify = File.OpenRead(target))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, timeout.Token));
            }
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded installer failed its SHA-256 check and was discarded.");
            }
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    private static string? FindChecksum(string checksums, string fileName)
    {
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<hex>  <name>" as written by the release workflow.
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1] == fileName && parts[0].Length == 64) return parts[0];
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // per-call timeouts above
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppPaths.AppName, "update-check"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+)$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^LocalRack-v\d+\.\d+\.\d+-win-x64-setup\.exe$")]
    private static partial Regex InstallerPattern();
}

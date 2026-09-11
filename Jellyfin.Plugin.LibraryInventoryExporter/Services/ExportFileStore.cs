using System.Text.Json;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class ExportFileStore
{
    public const string ExportPrefix = "jellyfin-inventory-";
    public const string DefaultDirectoryName = "inventory-exports";
    private readonly IServerApplicationPaths _paths;

    public ExportFileStore(IServerApplicationPaths paths)
    {
        _paths = paths;
    }

    public string ResolveOutputDirectory()
    {
        var configured = Plugin.Instance?.Configuration.OutputDirectory;
        var directory = string.IsNullOrWhiteSpace(configured)
            ? GetDefaultOutputDirectory()
            : configured;
        return Path.GetFullPath(directory);
    }

    public string GetDefaultOutputDirectory() => Path.GetFullPath(Path.Combine(_paths.DataPath, DefaultDirectoryName));

    /// <summary>
    /// Checks that Jellyfin can create files in the configured output directory, or in its parent when the directory does not exist yet.
    /// </summary>
    public bool CanWriteOutputDirectory(out string outputDirectory)
    {
        outputDirectory = ResolveOutputDirectory();
        return CanWriteToDirectory(outputDirectory);
    }

    public IReadOnlyList<OutputDirectoryOption> GetOutputDirectoryOptions()
    {
        var current = ResolveOutputDirectory();
        var options = new List<OutputDirectoryOption>
        {
            CreateOption(GetDefaultOutputDirectory(), "Jellyfin data directory", isDefault: true, current)
        };

        if (!string.Equals(current, GetDefaultOutputDirectory(), StringComparison.Ordinal))
        {
            options.Add(CreateOption(current, "Current setting", isDefault: false, current));
        }

        foreach (var candidate in GetCandidateOutputDirectories())
        {
            var fullPath = Path.GetFullPath(candidate.Path);
            if (options.Any(option => string.Equals(option.Path, fullPath, StringComparison.Ordinal)))
            {
                continue;
            }

            var option = CreateOption(fullPath, candidate.Label, isDefault: false, current);
            if (option.IsWritable)
            {
                options.Add(option);
            }
        }

        return options;
    }

    public string CreateExportId(DateTimeOffset generatedAt) => generatedAt.UtcDateTime.ToString("yyyy-MM-ddTHHmmssZ");

    public string GetExportDirectory(string exportId)
    {
        EnsureValidExportId(exportId);
        return Path.Combine(ResolveOutputDirectory(), ExportPrefix + exportId);
    }

    public string GetZipPath(string exportId)
    {
        EnsureValidExportId(exportId);
        return Path.Combine(ResolveOutputDirectory(), ExportPrefix + exportId + ".zip");
    }

    public async Task SaveHistoryAsync(ExportHistoryEntry entry, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ResolveOutputDirectory());
        var path = Path.Combine(ResolveOutputDirectory(), ExportPrefix + entry.Id + ".history.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ExportHistoryEntry> ListExports()
    {
        var directory = ResolveOutputDirectory();
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ExportHistoryEntry>();
        }

        return Directory.EnumerateFiles(directory, ExportPrefix + "*.history.json")
            .Select(ReadHistory)
            .Where(e => e is not null)
            .Cast<ExportHistoryEntry>()
            .OrderByDescending(e => e.GeneratedAt)
            .ToList();
    }

    public ExportHistoryEntry? GetLatest() => ListExports().FirstOrDefault();

    public bool ExportExists(string exportId) => File.Exists(GetZipPath(exportId));

    public FileStream OpenRead(string exportId)
    {
        var path = GetZipPath(exportId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Export archive not found.", path);
        }

        return File.OpenRead(path);
    }

    public bool DeleteExport(string exportId)
    {
        EnsureValidExportId(exportId);
        if (!Directory.Exists(ResolveOutputDirectory()))
        {
            return false;
        }

        var deleted = false;
        foreach (var path in Directory.EnumerateFiles(ResolveOutputDirectory(), ExportPrefix + exportId + "*"))
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(ResolveOutputDirectory(), StringComparison.Ordinal))
            {
                continue;
            }

            File.Delete(fullPath);
            deleted = true;
        }

        // Versions before 0.1.8 left the unzipped export next to the archive.
        var stagingDirectory = Path.Combine(ResolveOutputDirectory(), ExportPrefix + exportId);
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
            deleted = true;
        }

        return deleted;
    }

    public static bool IsValidExportId(string exportId)
        => !string.IsNullOrWhiteSpace(exportId) && exportId.All(c => char.IsDigit(c) || c == 'T' || c == 'Z' || c == '-');

    public static void EnsureValidExportId(string exportId)
    {
        if (!IsValidExportId(exportId))
        {
            throw new ArgumentException("Invalid export id.", nameof(exportId));
        }
    }

    private static ExportHistoryEntry? ReadHistory(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ExportHistoryEntry>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<(string Path, string Label)> GetCandidateOutputDirectories()
    {
        if (IsRunningInContainer())
        {
            yield return (Path.Combine(Path.DirectorySeparatorChar.ToString(), "config", DefaultDirectoryName), "Docker config volume");
            yield return (Path.Combine(Path.DirectorySeparatorChar.ToString(), "exports", "jellyfin-inventory"), "Docker exports volume");
        }

        yield return (Path.Combine(Path.DirectorySeparatorChar.ToString(), "var", "lib", "jellyfin", DefaultDirectoryName), "Linux Jellyfin data directory");
    }

    private static OutputDirectoryOption CreateOption(string path, string label, bool isDefault, string current)
    {
        var fullPath = Path.GetFullPath(path);
        return new OutputDirectoryOption
        {
            Path = fullPath,
            Label = label,
            IsDefault = isDefault,
            IsCurrent = string.Equals(fullPath, current, StringComparison.Ordinal),
            IsWritable = CanWriteToDirectory(fullPath)
        };
    }

    private static bool CanWriteToDirectory(string path)
    {
        try
        {
            var probeDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(probeDirectory) || !Directory.Exists(probeDirectory))
            {
                return false;
            }

            var probePath = Path.Combine(probeDirectory, ".jlie-write-test-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRunningInContainer()
    {
        if (File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"))
        {
            return true;
        }

        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var cgroup = File.ReadAllText("/proc/1/cgroup");
                return cgroup.Contains("docker", StringComparison.OrdinalIgnoreCase)
                    || cgroup.Contains("kubepods", StringComparison.OrdinalIgnoreCase)
                    || cgroup.Contains("containerd", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}

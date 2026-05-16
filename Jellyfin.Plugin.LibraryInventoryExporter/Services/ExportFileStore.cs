using System.Text.Json;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class ExportFileStore
{
    public const string ExportPrefix = "jellyfin-inventory-";
    private readonly IServerApplicationPaths _paths;

    public ExportFileStore(IServerApplicationPaths paths)
    {
        _paths = paths;
    }

    public string ResolveOutputDirectory()
    {
        var configured = Plugin.Instance?.Configuration.OutputDirectory;
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_paths.DataPath, "inventory-exports")
            : configured;
        return Path.GetFullPath(directory);
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

        return deleted;
    }

    public static void EnsureValidExportId(string exportId)
    {
        if (string.IsNullOrWhiteSpace(exportId) || exportId.Any(c => !(char.IsDigit(c) || c == 'T' || c == 'Z' || c == '-')))
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
}

using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class ExportRetentionService
{
    private readonly ExportFileStore _fileStore;
    private readonly ILogger<ExportRetentionService> _logger;

    public ExportRetentionService(ExportFileStore fileStore, ILogger<ExportRetentionService> logger)
    {
        _fileStore = fileStore;
        _logger = logger;
    }

    public void ApplyRetention(int retentionCount, int retentionDays, DateTimeOffset now)
    {
        var exports = _fileStore.ListExports();
        var toDelete = new HashSet<string>(StringComparer.Ordinal);

        if (retentionCount > 0)
        {
            foreach (var entry in exports.Skip(retentionCount))
            {
                toDelete.Add(entry.Id);
            }
        }

        if (retentionDays > 0)
        {
            var cutoff = now.AddDays(-retentionDays);
            foreach (var entry in exports.Where(e => e.GeneratedAt < cutoff))
            {
                toDelete.Add(entry.Id);
            }
        }

        foreach (var exportId in toDelete)
        {
            if (_fileStore.DeleteExport(exportId))
            {
                _logger.LogInformation("Deleted retained Library Inventory Exporter archive {ExportId}", exportId);
            }
        }
    }
}

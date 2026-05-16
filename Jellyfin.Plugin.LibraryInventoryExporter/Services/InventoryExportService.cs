using System.IO.Compression;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class InventoryExportService
{
    private readonly LibraryScanner _scanner;
    private readonly CsvExportWriter _csvWriter;
    private readonly JsonExportWriter _jsonWriter;
    private readonly ExportFileStore _fileStore;
    private readonly ExportRetentionService _retentionService;
    private readonly ILogger<InventoryExportService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ExportStatus _status = new();

    public InventoryExportService(LibraryScanner scanner, CsvExportWriter csvWriter, JsonExportWriter jsonWriter, ExportFileStore fileStore, ExportRetentionService retentionService, ILogger<InventoryExportService> logger)
    {
        _scanner = scanner;
        _csvWriter = csvWriter;
        _jsonWriter = jsonWriter;
        _fileStore = fileStore;
        _retentionService = retentionService;
        _logger = logger;
    }

    public ExportStatus Status => _status;

    public async Task<ExportHistoryEntry> RunExportAsync(ExportOptions options, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("An export is already running.");
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var exportId = _fileStore.CreateExportId(generatedAt);
        var exportDirectory = _fileStore.GetExportDirectory(exportId);
        Directory.CreateDirectory(exportDirectory);

        try
        {
            SetStatus(true, exportId, "Preparing export", 0, 0, 0, null);
            var manifest = await _scanner.ScanAsync(options, (stage, done, total) => SetStatus(true, exportId, stage, Percent(done, total), done, total, null), cancellationToken).ConfigureAwait(false);

            if (options.Formats.Contains(ExportFormat.Csv))
            {
                SetStatus(true, exportId, "Writing CSV", 70, 0, 0, null);
                await _csvWriter.WriteAsync(manifest, exportDirectory, options.IncludeUserData, cancellationToken).ConfigureAwait(false);
            }

            if (options.Formats.Contains(ExportFormat.Json))
            {
                SetStatus(true, exportId, "Writing JSON", 80, 0, 0, null);
                await _jsonWriter.WriteAsync(manifest, Path.Combine(exportDirectory, "manifest.json"), cancellationToken).ConfigureAwait(false);
            }

            SetStatus(true, exportId, "Compressing archive", 90, 0, 0, null);
            var zipPath = _fileStore.GetZipPath(exportId);
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(exportDirectory, zipPath, CompressionLevel.Optimal, false);
            var entry = new ExportHistoryEntry
            {
                Id = exportId,
                GeneratedAt = generatedAt,
                Formats = options.Formats.Select(f => f.ToString().ToLowerInvariant()).ToArray(),
                FileName = Path.GetFileName(zipPath),
                SizeBytes = new FileInfo(zipPath).Length,
                ItemCount = manifest.Libraries.Sum(l => l.Items.Count),
                LibraryCount = manifest.Libraries.Count,
                Status = "completed",
                IncludesUserData = options.IncludeUserData
            };

            await _fileStore.SaveHistoryAsync(entry, cancellationToken).ConfigureAwait(false);
            SetStatus(true, exportId, "Applying retention", 95, entry.ItemCount, entry.ItemCount, null);
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            _retentionService.ApplyRetention(config.RetentionCount, config.RetentionDays, DateTimeOffset.UtcNow);
            SetStatus(false, exportId, "Completed", 100, entry.ItemCount, entry.ItemCount, null);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library inventory export {ExportId} failed", exportId);
            SetStatus(false, exportId, "Failed", _status.ProgressPercent, _status.ProcessedItems, _status.TotalItems, ex.Message);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ExportOptions BuildOptionsFromConfiguration()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var formats = new List<ExportFormat>();
        if (config.ExportCsvByDefault)
        {
            formats.Add(ExportFormat.Csv);
        }

        if (config.ExportJsonByDefault)
        {
            formats.Add(ExportFormat.Json);
        }

        if (formats.Count == 0)
        {
            formats.Add(ExportFormat.Json);
        }

        return new ExportOptions
        {
            Formats = formats,
            IncludeUserData = config.IncludeUserData,
            IncludeMediaStreams = config.IncludeMediaStreams,
            IncludeProviderIds = config.IncludeProviderIds,
            CompressOutput = config.CompressOutput,
            AnonymizeUsers = config.AnonymizeUsers
        };
    }

    private void SetStatus(bool running, string exportId, string stage, double percent, int processed, int total, string? error)
    {
        _status = new ExportStatus
        {
            IsRunning = running,
            ExportId = exportId,
            Stage = stage,
            ProgressPercent = percent,
            ProcessedItems = processed,
            TotalItems = total,
            ErrorMessage = error
        };
    }

    private static double Percent(int done, int total) => total <= 0 ? 0 : Math.Min(69, Math.Round(done * 69d / total, 2));
}

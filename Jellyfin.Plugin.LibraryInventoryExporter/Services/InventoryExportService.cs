using System.IO.Compression;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class InventoryExportService
{
    // A finishing export reports "Completed" a moment before it releases the gate.
    private static readonly TimeSpan FinishingExportGracePeriod = TimeSpan.FromSeconds(5);

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

    /// <summary>
    /// Starts an export in the background and returns its id at once, so the plugin page can follow the progress.
    /// Returns null when another export is running.
    /// </summary>
    public string? TryStartExport(ExportOptions options)
    {
        if (!_gate.Wait(GateWait()))
        {
            return null;
        }

        var generatedAt = DateTimeOffset.UtcNow;
        var exportId = _fileStore.CreateExportId(generatedAt);
        SetStatus(true, exportId, "Preparing export", 0, 0, 0, null);
        _ = Task.Run(async () =>
        {
            try
            {
                await ExportAsync(options, generatedAt, exportId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ExportAsync logs the failure and records it in Status for the plugin page.
            }
            finally
            {
                _gate.Release();
            }
        });

        return exportId;
    }

    public async Task<ExportHistoryEntry> RunExportAsync(ExportOptions options, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(GateWait(), cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("An export is already running.");
        }

        try
        {
            var generatedAt = DateTimeOffset.UtcNow;
            var exportId = _fileStore.CreateExportId(generatedAt);
            SetStatus(true, exportId, "Preparing export", 0, 0, 0, null);
            return await ExportAsync(options, generatedAt, exportId, cancellationToken).ConfigureAwait(false);
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

    private async Task<ExportHistoryEntry> ExportAsync(ExportOptions options, DateTimeOffset generatedAt, string exportId, CancellationToken cancellationToken)
    {
        string? outputDirectory = null;
        string? exportDirectory = null;

        try
        {
            outputDirectory = _fileStore.ResolveOutputDirectory();
            exportDirectory = _fileStore.GetExportDirectory(exportId);
            _logger.LogInformation("Starting library inventory export {ExportId} in {ExportDirectory}", exportId, exportDirectory);

            // An export started in the same second reuses the export id, so start from an empty directory.
            DeleteStagingDirectory(exportDirectory);
            Directory.CreateDirectory(exportDirectory);

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
            _logger.LogInformation("Completed library inventory export {ExportId}: {ItemCount} items, {LibraryCount} libraries, {ZipPath}", exportId, entry.ItemCount, entry.LibraryCount, zipPath);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library inventory export {ExportId} failed", exportId);
            SetStatus(false, exportId, "Failed", _status.ProgressPercent, _status.ProcessedItems, _status.TotalItems, DescribeFailure(ex, outputDirectory));
            throw;
        }
        finally
        {
            // The archive holds the export. The unzipped copy can contain user data, so it must not stay behind.
            DeleteStagingDirectory(exportDirectory);
        }
    }

    // The plugin page shows this message, so it says what failed and what to do about it.
    private static string DescribeFailure(Exception exception, string? outputDirectory)
    {
        if (exception is OperationCanceledException)
        {
            return "The export was cancelled.";
        }

        if (exception is UnauthorizedAccessException or IOException)
        {
            return $"Jellyfin could not write the export to {outputDirectory ?? "the output directory"} ({exception.Message}). Choose an output directory that the Jellyfin server can write to, then save the settings.";
        }

        return exception.Message;
    }

    private void DeleteStagingDirectory(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove the export staging directory {ExportDirectory}", directory);
        }
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

    // Do not wait on an export that is still running, only on one that is about to release the gate.
    private TimeSpan GateWait() => _status.IsRunning ? TimeSpan.Zero : FinishingExportGracePeriod;

    private static double Percent(int done, int total) => total <= 0 ? 0 : Math.Min(69, Math.Round(done * 69d / total, 2));
}

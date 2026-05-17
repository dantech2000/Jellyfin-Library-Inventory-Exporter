using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Controllers;

[ApiController]
[Route("InventoryExporter")]
[Authorize(Policy = "RequiresElevation")]
public sealed class InventoryExporterController : ControllerBase
{
    private readonly InventoryExportService _exportService;
    private readonly ExportFileStore _fileStore;
    private readonly LibraryScanner _libraryScanner;

    public InventoryExporterController(InventoryExportService exportService, ExportFileStore fileStore, LibraryScanner libraryScanner)
    {
        _exportService = exportService;
        _fileStore = fileStore;
        _libraryScanner = libraryScanner;
    }

    [HttpGet("Libraries")]
    public ActionResult<IReadOnlyList<LibraryOption>> Libraries() => Ok(_libraryScanner.ListLibraries());

    [HttpGet("OutputDirectories")]
    public ActionResult<IReadOnlyList<OutputDirectoryOption>> OutputDirectories() => Ok(_fileStore.GetOutputDirectoryOptions());

    [HttpPost("Export")]
    public async Task<ActionResult<ExportStartResponse>> Export([FromBody] ExportRequest request)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var options = new ExportOptions
        {
            Formats = ParseFormats(request.Formats, config),
            LibraryIds = request.LibraryIds ?? Array.Empty<Guid>(),
            IncludeUserData = request.IncludeUserData ?? config.IncludeUserData,
            IncludeMediaStreams = request.IncludeMediaStreams ?? config.IncludeMediaStreams,
            IncludeProviderIds = config.IncludeProviderIds,
            CompressOutput = request.CompressOutput ?? config.CompressOutput,
            AnonymizeUsers = config.AnonymizeUsers
        };

        var task = _exportService.RunExportAsync(options, CancellationToken.None);
        _ = task.ContinueWith(_ => { }, TaskScheduler.Default);
        await Task.Yield();
        return Ok(new ExportStartResponse { ExportId = _exportService.Status.ExportId ?? string.Empty });
    }

    [HttpGet("Exports")]
    public ActionResult<IReadOnlyList<ExportHistoryEntry>> Exports() => Ok(_fileStore.ListExports());

    [HttpGet("Exports/Latest")]
    public IActionResult Latest()
    {
        var latest = _fileStore.GetLatest();
        return latest is null ? NotFound() : Download(latest.Id);
    }

    [HttpGet("Exports/{id}/Download")]
    public IActionResult Download(string id)
    {
        var stream = _fileStore.OpenRead(id);
        return File(stream, "application/zip", ExportFileStore.ExportPrefix + id + ".zip");
    }

    [HttpDelete("Exports/{id}")]
    public IActionResult Delete(string id) => _fileStore.DeleteExport(id) ? NoContent() : NotFound();

    [HttpGet("Status")]
    public ActionResult<ExportStatus> Status() => Ok(_exportService.Status);

    private static IReadOnlyList<ExportFormat> ParseFormats(string[]? formats, PluginConfiguration config)
    {
        if (formats is null || formats.Length == 0)
        {
            var defaults = new List<ExportFormat>();
            if (config.ExportCsvByDefault)
            {
                defaults.Add(ExportFormat.Csv);
            }

            if (config.ExportJsonByDefault)
            {
                defaults.Add(ExportFormat.Json);
            }

            return defaults.Count == 0 ? new[] { ExportFormat.Json } : defaults;
        }

        return formats.Select(f => Enum.Parse<ExportFormat>(f, true)).Distinct().ToArray();
    }
}

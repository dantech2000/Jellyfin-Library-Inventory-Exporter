using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    public ActionResult<ExportStartResponse> Export([FromBody] ExportRequest request)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!TryParseFormats(request.Formats, config, out var formats, out var unknownFormat))
        {
            return Error(StatusCodes.Status400BadRequest, "Unknown export format", $"\"{unknownFormat}\" is not an export format. Use \"csv\" or \"json\".");
        }

        if (!_fileStore.CanWriteOutputDirectory(out var outputDirectory))
        {
            return Error(StatusCodes.Status400BadRequest, "Output directory is not writable", $"Jellyfin cannot write to {outputDirectory}. Choose an output directory that the Jellyfin server can write to, then save the settings.");
        }

        var options = new ExportOptions
        {
            Formats = formats,
            LibraryIds = request.LibraryIds ?? Array.Empty<Guid>(),
            IncludeUserData = request.IncludeUserData ?? config.IncludeUserData,
            IncludeMediaStreams = request.IncludeMediaStreams ?? config.IncludeMediaStreams,
            IncludeProviderIds = config.IncludeProviderIds,
            CompressOutput = request.CompressOutput ?? config.CompressOutput,
            AnonymizeUsers = config.AnonymizeUsers
        };

        var exportId = _exportService.TryStartExport(options);
        if (exportId is null)
        {
            return Error(StatusCodes.Status409Conflict, "Export already running", "Another export is running. Wait until it finishes, then start a new one.");
        }

        return Ok(new ExportStartResponse { ExportId = exportId });
    }

    [HttpGet("Exports")]
    public ActionResult<IReadOnlyList<ExportHistoryEntry>> Exports() => Ok(_fileStore.ListExports());

    [HttpGet("Exports/Latest")]
    public IActionResult Latest()
    {
        var latest = _fileStore.GetLatest();
        return latest is null
            ? Error(StatusCodes.Status404NotFound, "No exports", "There is no export to download yet. Run an export first.")
            : Download(latest.Id);
    }

    [HttpGet("Exports/{id}/Download")]
    public IActionResult Download(string id)
    {
        if (!ExportFileStore.IsValidExportId(id))
        {
            return InvalidExportId(id);
        }

        if (!_fileStore.ExportExists(id))
        {
            return ExportNotFound(id);
        }

        return File(_fileStore.OpenRead(id), "application/zip", ExportFileStore.ExportPrefix + id + ".zip");
    }

    [HttpDelete("Exports/{id}")]
    public IActionResult Delete(string id)
    {
        if (!ExportFileStore.IsValidExportId(id))
        {
            return InvalidExportId(id);
        }

        return _fileStore.DeleteExport(id) ? NoContent() : ExportNotFound(id);
    }

    [HttpGet("Status")]
    public ActionResult<ExportStatus> Status() => Ok(_exportService.Status);

    private static bool TryParseFormats(string[]? requested, PluginConfiguration config, out IReadOnlyList<ExportFormat> formats, out string? unknownFormat)
    {
        unknownFormat = null;
        if (requested is null || requested.Length == 0)
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

            formats = defaults.Count == 0 ? new[] { ExportFormat.Json } : defaults;
            return true;
        }

        var parsed = new List<ExportFormat>();
        foreach (var value in requested)
        {
            if (!Enum.TryParse<ExportFormat>(value, true, out var format) || !Enum.IsDefined(format))
            {
                formats = Array.Empty<ExportFormat>();
                unknownFormat = value;
                return false;
            }

            if (!parsed.Contains(format))
            {
                parsed.Add(format);
            }
        }

        formats = parsed;
        return true;
    }

    private ObjectResult InvalidExportId(string id)
        => Error(StatusCodes.Status400BadRequest, "Invalid export id", $"\"{id}\" is not an export id. Export ids look like 2026-05-16T123456Z.");

    private ObjectResult ExportNotFound(string id)
        => Error(StatusCodes.Status404NotFound, "Export not found", $"Export {id} does not exist. Retention may have deleted it. Refresh the page to see the current exports.");

    // Problem details carry a message that the plugin page shows to the administrator.
    private ObjectResult Error(int statusCode, string title, string detail)
        => StatusCode(statusCode, new ProblemDetails { Status = statusCode, Title = title, Detail = detail });
}

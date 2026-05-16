using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.LibraryInventoryExporter.ScheduledTasks;

public sealed class ExportInventoryTask : IScheduledTask
{
    private readonly InventoryExportService _exportService;

    public ExportInventoryTask(InventoryExportService exportService)
    {
        _exportService = exportService;
    }

    public string Name => "Export library inventory";

    public string Key => "LibraryInventoryExporter";

    public string Description => "Exports Jellyfin library inventory to CSV and JSON.";

    public string Category => "Library Inventory Exporter";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var options = _exportService.BuildOptionsFromConfiguration();
        await _exportService.RunExportAsync(options, cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }
}

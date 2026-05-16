using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LibraryInventoryExporter;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ExportFileStore>();
        serviceCollection.AddSingleton<ExportRetentionService>();
        serviceCollection.AddSingleton<CsvExportWriter>();
        serviceCollection.AddSingleton<JsonExportWriter>();
        serviceCollection.AddSingleton<LibraryScanner>();
        serviceCollection.AddSingleton<InventoryExportService>();
    }
}

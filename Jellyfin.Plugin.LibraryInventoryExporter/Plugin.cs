using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LibraryInventoryExporter;

/// <summary>
/// Library Inventory Exporter plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginGuid = Guid.Parse("7184fe02-8e91-4fd2-9140-6d58d5e91f0a");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Library Inventory Exporter";

    public override Guid Id => PluginGuid;

    public override string Description => "Export Jellyfin library inventory to CSV and JSON.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "LibraryInventoryExporter",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
        yield return new PluginPageInfo
        {
            Name = "LibraryInventoryExporterJs",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
        };
    }
}

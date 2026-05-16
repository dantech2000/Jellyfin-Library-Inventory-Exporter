using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LibraryInventoryExporter;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public string OutputDirectory { get; set; } = string.Empty;

    public bool ExportCsvByDefault { get; set; } = true;

    public bool ExportJsonByDefault { get; set; } = true;

    public bool IncludeUserData { get; set; }

    public bool IncludeMediaStreams { get; set; } = true;

    public bool IncludeProviderIds { get; set; } = true;

    public bool IncludePeople { get; set; }

    public bool IncludeImages { get; set; }

    public bool IncludeCollections { get; set; } = true;

    public bool IncludePlaylists { get; set; }

    public bool CompressOutput { get; set; } = true;

    public int RetentionCount { get; set; } = 10;

    public int RetentionDays { get; set; } = 90;

    public bool AnonymizeUsers { get; set; }
}

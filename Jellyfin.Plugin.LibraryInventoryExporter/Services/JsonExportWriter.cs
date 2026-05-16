using System.Text.Json;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class JsonExportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task WriteAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
}

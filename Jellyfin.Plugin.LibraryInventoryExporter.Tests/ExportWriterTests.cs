using System.IO.Compression;
using System.Text.Json;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class ExportWriterTests
{
    [Fact]
    public async Task Writers_GenerateExpectedCsvAndJsonArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
        var manifest = new InventoryManifest
        {
            GeneratedAt = DateTimeOffset.Parse("2026-05-16T00:00:00Z"),
            ExportOptions = new InventoryExportOptions
            {
                IncludeMediaStreams = true,
                IncludeProviderIds = true
            }
        };
        var library = new InventoryLibrary { Id = "library1", Name = "Movies", CollectionType = "movies" };
        var item = new InventoryItem
        {
            Id = "item1",
            LibraryId = "library1",
            LibraryName = "Movies",
            Type = "Movie",
            Name = "The Matrix",
            ProductionYear = 1999,
            Path = "/media/The Matrix.mkv"
        };
        item.ProviderIds["Imdb"] = "tt0133093";
        var source = new InventoryMediaSource
        {
            ItemId = "item1",
            Id = "source1",
            Path = "/media/The Matrix.mkv",
            Container = "mkv",
            Width = 1920,
            Height = 1080
        };
        source.Streams.Add(new InventoryMediaStream
        {
            ItemId = "item1",
            MediaSourceId = "source1",
            Index = 1,
            Type = "Audio",
            Codec = "aac",
            Language = "eng",
            Channels = 2
        });
        item.MediaSources.Add(source);
        library.Items.Add(item);
        manifest.Libraries.Add(library);

        await new CsvExportWriter().WriteAsync(manifest, directory, false, CancellationToken.None);
        await new JsonExportWriter().WriteAsync(manifest, Path.Combine(directory, "manifest.json"), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(directory, "items.csv")));
        Assert.True(File.Exists(Path.Combine(directory, "media_sources.csv")));
        Assert.True(File.Exists(Path.Combine(directory, "media_streams.csv")));
        Assert.True(File.Exists(Path.Combine(directory, "provider_ids.csv")));
        Assert.False(File.Exists(Path.Combine(directory, "user_data.csv")));
        Assert.Contains("item1,library1,Movies,Movie,The Matrix", await File.ReadAllTextAsync(Path.Combine(directory, "items.csv")));
        Assert.Contains("item1,source1,1,Audio,aac", await File.ReadAllTextAsync(Path.Combine(directory, "media_streams.csv")));
        Assert.Contains("item1,Imdb,tt0133093", await File.ReadAllTextAsync(Path.Combine(directory, "provider_ids.csv")));

        await using var json = File.OpenRead(Path.Combine(directory, "manifest.json"));
        using var document = await JsonDocument.ParseAsync(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Movies", document.RootElement.GetProperty("libraries")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ZipArchive_CanContainGeneratedArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "items.csv"), "item_id");
        var zipPath = Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N") + ".zip");

        ZipFile.CreateFromDirectory(directory, zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "items.csv");
    }
}

using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class ExportFileStoreIntegrationTests
{
    [Fact]
    public async Task SaveListOpenAndDeleteExport_RoundTripsPluginCreatedFiles()
    {
        var root = NewTempDirectory();
        var store = CreateStore(root);
        var exportId = "2026-05-16T123456Z";
        Directory.CreateDirectory(store.ResolveOutputDirectory());
        await File.WriteAllTextAsync(store.GetZipPath(exportId), "zip");

        await store.SaveHistoryAsync(new ExportHistoryEntry
        {
            Id = exportId,
            GeneratedAt = DateTimeOffset.Parse("2026-05-16T12:34:56Z"),
            Formats = new[] { "csv", "json" },
            FileName = ExportFileStore.ExportPrefix + exportId + ".zip",
            ItemCount = 12,
            LibraryCount = 2,
            SizeBytes = 3,
            Status = "completed"
        }, CancellationToken.None);

        var exports = store.ListExports();
        Assert.Single(exports);
        Assert.Equal(exportId, exports[0].Id);
        Assert.Equal(exportId, store.GetLatest()?.Id);
        await using (var stream = store.OpenRead(exportId))
        {
            Assert.True(stream.CanRead);
        }

        Assert.True(store.DeleteExport(exportId));
        Assert.Empty(store.ListExports());
        Assert.False(File.Exists(store.GetZipPath(exportId)));
    }

    [Fact]
    public async Task ListExports_IgnoresMalformedHistoryFiles()
    {
        var root = NewTempDirectory();
        var store = CreateStore(root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, ExportFileStore.ExportPrefix + "bad.history.json"), "{not json");
        await store.SaveHistoryAsync(new ExportHistoryEntry
        {
            Id = "2026-05-16T000000Z",
            GeneratedAt = DateTimeOffset.Parse("2026-05-16T00:00:00Z"),
            FileName = "ok.zip"
        }, CancellationToken.None);

        var export = Assert.Single(store.ListExports());
        Assert.Equal("2026-05-16T000000Z", export.Id);
    }

    [Fact]
    public void DeleteExport_DoesNotDeleteNonPluginFiles()
    {
        var root = NewTempDirectory();
        var store = CreateStore(root);
        Directory.CreateDirectory(root);
        var unrelated = Path.Combine(root, "manual-note.txt");
        File.WriteAllText(unrelated, "keep");

        Assert.False(store.DeleteExport("2026-05-16T000000Z"));
        Assert.True(File.Exists(unrelated));
    }

    private static ExportFileStore CreateStore(string dataPath)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(dataPath);
        return new ExportFileStore(paths.Object);
    }

    private static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
}

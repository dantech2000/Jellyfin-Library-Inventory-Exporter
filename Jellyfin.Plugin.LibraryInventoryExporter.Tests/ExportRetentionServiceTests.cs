using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class ExportRetentionServiceTests
{
    [Fact]
    public async Task ApplyRetention_DeletesExportsBeyondCount()
    {
        var root = NewTempDirectory();
        var store = CreateStore(root);
        await CreateExportAsync(store, "2026-05-16T030000Z", "2026-05-16T03:00:00Z");
        await CreateExportAsync(store, "2026-05-16T020000Z", "2026-05-16T02:00:00Z");
        await CreateExportAsync(store, "2026-05-16T010000Z", "2026-05-16T01:00:00Z");

        new ExportRetentionService(store, NullLogger<ExportRetentionService>.Instance)
            .ApplyRetention(retentionCount: 2, retentionDays: 0, DateTimeOffset.Parse("2026-05-16T04:00:00Z"));

        Assert.Equal(new[] { "2026-05-16T030000Z", "2026-05-16T020000Z" }, store.ListExports().Select(e => e.Id).ToArray());
        Assert.False(File.Exists(store.GetZipPath("2026-05-16T010000Z")));
    }

    [Fact]
    public async Task ApplyRetention_DeletesExportsOlderThanRetentionDays()
    {
        var root = NewTempDirectory();
        var store = CreateStore(root);
        await CreateExportAsync(store, "2026-05-16T000000Z", "2026-05-16T00:00:00Z");
        await CreateExportAsync(store, "2026-01-01T000000Z", "2026-01-01T00:00:00Z");

        new ExportRetentionService(store, NullLogger<ExportRetentionService>.Instance)
            .ApplyRetention(retentionCount: 0, retentionDays: 30, DateTimeOffset.Parse("2026-05-16T00:00:00Z"));

        var export = Assert.Single(store.ListExports());
        Assert.Equal("2026-05-16T000000Z", export.Id);
        Assert.False(File.Exists(store.GetZipPath("2026-01-01T000000Z")));
    }

    private static async Task CreateExportAsync(ExportFileStore store, string id, string generatedAt)
    {
        Directory.CreateDirectory(store.ResolveOutputDirectory());
        await File.WriteAllTextAsync(store.GetZipPath(id), "zip");
        await store.SaveHistoryAsync(new ExportHistoryEntry
        {
            Id = id,
            GeneratedAt = DateTimeOffset.Parse(generatedAt),
            FileName = ExportFileStore.ExportPrefix + id + ".zip",
            Status = "completed"
        }, CancellationToken.None);
    }

    private static ExportFileStore CreateStore(string dataPath)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(dataPath);
        return new ExportFileStore(paths.Object);
    }

    private static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
}

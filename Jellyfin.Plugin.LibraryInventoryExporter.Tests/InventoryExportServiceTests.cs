using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class InventoryExportServiceTests
{
    [Fact]
    public async Task RunExportAsync_LeavesOnlyTheArchiveAndItsHistoryFile()
    {
        var dataPath = NewTempDirectory();
        var service = CreateService(() => dataPath);

        var entry = await service.RunExportAsync(new ExportOptions(), CancellationToken.None);

        var outputDirectory = Path.Combine(dataPath, ExportFileStore.DefaultDirectoryName);
        Assert.Equal(
            new[] { entry.FileName, ExportFileStore.ExportPrefix + entry.Id + ".history.json" }.OrderBy(name => name, StringComparer.Ordinal),
            Directory.EnumerateFileSystemEntries(outputDirectory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("Completed", service.Status.Stage);
    }

    [Fact]
    public async Task RunExportAsync_ReportsAnUnwritableOutputDirectoryAndStaysUsable()
    {
        var root = NewTempDirectory();
        Directory.CreateDirectory(root);
        var notADirectory = Path.Combine(root, "not-a-directory");
        await File.WriteAllTextAsync(notADirectory, "x");
        var dataPath = Path.Combine(notADirectory, "data");
        var service = CreateService(() => dataPath);

        await Assert.ThrowsAnyAsync<IOException>(() => service.RunExportAsync(new ExportOptions(), CancellationToken.None));
        Assert.False(service.Status.IsRunning);
        Assert.Equal("Failed", service.Status.Stage);
        Assert.Contains("Choose an output directory that the Jellyfin server can write to", service.Status.ErrorMessage);

        dataPath = Path.Combine(root, "data");
        var entry = await service.RunExportAsync(new ExportOptions(), CancellationToken.None);
        Assert.Equal("Completed", service.Status.Stage);
        Assert.Equal(entry.Id, service.Status.ExportId);
    }

    [Fact]
    public async Task TryStartExport_RefusesASecondExportWhileOneRuns()
    {
        using var scanStarted = new ManualResetEventSlim();
        using var releaseScan = new ManualResetEventSlim();
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(() =>
            {
                scanStarted.Set();
                releaseScan.Wait(TimeSpan.FromSeconds(30));
                return new List<VirtualFolderInfo>();
            });
        var dataPath = NewTempDirectory();
        var service = CreateService(() => dataPath, libraryManager.Object);

        Assert.NotNull(service.TryStartExport(new ExportOptions()));
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(service.Status.IsRunning);
        Assert.Null(service.TryStartExport(new ExportOptions()));

        releaseScan.Set();
        await WaitUntil(() => !service.Status.IsRunning);
        Assert.Equal("Completed", service.Status.Stage);
        Assert.NotNull(service.TryStartExport(new ExportOptions()));
    }

    private static InventoryExportService CreateService(Func<string> dataPath, ILibraryManager? libraryManager = null)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(dataPath);
        var fileStore = new ExportFileStore(paths.Object);
        var scanner = new LibraryScanner(
            libraryManager ?? Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IServerApplicationHost>(),
            NullLogger<LibraryScanner>.Instance);

        return new InventoryExportService(
            scanner,
            new CsvExportWriter(),
            new JsonExportWriter(),
            fileStore,
            new ExportRetentionService(fileStore, NullLogger<ExportRetentionService>.Instance),
            NullLogger<InventoryExportService>.Instance);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the export to finish.");
            await Task.Delay(20);
        }
    }

    private static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
}

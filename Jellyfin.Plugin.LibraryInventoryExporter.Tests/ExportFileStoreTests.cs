using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class ExportFileStoreTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("2026-05-16T000000Z/evil")]
    [InlineData("")]
    public void EnsureValidExportId_RejectsTraversal(string exportId)
    {
        Assert.Throws<ArgumentException>(() => ExportFileStore.EnsureValidExportId(exportId));
    }

    [Fact]
    public void EnsureValidExportId_AllowsTimestampId()
    {
        ExportFileStore.EnsureValidExportId("2026-05-16T000000Z");
    }

    [Fact]
    public void ResolveOutputDirectory_DefaultsToJellyfinDataPath()
    {
        var dataPath = NewTempDirectory();
        var store = CreateStore(dataPath);

        Assert.Equal(Path.Combine(dataPath, ExportFileStore.DefaultDirectoryName), store.ResolveOutputDirectory());
    }

    [Fact]
    public void GetOutputDirectoryOptions_IncludesWritableDefault()
    {
        var dataPath = NewTempDirectory();
        Directory.CreateDirectory(dataPath);
        var store = CreateStore(dataPath);

        var option = Assert.Single(store.GetOutputDirectoryOptions(), option => option.IsDefault);
        Assert.Equal(Path.Combine(dataPath, ExportFileStore.DefaultDirectoryName), option.Path);
        Assert.True(option.IsCurrent);
        Assert.True(option.IsWritable);
    }

    private static ExportFileStore CreateStore(string dataPath)
    {
        var paths = new Mock<IServerApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(dataPath);
        return new ExportFileStore(paths.Object);
    }

    private static string NewTempDirectory() => Path.Combine(Path.GetTempPath(), "jlie-tests", Guid.NewGuid().ToString("N"));
}

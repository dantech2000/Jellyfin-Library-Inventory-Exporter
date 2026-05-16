using Jellyfin.Plugin.LibraryInventoryExporter.Services;
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
}

using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Tests;

public sealed class LibraryScannerTests
{
    [Fact]
    public async Task ScanAsync_UsesLibraryManagerItemQueryForVirtualFolderItems()
    {
        var libraryId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(new List<VirtualFolderInfo>
            {
                new()
                {
                    ItemId = libraryId.ToString("N"),
                    Name = "Movies"
                }
            });
        libraryManager
            .Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query =>
                query.Recursive
                && query.TopParentIds.Length == 1
                && query.TopParentIds[0] == libraryId)))
            .Returns(new BaseItem[]
            {
                new Movie
                {
                    Id = movieId,
                    Name = "The Matrix",
                    Path = "/media/movies/The Matrix.mkv"
                }
            });

        var scanner = new LibraryScanner(
            libraryManager.Object,
            Mock.Of<IUserManager>(),
            Mock.Of<IUserDataManager>(),
            NullLogger<LibraryScanner>.Instance);

        var manifest = await scanner.ScanAsync(new ExportOptions(), (_, _, _) => { }, CancellationToken.None);

        var library = Assert.Single(manifest.Libraries);
        Assert.Equal("Movies", library.Name);
        var item = Assert.Single(library.Items);
        Assert.Equal(movieId.ToString("N"), item.Id);
        Assert.Equal("The Matrix", item.Name);
    }

    [Fact]
    public async Task ScanAsync_FallsBackToLibraryLocationsWhenParentQueryIsEmpty()
    {
        var libraryId = Guid.NewGuid();
        var movieId = Guid.NewGuid();
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(new List<VirtualFolderInfo>
            {
                new()
                {
                    ItemId = libraryId.ToString("N"),
                    Name = "Movies",
                    Locations = new[] { "/media/movies" }
                }
            });
        libraryManager
            .Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query =>
                query.TopParentIds.Length == 1 || query.ParentId == libraryId)))
            .Returns(Array.Empty<BaseItem>());
        libraryManager
            .Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query =>
                query.TopParentIds.Length == 0 && query.ParentId == Guid.Empty)))
            .Returns(new BaseItem[]
            {
                new Movie
                {
                    Id = movieId,
                    Name = "The Matrix",
                    Path = "/media/movies/The Matrix.mkv"
                },
                new Movie
                {
                    Id = Guid.NewGuid(),
                    Name = "Other Library",
                    Path = "/media/other/Other Library.mkv"
                }
            });

        var scanner = new LibraryScanner(
            libraryManager.Object,
            Mock.Of<IUserManager>(),
            Mock.Of<IUserDataManager>(),
            NullLogger<LibraryScanner>.Instance);

        var manifest = await scanner.ScanAsync(new ExportOptions(), (_, _, _) => { }, CancellationToken.None);

        var library = Assert.Single(manifest.Libraries);
        var item = Assert.Single(library.Items);
        Assert.Equal(movieId.ToString("N"), item.Id);
        Assert.Equal("The Matrix", item.Name);
    }
}

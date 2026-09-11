using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using Jellyfin.Plugin.LibraryInventoryExporter.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
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

        var scanner = CreateScanner(
            libraryManager.Object,
            applicationHost: Mock.Of<IServerApplicationHost>(host => host.ApplicationVersionString == "10.11.11"));

        var manifest = await scanner.ScanAsync(new ExportOptions(), (_, _, _) => { }, CancellationToken.None);

        Assert.Equal("10.11.11", manifest.Server.Version);
        var library = Assert.Single(manifest.Libraries);
        Assert.Equal(libraryId.ToString("N"), library.Id);
        Assert.Equal("Movies", library.Name);
        var item = Assert.Single(library.Items);
        Assert.Equal(libraryId.ToString("N"), item.LibraryId);
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

        var scanner = CreateScanner(libraryManager.Object);

        var manifest = await scanner.ScanAsync(new ExportOptions(), (_, _, _) => { }, CancellationToken.None);

        var library = Assert.Single(manifest.Libraries);
        Assert.Equal(libraryId.ToString("N"), library.Id);
        var item = Assert.Single(library.Items);
        Assert.Equal(libraryId.ToString("N"), item.LibraryId);
        Assert.Equal(movieId.ToString("N"), item.Id);
        Assert.Equal("The Matrix", item.Name);
    }

    [Fact]
    public async Task ScanAsync_ExportsTheProbedStreamsOfEachMediaSource()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "The Matrix",
            Path = "/media/movies/The Matrix.mkv"
        };
        var mediaSourceManager = new Mock<IMediaSourceManager>();
        mediaSourceManager
            .Setup(m => m.GetStaticMediaSources(movie, false, It.IsAny<User>()))
            .Returns(new List<MediaSourceInfo>
            {
                new()
                {
                    Id = "source1",
                    Path = movie.Path,
                    Container = "mkv",
                    Size = 1234,
                    Bitrate = 8000,
                    MediaStreams = new List<MediaStream>
                    {
                        new() { Index = 0, Type = MediaStreamType.Video, Codec = "h264", Width = 1920, Height = 1080 },
                        new() { Index = 1, Type = MediaStreamType.Audio, Codec = "aac", Language = "eng", Channels = 2, IsDefault = true },
                        new() { Index = 2, Type = MediaStreamType.Subtitle, Codec = "subrip", Language = "spa", IsExternal = true }
                    }
                }
            });

        var scanner = CreateScanner(LibraryWith(movie), mediaSourceManager: mediaSourceManager.Object);

        var manifest = await scanner.ScanAsync(new ExportOptions(), (_, _, _) => { }, CancellationToken.None);

        var source = Assert.Single(Assert.Single(Assert.Single(manifest.Libraries).Items).MediaSources);
        Assert.Equal("source1", source.Id);
        Assert.Equal("mkv", source.Container);
        Assert.Equal(1234, source.SizeBytes);
        Assert.Equal(1920, source.Width);
        Assert.Equal(1080, source.Height);
        Assert.Equal(
            new[] { "Video|h264|", "Audio|aac|eng", "Subtitle|subrip|spa" },
            source.Streams.Select(stream => $"{stream.Type}|{stream.Codec}|{stream.Language}"));
        Assert.True(source.Streams[2].IsExternal);
        Assert.All(source.Streams, stream => Assert.Equal("source1", stream.MediaSourceId));
    }

    [Fact]
    public async Task ScanAsync_ExportsWatchStateForEveryUserWhenRequested()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "The Matrix",
            Path = "/media/movies/The Matrix.mkv"
        };
        var user = new User("alice", "provider", "reset-provider");
        var lastPlayed = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var userManager = new Mock<IUserManager>();
        userManager
            .Setup(m => m.GetUsers())
            .Returns(new List<User> { user });
        var userDataManager = new Mock<IUserDataManager>();
        userDataManager
            .Setup(m => m.GetUserData(user, movie))
            .Returns(new UserItemData
            {
                Key = "matrix",
                Played = true,
                IsFavorite = true,
                PlayCount = 2,
                LastPlayedDate = lastPlayed,
                PlaybackPositionTicks = 42
            });

        var scanner = CreateScanner(LibraryWith(movie), userManager.Object, userDataManager.Object);

        var manifest = await scanner.ScanAsync(new ExportOptions { IncludeUserData = true }, (_, _, _) => { }, CancellationToken.None);

        var item = Assert.Single(Assert.Single(manifest.Libraries).Items);
        var userData = Assert.Single(item.UserData);
        Assert.Equal(item.Id, userData.ItemId);
        Assert.Equal(user.Id.ToString("N"), userData.UserId);
        Assert.Equal("alice", userData.UserName);
        Assert.True(userData.Played);
        Assert.True(userData.IsFavorite);
        Assert.Equal(2, userData.PlayCount);
        Assert.Equal(new DateTimeOffset(lastPlayed), userData.LastPlayedDate);
        Assert.Equal(42, userData.PlaybackPositionTicks);
    }

    [Fact]
    public void ListLibraries_UsesVirtualFolderItemId()
    {
        var libraryId = Guid.NewGuid();
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

        var scanner = CreateScanner(libraryManager.Object);

        var library = Assert.Single(scanner.ListLibraries());
        Assert.Equal(libraryId.ToString("N"), library.Id);
    }

    private static ILibraryManager LibraryWith(BaseItem item)
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(new List<VirtualFolderInfo>
            {
                new()
                {
                    ItemId = Guid.NewGuid().ToString("N"),
                    Name = "Movies"
                }
            });
        libraryManager
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new[] { item });
        return libraryManager.Object;
    }

    private static LibraryScanner CreateScanner(
        ILibraryManager libraryManager,
        IUserManager? userManager = null,
        IUserDataManager? userDataManager = null,
        IMediaSourceManager? mediaSourceManager = null,
        IServerApplicationHost? applicationHost = null)
        => new(
            libraryManager,
            userManager ?? Mock.Of<IUserManager>(),
            userDataManager ?? Mock.Of<IUserDataManager>(),
            mediaSourceManager ?? Mock.Of<IMediaSourceManager>(),
            applicationHost ?? Mock.Of<IServerApplicationHost>(),
            NullLogger<LibraryScanner>.Instance);
}

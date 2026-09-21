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
    public async Task ScanAsync_ReportsProgressAcrossAllLibraries()
    {
        var movies = Guid.NewGuid();
        var music = Guid.NewGuid();
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(new List<VirtualFolderInfo>
            {
                new() { ItemId = movies.ToString("N"), Name = "Movies" },
                new() { ItemId = music.ToString("N"), Name = "Music" }
            });
        libraryManager.Setup(l => l.GetCount(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(movies)))).Returns(2);
        libraryManager.Setup(l => l.GetCount(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(music)))).Returns(3);
        var movieItems = NewMovies(2);
        var musicItems = NewMovies(3);
        libraryManager.Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(movies)))).Returns(movieItems);
        libraryManager.Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(music)))).Returns(musicItems);
        var reports = new List<(string Stage, int Processed, int Total)>();

        var manifest = await CreateScanner(libraryManager.Object).ScanAsync(new ExportOptions(), (stage, processed, total) => reports.Add((stage, processed, total)), CancellationToken.None);

        // One report per chunk of items. Each library here fits into one chunk.
        var scanning = reports.Where(report => report.Total > 0).ToList();
        Assert.Equal(new[] { 2, 5 }, scanning.Select(report => report.Processed));
        Assert.All(scanning, report => Assert.Equal(5, report.Total));
        Assert.Equal("Scanning Movies (library 1 of 2)", scanning[0].Stage);
        Assert.Equal("Scanning Music (library 2 of 2)", scanning[^1].Stage);

        // Parallel mapping keeps the order in which Jellyfin returned the items.
        Assert.Equal(
            movieItems.Concat(musicItems).Select(item => item.Id.ToString("N")),
            manifest.Libraries.SelectMany(library => library.Items).Select(item => item.Id));
    }

    [Fact]
    public async Task ScanAsync_CountsLibrariesThatNeedTheParentQueryBeforeScanning()
    {
        var shows = Guid.NewGuid();
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager
            .Setup(l => l.GetVirtualFolders())
            .Returns(new List<VirtualFolderInfo> { new() { ItemId = shows.ToString("N"), Name = "Shows" } });
        libraryManager.Setup(l => l.GetCount(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(shows)))).Returns(0);
        libraryManager.Setup(l => l.GetCount(It.Is<InternalItemsQuery>(query => query.ParentId == shows))).Returns(3);
        libraryManager.Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query => query.TopParentIds.Contains(shows)))).Returns(Array.Empty<BaseItem>());
        libraryManager.Setup(l => l.GetItemList(It.Is<InternalItemsQuery>(query => query.ParentId == shows))).Returns(NewMovies(3));
        var reports = new List<(string Stage, int Processed, int Total)>();

        await CreateScanner(libraryManager.Object).ScanAsync(new ExportOptions(), (stage, processed, total) => reports.Add((stage, processed, total)), CancellationToken.None);

        var scanning = reports.Where(report => report.Stage.StartsWith("Scanning", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { 3 }, scanning.Select(report => report.Processed));
        Assert.All(scanning, report => Assert.Equal(3, report.Total));
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
        var bob = new User("bob", "provider", "reset-provider");
        var lastPlayed = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

        // The library query loads the user data rows of the item: alice has one, bob has none.
        movie.UserData = new List<UserData>
        {
            new() { CustomDataKey = "matrix", ItemId = movie.Id, Item = null, UserId = user.Id, User = null, Played = true }
        };
        var userManager = new Mock<IUserManager>();
        userManager
            .Setup(m => m.GetUsers())
            .Returns(new List<User> { user, bob });
        var watchState = new UserItemData
        {
            Key = "matrix",
            Played = true,
            IsFavorite = true,
            PlayCount = 2,
            LastPlayedDate = lastPlayed,
            PlaybackPositionTicks = 42
        };
        var userDataManager = new Mock<IUserDataManager>();
#if NET10_0_OR_GREATER
        // Jellyfin 12.0: one batch lookup per user and chunk of items.
        userDataManager
            .Setup(m => m.GetUserDataBatch(It.Is<IReadOnlyList<BaseItem>>(items => items.Contains(movie)), user))
            .Returns(new Dictionary<Guid, UserItemData> { [movie.Id] = watchState });
#else
        userDataManager
            .Setup(m => m.GetUserData(user, movie))
            .Returns(watchState);
#endif

        var scanner = CreateScanner(LibraryWith(movie), userManager.Object, userDataManager.Object);

        var manifest = await scanner.ScanAsync(new ExportOptions { IncludeUserData = true }, (_, _, _) => { }, CancellationToken.None);

        var item = Assert.Single(Assert.Single(manifest.Libraries).Items);
        Assert.Equal(2, item.UserData.Count);

        // Bob has no user data row, so he gets the default watch state without a Jellyfin lookup.
        var bobData = Assert.Single(item.UserData, data => data.UserName == "bob");
        Assert.False(bobData.Played);
        Assert.False(bobData.IsFavorite);
        Assert.Equal(0, bobData.PlayCount);
#if NET10_0_OR_GREATER
        userDataManager.Verify(m => m.GetUserDataBatch(It.IsAny<IReadOnlyList<BaseItem>>(), bob), Times.Never);
#else
        userDataManager.Verify(m => m.GetUserData(bob, It.IsAny<BaseItem>()), Times.Never);
#endif

        var userData = Assert.Single(item.UserData, data => data.UserName == "alice");
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
    public async Task ScanAsync_AsksJellyfinWhenTheQueryDidNotLoadUserDataRows()
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Name = "The Matrix",
            Path = "/media/movies/The Matrix.mkv"
        };
        movie.UserData = null!;
        var user = new User("alice", "provider", "reset-provider");
        var userManager = new Mock<IUserManager>();
        userManager
            .Setup(m => m.GetUsers())
            .Returns(new List<User> { user });
        var watchState = new UserItemData { Key = "matrix", Played = true };
        var userDataManager = new Mock<IUserDataManager>();
#if NET10_0_OR_GREATER
        userDataManager
            .Setup(m => m.GetUserDataBatch(It.Is<IReadOnlyList<BaseItem>>(items => items.Contains(movie)), user))
            .Returns(new Dictionary<Guid, UserItemData> { [movie.Id] = watchState });
#else
        userDataManager
            .Setup(m => m.GetUserData(user, movie))
            .Returns(watchState);
#endif

        var manifest = await CreateScanner(LibraryWith(movie), userManager.Object, userDataManager.Object)
            .ScanAsync(new ExportOptions { IncludeUserData = true }, (_, _, _) => { }, CancellationToken.None);

        Assert.True(Assert.Single(Assert.Single(Assert.Single(manifest.Libraries).Items).UserData).Played);
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

    private static BaseItem[] NewMovies(int count)
        => Enumerable.Range(1, count)
            .Select(index => (BaseItem)new Movie { Id = Guid.NewGuid(), Name = $"Movie {index}", Path = $"/media/movies/{Guid.NewGuid():N}.mkv" })
            .ToArray();

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

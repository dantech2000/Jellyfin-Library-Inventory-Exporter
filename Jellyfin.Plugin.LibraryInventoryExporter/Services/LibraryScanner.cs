using System.Collections;
using System.Runtime.ExceptionServices;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class LibraryScanner
{
    private static readonly BaseItemKind[] SupportedItemKinds =
    {
        BaseItemKind.Movie,
        BaseItemKind.Series,
        BaseItemKind.Season,
        BaseItemKind.Episode,
        BaseItemKind.MusicAlbum,
        BaseItemKind.Audio,
        BaseItemKind.Video,
        BaseItemKind.BoxSet
    };

    // Watch state is loaded for this many items at a time. Jellyfin 12.0 answers each chunk with one database query.
    private const int ItemChunkSize = 500;

    // Items are mapped on up to four threads, and on no more than half of the cores, so playback keeps its share.
    // Measured on a four-core server with 49,500 items: one thread exports in 12.6 s, half the cores in 9.5 s, and
    // all cores in 8.7 s. All cores also raise the 95th percentile of other Jellyfin requests from 7 ms to 103 ms.
    private static readonly int MappingParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IServerApplicationHost _applicationHost;
    private readonly ILogger<LibraryScanner> _logger;

    public LibraryScanner(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, IMediaSourceManager mediaSourceManager, IServerApplicationHost applicationHost, ILogger<LibraryScanner> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _mediaSourceManager = mediaSourceManager;
        _applicationHost = applicationHost;
        _logger = logger;
    }

    public Task<InventoryManifest> ScanAsync(ExportOptions options, Action<string, int, int> reportProgress, CancellationToken cancellationToken)
    {
        var manifest = new InventoryManifest
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Server = new InventoryServer { Version = _applicationHost.ApplicationVersionString },
            ExportOptions = new InventoryExportOptions
            {
                IncludeUserData = options.IncludeUserData,
                IncludeMediaStreams = options.IncludeMediaStreams,
                IncludeProviderIds = options.IncludeProviderIds
            }
        };

        var libraries = GetLibraries()
            .Where(l => options.LibraryIds.Count == 0 || options.LibraryIds.Contains(GetLibraryGuid(l)))
            .ToList();
        var allItems = new Lazy<IReadOnlyList<object>>(QueryAllItems);
        var users = options.IncludeUserData ? _userManager.GetUsers().ToList() : new List<User>();

        // Count every library first, so the progress covers the whole export. A library whose items come from a
        // fallback query counts as zero here and corrects the total when the scan reaches it.
        reportProgress("Counting items", 0, 0);
        var counts = libraries.Select(CountItems).ToList();
        var total = counts.Sum();
        var processed = 0;
        _logger.LogInformation("Starting inventory scan for {LibraryCount} libraries with {ItemCount} counted items. Selected library filter count: {SelectedLibraryCount}", libraries.Count, total, options.LibraryIds.Count);

        for (var index = 0; index < libraries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var library = libraries[index];
            var inventoryLibrary = new InventoryLibrary
            {
                Id = GetLibraryGuid(library).ToString("N"),
                Name = GetString(library, "Name") ?? "Unknown",
                CollectionType = GetString(library, "CollectionType")
            };

            var items = GetItems(library, allItems).Where(IsSupportedItem).ToList();
            total += items.Count - counts[index];
            var stage = $"Scanning {inventoryLibrary.Name} (library {index + 1} of {libraries.Count})";
            _logger.LogInformation("Inventory scan library {LibraryName} ({LibraryId}) resolved {ItemCount} supported items", inventoryLibrary.Name, inventoryLibrary.Id, items.Count);

            foreach (var chunk in items.Chunk(ItemChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var watchState = LoadWatchState(chunk, users);
                inventoryLibrary.Items.AddRange(MapChunk(chunk, inventoryLibrary, options, users, watchState, cancellationToken));
                processed += chunk.Length;
                reportProgress(stage, processed, total);
            }

            manifest.Libraries.Add(inventoryLibrary);
        }

        _logger.LogInformation("Scanned {LibraryCount} libraries and {ItemCount} items for inventory export", manifest.Libraries.Count, manifest.Libraries.Sum(l => l.Items.Count));
        return Task.FromResult(manifest);
    }

    // Counts what GetItems returns: the top-parent query, or the parent query when the first finds nothing.
    // Libraries that need the location or reflection fallback count as zero.
    private int CountItems(object library)
    {
        var libraryId = GetLibraryGuid(library);
        if (libraryId == Guid.Empty)
        {
            return 0;
        }

        var count = CountItems(libraryId, useTopParent: true);
        return count > 0 ? count : CountItems(libraryId, useTopParent: false);
    }

    private int CountItems(Guid libraryId, bool useTopParent)
    {
        try
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = SupportedItemKinds
            };

            if (useTopParent)
            {
                query.TopParentIds = new[] { libraryId };
            }
            else
            {
                query.ParentId = libraryId;
            }

            return _libraryManager.GetCount(query);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to count inventory export items for library {LibraryId}", libraryId);
            return 0;
        }
    }

    // Maps a chunk of items in parallel and keeps their order. Most of the time per item is Jellyfin reading
    // media sources and streams from its database, so a few threads shorten the export considerably.
    private InventoryItem[] MapChunk(object[] chunk, InventoryLibrary library, ExportOptions options, IReadOnlyList<User> users, IReadOnlyDictionary<(Guid UserId, Guid ItemId), UserItemData> watchState, CancellationToken cancellationToken)
    {
        var mapped = new InventoryItem[chunk.Length];
        try
        {
            Parallel.For(
                0,
                chunk.Length,
                new ParallelOptions { MaxDegreeOfParallelism = MappingParallelism, CancellationToken = cancellationToken },
                index => mapped[index] = MapItem(chunk[index], library, options, users, watchState));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Keep the original exception, so the plugin page shows its message.
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }

        return mapped;
    }

    // Loads the watch state of every user for a chunk of items, keyed by user and item.
    //
    // Library queries load the user data rows of all users with each item, and most items have no row for most
    // users. Such a pair has Jellyfin's default watch state, so it is filled in here. Asking Jellyfin instead costs
    // a key lookup per pair, and for an episode that lookup loads its series, which made exports with watch state
    // slow in large TV libraries. Pairs with a row, and items whose rows were not loaded (null), go to Jellyfin.
    private Dictionary<(Guid UserId, Guid ItemId), UserItemData> LoadWatchState(object[] chunk, IReadOnlyList<User> users)
    {
        var watchState = new Dictionary<(Guid UserId, Guid ItemId), UserItemData>();
        if (users.Count == 0)
        {
            return watchState;
        }

        var items = chunk.OfType<BaseItem>().ToList();
        foreach (var user in users)
        {
            var itemsWithRows = new List<BaseItem>();
            foreach (var item in items)
            {
                if (item.UserData is null || item.UserData.Any(row => row.UserId.Equals(user.Id)))
                {
                    itemsWithRows.Add(item);
                }
                else
                {
                    watchState[(user.Id, item.Id)] = new UserItemData { Key = string.Empty };
                }
            }

            if (itemsWithRows.Count == 0)
            {
                continue;
            }

#if NET10_0_OR_GREATER
            // Jellyfin 12.0 queries the database for every single lookup. The batch call needs one query per chunk.
            foreach (var (itemId, data) in _userDataManager.GetUserDataBatch(itemsWithRows, user))
            {
                watchState[(user.Id, itemId)] = data;
            }
#else
            // Jellyfin 10.11 reads user data from the rows it already loaded, so single lookups are cheap.
            foreach (var item in itemsWithRows)
            {
                if (_userDataManager.GetUserData(user, item) is { } data)
                {
                    watchState[(user.Id, item.Id)] = data;
                }
            }
#endif
        }

        return watchState;
    }

    public IReadOnlyList<LibraryOption> ListLibraries()
    {
        return GetLibraries()
            .Select(l => new LibraryOption
            {
                Id = GetLibraryGuid(l).ToString("N"),
                Name = GetString(l, "Name") ?? "Unknown",
                CollectionType = GetString(l, "CollectionType")
            })
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<object> GetLibraries()
    {
        var method = _libraryManager.GetType().GetMethod("GetVirtualFolders", Type.EmptyTypes);
        var folders = method?.Invoke(_libraryManager, Array.Empty<object>()) as IEnumerable;
        if (folders is null)
        {
            return Array.Empty<object>();
        }

        return folders.Cast<object>();
    }

    private IEnumerable<object> GetChildren(object library)
    {
        foreach (var propertyName in new[] { "Locations", "ItemId", "Id" })
        {
            var value = GetValue(library, propertyName);
            if (value is null)
            {
                continue;
            }

            var folder = TryGetItemById(value);
            if (folder is not null)
            {
                return EnumerateTree(folder);
            }
        }

        return Array.Empty<object>();
    }

    private IReadOnlyList<object> GetItems(object library, Lazy<IReadOnlyList<object>> allItems)
    {
        var libraryName = GetString(library, "Name") ?? "Unknown";
        var libraryId = GetLibraryGuid(library);

        var locations = GetStringArray(library, "Locations")
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .ToArray();
        _logger.LogInformation("Resolving inventory items for library {LibraryName}. ItemId/Id: {LibraryId}; Locations: {Locations}", libraryName, libraryId, locations.Length == 0 ? "(none)" : string.Join(", ", locations));

        if (libraryId != Guid.Empty)
        {
            var items = QueryItems(libraryId, useTopParent: true);
            _logger.LogInformation("Top-parent query for library {LibraryName} ({LibraryId}) returned {ItemCount} items", libraryName, libraryId, items.Count);
            if (items.Count > 0)
            {
                return items;
            }

            items = QueryItems(libraryId, useTopParent: false);
            _logger.LogInformation("Parent query for library {LibraryName} ({LibraryId}) returned {ItemCount} items", libraryName, libraryId, items.Count);
            if (items.Count > 0)
            {
                return items;
            }
        }

        var itemsByLocation = GetItemsByLocation(library, allItems.Value);
        _logger.LogInformation("Location query for library {LibraryName} matched {ItemCount} items from {AllItemCount} indexed items", libraryName, itemsByLocation.Count, allItems.Value.Count);
        if (itemsByLocation.Count > 0)
        {
            return itemsByLocation;
        }

        var reflectedItems = GetChildren(library).Where(IsSupportedItem).ToList();
        _logger.LogInformation("Reflection fallback for library {LibraryName} returned {ItemCount} items", libraryName, reflectedItems.Count);
        return reflectedItems;
    }

    private IReadOnlyList<object> QueryItems(Guid libraryId, bool useTopParent)
    {
        try
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = SupportedItemKinds
            };

            if (useTopParent)
            {
                query.TopParentIds = new[] { libraryId };
            }
            else
            {
                query.ParentId = libraryId;
            }

            return _libraryManager.GetItemList(query).Cast<object>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to query inventory export items for library {LibraryId}", libraryId);
            return Array.Empty<object>();
        }
    }

    private IReadOnlyList<object> QueryAllItems()
    {
        try
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = SupportedItemKinds
            }).Cast<object>().ToList();
            _logger.LogInformation("Inventory scan all-items query returned {ItemCount} supported items", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to query all inventory export items");
            return Array.Empty<object>();
        }
    }

    private static IReadOnlyList<object> GetItemsByLocation(object library, IReadOnlyList<object> allItems)
    {
        var locations = GetStringArray(library, "Locations")
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Select(NormalizeDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (locations.Length == 0)
        {
            return Array.Empty<object>();
        }

        return allItems
            .Where(item => IsSupportedItem(item))
            .Where(item => IsUnderAnyLocation(GetString(item, "Path"), locations))
            .ToList();
    }

    private static bool IsUnderAnyLocation(string? path, IReadOnlyList<string> locations)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(path);
        return locations.Any(location => normalizedPath.StartsWith(location, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private IEnumerable<object> EnumerateTree(object root)
    {
        var stack = new Stack<object>();
        foreach (var child in GetDirectChildren(root))
        {
            stack.Push(child);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach (var child in GetDirectChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private IEnumerable<object> GetDirectChildren(object item)
    {
        var method = item.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetChildren" && m.GetParameters().Length <= 1);
        if (method?.Invoke(item, method.GetParameters().Length == 0 ? Array.Empty<object>() : new object?[] { null }) is IEnumerable children)
        {
            return children.Cast<object>();
        }

        if (GetValue(item, "Children") is IEnumerable childProperty)
        {
            return childProperty.Cast<object>();
        }

        return Array.Empty<object>();
    }

    private object? TryGetItemById(object id)
    {
        foreach (var method in _libraryManager.GetType().GetMethods().Where(m => m.Name is "GetItemById" or "GetItemByIdAsync"))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            try
            {
                return method.Invoke(_libraryManager, new[] { id });
            }
            catch
            {
                // Try the next overload.
            }
        }

        return null;
    }

    private InventoryItem MapItem(object item, InventoryLibrary library, ExportOptions options, IReadOnlyList<User> users, IReadOnlyDictionary<(Guid UserId, Guid ItemId), UserItemData> watchState)
    {
        var inventoryItem = new InventoryItem
        {
            Id = GetGuid(item, "Id").ToString("N"),
            LibraryId = library.Id,
            LibraryName = library.Name,
            Type = item.GetType().Name,
            Name = GetString(item, "Name") ?? string.Empty,
            SortName = GetString(item, "SortName"),
            OriginalTitle = GetString(item, "OriginalTitle"),
            SeriesName = GetString(item, "SeriesName"),
            SeasonName = GetString(item, "SeasonName"),
            SeasonNumber = GetInt(item, "ParentIndexNumber") ?? (item.GetType().Name == "Season" ? GetInt(item, "IndexNumber") : null),
            EpisodeNumber = GetInt(item, "IndexNumber"),
            ProductionYear = GetInt(item, "ProductionYear"),
            PremiereDate = GetDate(item, "PremiereDate"),
            RuntimeTicks = GetLong(item, "RunTimeTicks"),
            DateCreated = GetDate(item, "DateCreated"),
            Path = GetString(item, "Path"),
            ParentId = GetGuid(item, "ParentId").ToString("N"),
            SeriesId = GetGuid(item, "SeriesId").ToString("N"),
            SeasonId = GetGuid(item, "SeasonId").ToString("N"),
            OfficialRating = GetString(item, "OfficialRating"),
            CommunityRating = GetFloat(item, "CommunityRating"),
            CriticRating = GetFloat(item, "CriticRating"),
            Overview = GetString(item, "Overview")
        };

        if (options.IncludeProviderIds && GetValue(item, "ProviderIds") is IDictionary providerIds)
        {
            foreach (DictionaryEntry providerId in providerIds)
            {
                inventoryItem.ProviderIds[Convert.ToString(providerId.Key) ?? string.Empty] = Convert.ToString(providerId.Value) ?? string.Empty;
            }
        }

        MapMediaSources(item, inventoryItem, options);
        if (options.IncludeUserData)
        {
            MapUserData(item, inventoryItem, options, users, watchState);
        }

        return inventoryItem;
    }

    private void MapMediaSources(object item, InventoryItem inventoryItem, ExportOptions options)
    {
        // Videos and songs get their probed media sources, streams included, from the media source manager.
        // Series, seasons, albums, and collections fall back to one source built from the item itself.
        IEnumerable? mediaSources = item is IHasMediaSources && item is BaseItem baseItem
            ? _mediaSourceManager.GetStaticMediaSources(baseItem, false)
            : GetValue(item, "MediaSources") as IEnumerable;

        if (mediaSources is null)
        {
            var source = new InventoryMediaSource
            {
                ItemId = inventoryItem.Id,
                Id = inventoryItem.Id,
                Path = inventoryItem.Path,
                Container = GetString(item, "Container"),
                SizeBytes = GetLong(item, "Size"),
                Bitrate = GetInt(item, "TotalBitrate"),
                Width = GetInt(item, "Width"),
                Height = GetInt(item, "Height"),
                RunTimeTicks = inventoryItem.RuntimeTicks
            };
            inventoryItem.MediaSources.Add(source);
            return;
        }

        foreach (var mediaSource in mediaSources.Cast<object>())
        {
            var streams = (GetValue(mediaSource, "MediaStreams") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

            // A media source has no picture size or HDR range of its own. The first video stream has them.
            var videoStream = streams.FirstOrDefault(stream => Convert.ToString(GetValue(stream, "Type")) == "Video");
            var source = new InventoryMediaSource
            {
                ItemId = inventoryItem.Id,
                Id = GetString(mediaSource, "Id") ?? inventoryItem.Id,
                Path = GetString(mediaSource, "Path"),
                Container = GetString(mediaSource, "Container"),
                SizeBytes = GetLong(mediaSource, "Size"),
                Bitrate = GetInt(mediaSource, "Bitrate"),
                VideoType = Convert.ToString(GetValue(mediaSource, "VideoType")),
                Width = GetInt(mediaSource, "Width") ?? (videoStream is null ? null : GetInt(videoStream, "Width")),
                Height = GetInt(mediaSource, "Height") ?? (videoStream is null ? null : GetInt(videoStream, "Height")),
                VideoRange = Convert.ToString(GetValue(mediaSource, "VideoRange") ?? (videoStream is null ? null : GetValue(videoStream, "VideoRange"))),
                VideoRangeType = Convert.ToString(GetValue(mediaSource, "VideoRangeType") ?? (videoStream is null ? null : GetValue(videoStream, "VideoRangeType"))),
                IsRemote = GetBool(mediaSource, "IsRemote"),
                RunTimeTicks = GetLong(mediaSource, "RunTimeTicks")
            };

            if (options.IncludeMediaStreams)
            {
                foreach (var stream in streams)
                {
                    source.Streams.Add(MapStream(inventoryItem.Id, source.Id, stream));
                }
            }

            inventoryItem.MediaSources.Add(source);
        }
    }

    private static void MapUserData(object item, InventoryItem inventoryItem, ExportOptions options, IReadOnlyList<User> users, IReadOnlyDictionary<(Guid UserId, Guid ItemId), UserItemData> watchState)
    {
        if (item is not BaseItem baseItem)
        {
            return;
        }

        foreach (var user in users)
        {
            if (!watchState.TryGetValue((user.Id, baseItem.Id), out var userData))
            {
                continue;
            }

            inventoryItem.UserData.Add(new InventoryUserData
            {
                ItemId = inventoryItem.Id,
                UserId = options.AnonymizeUsers ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(user.Id.ToByteArray())) : user.Id.ToString("N"),
                UserName = options.AnonymizeUsers ? string.Empty : user.Username,
                Played = userData.Played,
                IsFavorite = userData.IsFavorite,
                PlayCount = userData.PlayCount,
                LastPlayedDate = userData.LastPlayedDate is { } lastPlayed ? new DateTimeOffset(DateTime.SpecifyKind(lastPlayed, DateTimeKind.Utc)) : null,
                PlaybackPositionTicks = userData.PlaybackPositionTicks
            });
        }
    }

    private static InventoryMediaStream MapStream(string itemId, string sourceId, object stream) => new()
    {
        ItemId = itemId,
        MediaSourceId = sourceId,
        Index = GetInt(stream, "Index"),
        Type = Convert.ToString(GetValue(stream, "Type")),
        Codec = GetString(stream, "Codec"),
        CodecTag = GetString(stream, "CodecTag"),
        Language = GetString(stream, "Language"),
        Title = GetString(stream, "Title"),
        DisplayTitle = GetString(stream, "DisplayTitle"),
        IsDefault = GetBool(stream, "IsDefault"),
        IsForced = GetBool(stream, "IsForced"),
        IsExternal = GetBool(stream, "IsExternal"),
        Channels = GetInt(stream, "Channels"),
        ChannelLayout = GetString(stream, "ChannelLayout"),
        SampleRate = GetInt(stream, "SampleRate"),
        Bitrate = GetInt(stream, "BitRate") ?? GetInt(stream, "Bitrate"),
        Width = GetInt(stream, "Width"),
        Height = GetInt(stream, "Height"),
        Profile = GetString(stream, "Profile"),
        Level = GetDouble(stream, "Level"),
        PixelFormat = GetString(stream, "PixelFormat"),
        AspectRatio = GetString(stream, "AspectRatio")
    };

    private static bool IsSupportedItem(object item)
    {
        var name = item.GetType().Name;
        return name is "Movie" or "Series" or "Season" or "Episode" or "MusicAlbum" or "Audio" or "Video" or "BoxSet";
    }

    private static object? GetValue(object target, string name)
    {
        try
        {
            var property = target.GetType().GetProperty(name);
            if (property is not null)
            {
                return property.GetValue(target);
            }

            var method = target.GetType().GetMethod(name, Type.EmptyTypes);
            return method?.Invoke(target, Array.Empty<object>());
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(object target, string name) => Convert.ToString(GetValue(target, name));
    private static IEnumerable<string> GetStringArray(object target, string name) => GetValue(target, name) is IEnumerable values ? values.Cast<object>().Select(value => Convert.ToString(value) ?? string.Empty) : Array.Empty<string>();
    private static Guid GetLibraryGuid(object target)
    {
        var itemId = GetGuid(target, "ItemId");
        return itemId == Guid.Empty ? GetGuid(target, "Id") : itemId;
    }

    private static Guid GetGuid(object target, string name) => Guid.TryParse(Convert.ToString(GetValue(target, name)), out var guid) ? guid : Guid.Empty;
    private static int? GetInt(object target, string name) => int.TryParse(Convert.ToString(GetValue(target, name)), out var value) ? value : null;
    private static long? GetLong(object target, string name) => long.TryParse(Convert.ToString(GetValue(target, name)), out var value) ? value : null;
    private static float? GetFloat(object target, string name) => float.TryParse(Convert.ToString(GetValue(target, name)), out var value) ? value : null;
    private static double? GetDouble(object target, string name) => double.TryParse(Convert.ToString(GetValue(target, name)), out var value) ? value : null;
    private static bool GetBool(object target, string name) => bool.TryParse(Convert.ToString(GetValue(target, name)), out var value) && value;
    private static DateTimeOffset? GetDate(object target, string name) => DateTimeOffset.TryParse(Convert.ToString(GetValue(target, name)), out var value) ? value : null;
}

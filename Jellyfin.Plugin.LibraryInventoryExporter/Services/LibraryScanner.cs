using System.Collections;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;
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

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<LibraryScanner> _logger;

    public LibraryScanner(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, ILogger<LibraryScanner> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    public Task<InventoryManifest> ScanAsync(ExportOptions options, Action<string, int, int> reportProgress, CancellationToken cancellationToken)
    {
        var manifest = new InventoryManifest
        {
            GeneratedAt = DateTimeOffset.UtcNow,
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
        var processed = 0;
        _logger.LogInformation("Starting inventory scan for {LibraryCount} libraries. Selected library filter count: {SelectedLibraryCount}", libraries.Count, options.LibraryIds.Count);

        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var libraryId = GetLibraryGuid(library).ToString("N");
            var inventoryLibrary = new InventoryLibrary
            {
                Id = libraryId,
                Name = GetString(library, "Name") ?? "Unknown",
                CollectionType = GetString(library, "CollectionType")
            };

            var items = GetItems(library, allItems).Where(IsSupportedItem).ToList();
            _logger.LogInformation("Inventory scan library {LibraryName} ({LibraryId}) resolved {ItemCount} supported items", inventoryLibrary.Name, inventoryLibrary.Id, items.Count);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                reportProgress("Scanning library " + inventoryLibrary.Name, processed, items.Count);
                inventoryLibrary.Items.Add(MapItem(item, inventoryLibrary, options));
            }

            manifest.Libraries.Add(inventoryLibrary);
        }

        _logger.LogInformation("Scanned {LibraryCount} libraries and {ItemCount} items for inventory export", manifest.Libraries.Count, manifest.Libraries.Sum(l => l.Items.Count));
        return Task.FromResult(manifest);
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

    private InventoryItem MapItem(object item, InventoryLibrary library, ExportOptions options)
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
            MapUserData(item, inventoryItem, options);
        }

        return inventoryItem;
    }

    private static void MapMediaSources(object item, InventoryItem inventoryItem, ExportOptions options)
    {
        var mediaSources = GetValue(item, "MediaSources") as IEnumerable;
        if (mediaSources is null && Invoke(item, "GetMediaSources") is IEnumerable methodSources)
        {
            mediaSources = methodSources;
        }

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
            var source = new InventoryMediaSource
            {
                ItemId = inventoryItem.Id,
                Id = GetString(mediaSource, "Id") ?? inventoryItem.Id,
                Path = GetString(mediaSource, "Path"),
                Container = GetString(mediaSource, "Container"),
                SizeBytes = GetLong(mediaSource, "Size"),
                Bitrate = GetInt(mediaSource, "Bitrate"),
                VideoType = Convert.ToString(GetValue(mediaSource, "VideoType")),
                Width = GetInt(mediaSource, "Width"),
                Height = GetInt(mediaSource, "Height"),
                VideoRange = Convert.ToString(GetValue(mediaSource, "VideoRange")),
                VideoRangeType = Convert.ToString(GetValue(mediaSource, "VideoRangeType")),
                IsRemote = GetBool(mediaSource, "IsRemote"),
                RunTimeTicks = GetLong(mediaSource, "RunTimeTicks")
            };

            if (options.IncludeMediaStreams && GetValue(mediaSource, "MediaStreams") is IEnumerable streams)
            {
                foreach (var stream in streams.Cast<object>())
                {
                    source.Streams.Add(MapStream(inventoryItem.Id, source.Id, stream));
                }
            }

            inventoryItem.MediaSources.Add(source);
        }
    }

    private void MapUserData(object item, InventoryItem inventoryItem, ExportOptions options)
    {
        var users = InvokeEnumerable(_userManager, "GetUsers").ToList();
        foreach (var user in users)
        {
            var userId = GetGuid(user, "Id");
            var userData = Invoke(_userDataManager, "GetUserData", userId, inventoryItem.Id);
            if (userData is null)
            {
                continue;
            }

            inventoryItem.UserData.Add(new InventoryUserData
            {
                ItemId = inventoryItem.Id,
                UserId = options.AnonymizeUsers ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(userId.ToByteArray())) : userId.ToString("N"),
                UserName = options.AnonymizeUsers ? string.Empty : GetString(user, "Username") ?? GetString(user, "Name") ?? string.Empty,
                Played = GetBool(userData, "Played"),
                IsFavorite = GetBool(userData, "IsFavorite"),
                PlayCount = GetInt(userData, "PlayCount") ?? 0,
                LastPlayedDate = GetDate(userData, "LastPlayedDate"),
                PlaybackPositionTicks = GetLong(userData, "PlaybackPositionTicks") ?? 0
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

    private static object? Invoke(object target, string methodName, params object?[] arguments)
    {
        return target.GetType().GetMethods().FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == arguments.Length)?.Invoke(target, arguments);
    }

    private static IEnumerable<object> InvokeEnumerable(object target, string methodName)
    {
        return Invoke(target, methodName) is IEnumerable values ? values.Cast<object>() : Array.Empty<object>();
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

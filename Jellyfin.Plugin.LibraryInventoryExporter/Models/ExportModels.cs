namespace Jellyfin.Plugin.LibraryInventoryExporter.Models;

public enum ExportFormat
{
    Csv,
    Json
}

public sealed class ExportOptions
{
    public IReadOnlyList<ExportFormat> Formats { get; init; } = new[] { ExportFormat.Csv, ExportFormat.Json };
    public IReadOnlyList<Guid> LibraryIds { get; init; } = Array.Empty<Guid>();
    public bool IncludeUserData { get; init; }
    public bool IncludeMediaStreams { get; init; } = true;
    public bool IncludeProviderIds { get; init; } = true;
    public bool CompressOutput { get; init; } = true;
    public bool AnonymizeUsers { get; init; }
}

public sealed class ExportRequest
{
    public string[]? Formats { get; set; }
    public Guid[]? LibraryIds { get; set; }
    public bool? IncludeUserData { get; set; }
    public bool? IncludeMediaStreams { get; set; }
    public bool? CompressOutput { get; set; }
}

public sealed class ExportStartResponse
{
    public string ExportId { get; init; } = string.Empty;
    public string Status { get; init; } = "running";
}

public sealed class ExportStatus
{
    public bool IsRunning { get; set; }
    public string? ExportId { get; set; }
    public string Stage { get; set; } = "Idle";
    public double ProgressPercent { get; set; }
    public int ProcessedItems { get; set; }
    public int TotalItems { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ExportHistoryEntry
{
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<string> Formats { get; init; } = Array.Empty<string>();
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int ItemCount { get; init; }
    public int LibraryCount { get; init; }
    public string Status { get; init; } = "completed";
    public bool IncludesUserData { get; init; }
}

public sealed class LibraryOption
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CollectionType { get; init; }
}

public sealed class InventoryManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset GeneratedAt { get; init; }
    public InventoryServer Server { get; init; } = new();
    public InventoryExportOptions ExportOptions { get; init; } = new();
    public List<InventoryLibrary> Libraries { get; } = new();
}

public sealed class InventoryServer
{
    public string Name { get; init; } = "Jellyfin";
    public string Version { get; init; } = string.Empty;
}

public sealed class InventoryExportOptions
{
    public bool IncludeUserData { get; init; }
    public bool IncludeMediaStreams { get; init; }
    public bool IncludeProviderIds { get; init; }
}

public sealed class InventoryLibrary
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CollectionType { get; init; }
    public List<InventoryItem> Items { get; } = new();
}

public sealed class InventoryItem
{
    public string Id { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public string LibraryName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? SortName { get; init; }
    public string? OriginalTitle { get; init; }
    public string? SeriesName { get; init; }
    public string? SeasonName { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public int? ProductionYear { get; init; }
    public DateTimeOffset? PremiereDate { get; init; }
    public long? RuntimeTicks { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public string? Path { get; init; }
    public string? ParentId { get; init; }
    public string? SeriesId { get; init; }
    public string? SeasonId { get; init; }
    public string? OfficialRating { get; init; }
    public float? CommunityRating { get; init; }
    public float? CriticRating { get; init; }
    public string? Overview { get; init; }
    public Dictionary<string, string> ProviderIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<InventoryMediaSource> MediaSources { get; } = new();
    public List<InventoryUserData> UserData { get; } = new();
}

public sealed class InventoryMediaSource
{
    public string ItemId { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? Path { get; init; }
    public string? Container { get; init; }
    public long? SizeBytes { get; init; }
    public int? Bitrate { get; init; }
    public string? VideoType { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? VideoRange { get; init; }
    public string? VideoRangeType { get; init; }
    public bool IsRemote { get; init; }
    public long? RunTimeTicks { get; init; }
    public List<InventoryMediaStream> Streams { get; } = new();
}

public sealed class InventoryMediaStream
{
    public string ItemId { get; init; } = string.Empty;
    public string MediaSourceId { get; init; } = string.Empty;
    public int? Index { get; init; }
    public string? Type { get; init; }
    public string? Codec { get; init; }
    public string? CodecTag { get; init; }
    public string? Language { get; init; }
    public string? Title { get; init; }
    public string? DisplayTitle { get; init; }
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public bool IsExternal { get; init; }
    public int? Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public int? SampleRate { get; init; }
    public int? Bitrate { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Profile { get; init; }
    public double? Level { get; init; }
    public string? PixelFormat { get; init; }
    public string? AspectRatio { get; init; }
}

public sealed class InventoryUserData
{
    public string ItemId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public bool Played { get; init; }
    public bool IsFavorite { get; init; }
    public int PlayCount { get; init; }
    public DateTimeOffset? LastPlayedDate { get; init; }
    public long PlaybackPositionTicks { get; init; }
}

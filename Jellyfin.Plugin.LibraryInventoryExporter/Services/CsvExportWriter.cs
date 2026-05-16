using System.Globalization;
using System.Text;
using Jellyfin.Plugin.LibraryInventoryExporter.Models;

namespace Jellyfin.Plugin.LibraryInventoryExporter.Services;

public sealed class CsvExportWriter
{
    public async Task WriteAsync(InventoryManifest manifest, string directory, bool includeUserData, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await WriteItemsAsync(manifest, Path.Combine(directory, "items.csv"), cancellationToken).ConfigureAwait(false);
        await WriteMediaSourcesAsync(manifest, Path.Combine(directory, "media_sources.csv"), cancellationToken).ConfigureAwait(false);
        await WriteMediaStreamsAsync(manifest, Path.Combine(directory, "media_streams.csv"), cancellationToken).ConfigureAwait(false);
        await WriteProviderIdsAsync(manifest, Path.Combine(directory, "provider_ids.csv"), cancellationToken).ConfigureAwait(false);
        if (includeUserData)
        {
            await WriteUserDataAsync(manifest, Path.Combine(directory, "user_data.csv"), cancellationToken).ConfigureAwait(false);
        }
    }

    public static string Escape(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (text.Contains('"', StringComparison.Ordinal) || text.Contains(',', StringComparison.Ordinal) || text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal))
        {
            return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }

    private static async Task WriteItemsAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("item_id,library_id,library_name,item_type,name,sort_name,original_title,series_name,season_name,season_number,episode_number,production_year,premiere_date,runtime_ticks,date_created,path,parent_id,series_id,season_id,official_rating,community_rating,critic_rating,overview").ConfigureAwait(false);
        foreach (var item in manifest.Libraries.SelectMany(l => l.Items))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Row(item.Id, item.LibraryId, item.LibraryName, item.Type, item.Name, item.SortName, item.OriginalTitle, item.SeriesName, item.SeasonName, item.SeasonNumber, item.EpisodeNumber, item.ProductionYear, item.PremiereDate, item.RuntimeTicks, item.DateCreated, item.Path, item.ParentId, item.SeriesId, item.SeasonId, item.OfficialRating, item.CommunityRating, item.CriticRating, item.Overview)).ConfigureAwait(false);
        }
    }

    private static async Task WriteMediaSourcesAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("item_id,media_source_id,path,container,size_bytes,bitrate,video_type,width,height,video_range,video_range_type,is_remote,run_time_ticks").ConfigureAwait(false);
        foreach (var source in manifest.Libraries.SelectMany(l => l.Items).SelectMany(i => i.MediaSources))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Row(source.ItemId, source.Id, source.Path, source.Container, source.SizeBytes, source.Bitrate, source.VideoType, source.Width, source.Height, source.VideoRange, source.VideoRangeType, source.IsRemote, source.RunTimeTicks)).ConfigureAwait(false);
        }
    }

    private static async Task WriteMediaStreamsAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("item_id,media_source_id,stream_index,stream_type,codec,codec_tag,language,title,display_title,is_default,is_forced,is_external,channels,channel_layout,sample_rate,bitrate,width,height,profile,level,pixel_format,aspect_ratio").ConfigureAwait(false);
        foreach (var streamInfo in manifest.Libraries.SelectMany(l => l.Items).SelectMany(i => i.MediaSources).SelectMany(s => s.Streams))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Row(streamInfo.ItemId, streamInfo.MediaSourceId, streamInfo.Index, streamInfo.Type, streamInfo.Codec, streamInfo.CodecTag, streamInfo.Language, streamInfo.Title, streamInfo.DisplayTitle, streamInfo.IsDefault, streamInfo.IsForced, streamInfo.IsExternal, streamInfo.Channels, streamInfo.ChannelLayout, streamInfo.SampleRate, streamInfo.Bitrate, streamInfo.Width, streamInfo.Height, streamInfo.Profile, streamInfo.Level, streamInfo.PixelFormat, streamInfo.AspectRatio)).ConfigureAwait(false);
        }
    }

    private static async Task WriteProviderIdsAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("item_id,provider,provider_id").ConfigureAwait(false);
        foreach (var item in manifest.Libraries.SelectMany(l => l.Items))
        {
            foreach (var providerId in item.ProviderIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(Row(item.Id, providerId.Key, providerId.Value)).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteUserDataAsync(InventoryManifest manifest, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("item_id,user_id,user_name,played,is_favorite,play_count,last_played_date,playback_position_ticks").ConfigureAwait(false);
        foreach (var userData in manifest.Libraries.SelectMany(l => l.Items).SelectMany(i => i.UserData))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Row(userData.ItemId, userData.UserId, userData.UserName, userData.Played, userData.IsFavorite, userData.PlayCount, userData.LastPlayedDate, userData.PlaybackPositionTicks)).ConfigureAwait(false);
        }
    }

    private static string Row(params object?[] values) => string.Join(",", values.Select(Escape));
}

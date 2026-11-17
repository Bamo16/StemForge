using System.Text.Json.Nodes;

namespace StemForge.Core.Downloading;

/// <summary>Reads the two YouTube Music InnerTube responses the Art Track lookup needs.</summary>
internal static class InnerTubeMusicParser
{
    /// <summary>The first queue item of a <c>next</c> response: the video that was asked about.</summary>
    public static MusicEntry? ParseWatchedVideo(JsonNode? response) =>
        FindAll(response, "playlistPanelVideoRenderer").FirstOrDefault() is { } item
            ? ToEntry(
                item,
                item["title"]?["runs"]?[0]?["text"]?.GetValue<string>(),
                item["longBylineText"]?["runs"]?[0]?["text"]?.GetValue<string>(),
                item["lengthText"]?["runs"]?[0]?["text"]?.GetValue<string>(),
                item["videoId"]?.GetValue<string>()
            )
            : null;

    /// <summary>Every song row of a <c>search</c> response filtered to Songs.</summary>
    public static IEnumerable<MusicEntry> ParseSongs(JsonNode? response) =>
        FindAll(response, "musicResponsiveListItemRenderer")
            .Select(row =>
            {
                var columns = row["flexColumns"]?.AsArray();
                var details = columns?.Count > 1 ? ColumnTexts(columns[1]) : [];

                // Songs read "Artist • Album • 3:20", so the artist is first and the length last.
                return ToEntry(
                    row,
                    ColumnTexts(columns?[0]).FirstOrDefault(),
                    details.FirstOrDefault(),
                    details.LastOrDefault(),
                    row["playlistItemData"]?["videoId"]?.GetValue<string>()
                );
            })
            .OfType<MusicEntry>();

    internal static int? ParseLength(string? text) =>
        text?.Split(':') is { Length: > 1 } parts && parts.All(part => int.TryParse(part, out _))
            ? parts.Aggregate(0, (total, part) => total * 60 + int.Parse(part))
            : null;

    private static List<string> ColumnTexts(JsonNode? column) =>
        column?["musicResponsiveListItemFlexColumnRenderer"]?["text"]?["runs"] is JsonArray runs
            ? [.. runs.Select(run => run?["text"]?.GetValue<string>() ?? string.Empty)]
            : [];

    private static MusicEntry? ToEntry(
        JsonNode node,
        string? title,
        string? artist,
        string? length,
        string? videoId
    ) =>
        videoId is { Length: > 0 } && title is { Length: > 0 } && ParseLength(length) is { } seconds
            ? new MusicEntry(
                videoId,
                title,
                artist ?? string.Empty,
                seconds,
                FindAll(node, "musicVideoType")
                    .Any(type => type.GetValue<string>() == "MUSIC_VIDEO_TYPE_ATV")
            )
            : null;

    private static IEnumerable<JsonNode> FindAll(JsonNode? node, string key)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (name == key && child is not null)
                        yield return child;

                    foreach (var found in FindAll(child, key))
                        yield return found;
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    foreach (var found in FindAll(child, key))
                        yield return found;
                }

                break;
        }
    }
}

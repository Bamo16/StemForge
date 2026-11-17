using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StemForge.Core.Downloading;

/// <summary>The song upload that matches a video, with how far its length is from the video's.</summary>
public sealed record ArtTrackSuggestion(
    string VideoId,
    string Title,
    string Artist,
    int LengthSeconds,
    int LengthDifferenceSeconds
);

public interface IArtTrackFinder
{
    /// <summary>
    /// Looks the video up on YouTube Music without signing in and returns its Art Track, or null
    /// when none matches. Throws <see cref="HttpRequestException"/> when YouTube cannot be reached.
    /// </summary>
    Task<ArtTrackSuggestion?> FindAsync(string videoId, CancellationToken ct = default);
}

public sealed class ArtTrackFinder(IHttpClientFactory factory) : IArtTrackFinder
{
    internal const string HttpClientName = "innertube";

    // The "Songs" filter of a YouTube Music search.
    private const string SongsFilter = "EgWKAQIIAWoMEA4QChADEAQQCRAF";

    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(1100);

    private readonly IHttpClientFactory _factory = factory;

    public async Task<ArtTrackSuggestion?> FindAsync(string videoId, CancellationToken ct = default)
    {
        var http = _factory.CreateClient(HttpClientName);

        var watch = await PostAsync(
            http,
            "next",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["playlistId"] = "RDAMVM" + videoId,
                ["isAudioOnly"] = true,
                ["enablePersistentPlaylistPanel"] = true,
                ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
            },
            ct
        );

        // A video that is already the song has no other half to suggest.
        if (InnerTubeMusicParser.ParseWatchedVideo(watch) is not { IsArtTrack: false } video)
            return null;

        // The pause keeps the two requests polite; YouTube Music is not an API we have a deal with.
        await Task.Delay(RequestGap, ct);
        var search = await PostAsync(
            http,
            "search",
            new JsonObject
            {
                ["query"] = $"{video.Artist} {ArtTrackMatcher.WithoutOfficialSuffix(video.Title)}",
                ["params"] = SongsFilter,
            },
            ct
        );

        return ArtTrackMatcher.TryPick(video, InnerTubeMusicParser.ParseSongs(search), out var pick)
            ? pick
            : null;
    }

    private static async Task<JsonNode?> PostAsync(
        HttpClient http,
        string endpoint,
        JsonObject body,
        CancellationToken ct
    )
    {
        body["context"] = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = "1.20251110.03.00",
                ["hl"] = "en",
            },
        };

        using var response = await http.PostAsJsonAsync($"youtubei/v1/{endpoint}", body, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
    }
}

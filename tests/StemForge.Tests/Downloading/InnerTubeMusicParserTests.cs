using System.Text.Json.Nodes;

namespace StemForge.Tests.Downloading;

public sealed class InnerTubeMusicParserTests
{
    private static JsonNode? Fixture(string name) =>
        JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "InnerTube", name))
        );

    [Theory]
    [InlineData("3:20", 200)]
    [InlineData("0:05", 5)]
    [InlineData("1:02:03", 3723)]
    [InlineData("10:00", 600)]
    public void ParseLength_ClockText_GivesSeconds(string text, int seconds) =>
        Assert.Equal(seconds, InnerTubeMusicParser.ParseLength(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("200")]
    [InlineData("3:xx")]
    [InlineData("LIVE")]
    [InlineData("3:")]
    public void ParseLength_NotAClock_IsNull(string? text) =>
        Assert.Null(InnerTubeMusicParser.ParseLength(text));

    [Fact]
    public void ParseWatchedVideo_RecordedNextResponse_ReadsTheVideoAsNotAnArtTrack()
    {
        var video = InnerTubeMusicParser.ParseWatchedVideo(Fixture("next-streetpop.json"));

        Assert.NotNull(video);
        Assert.Equal("-pj24dQYGRc", video.VideoId);
        Assert.False(video.IsArtTrack);
        Assert.True(video.LengthSeconds > 0);
        Assert.NotEmpty(video.Title);
    }

    [Fact]
    public void ParseWatchedVideo_FirstQueueItemWins()
    {
        var response = JsonNode.Parse(
            """
            { "a": { "playlistPanelVideoRenderer": {
                "videoId": "vid1",
                "title": { "runs": [ { "text": "Song" } ] },
                "longBylineText": { "runs": [ { "text": "Someone" } ] },
                "lengthText": { "runs": [ { "text": "2:30" } ] }
            } },
            "b": { "playlistPanelVideoRenderer": { "videoId": "vid2" } } }
            """
        );

        var video = InnerTubeMusicParser.ParseWatchedVideo(response);

        Assert.Equal(new MusicEntry("vid1", "Song", "Someone", 150, false), video);
    }

    [Fact]
    public void ParseWatchedVideo_NoLength_IsNull()
    {
        var response = JsonNode.Parse(
            """
            { "playlistPanelVideoRenderer": {
                "videoId": "vid1",
                "title": { "runs": [ { "text": "Song" } ] }
            } }
            """
        );

        Assert.Null(InnerTubeMusicParser.ParseWatchedVideo(response));
    }

    [Fact]
    public void ParseWatchedVideo_NullOrEmptyResponse_IsNull()
    {
        Assert.Null(InnerTubeMusicParser.ParseWatchedVideo(null));
        Assert.Null(InnerTubeMusicParser.ParseWatchedVideo(JsonNode.Parse("{}")));
    }

    [Fact]
    public void ParseWatchedVideo_ArtTrackMarker_IsDetected()
    {
        var response = JsonNode.Parse(
            """
            { "playlistPanelVideoRenderer": {
                "videoId": "vid1",
                "title": { "runs": [ { "text": "Song" } ] },
                "lengthText": { "runs": [ { "text": "1:00" } ] },
                "navigationEndpoint": { "watchEndpoint": { "watchEndpointMusicSupportedConfigs": {
                    "watchEndpointMusicConfig": { "musicVideoType": "MUSIC_VIDEO_TYPE_ATV" } } } }
            } }
            """
        );

        Assert.True(InnerTubeMusicParser.ParseWatchedVideo(response)?.IsArtTrack);
    }

    [Fact]
    public void ParseSongs_RecordedSearchResponse_YieldsArtTrackRows()
    {
        var songs = InnerTubeMusicParser.ParseSongs(Fixture("search-streetpop.json")).ToList();

        Assert.Contains(songs, song => song is { VideoId: "LcTV5ceBb_I", IsArtTrack: true });
    }

    [Fact]
    public void ParseSongs_RowTakesArtistFromSecondColumnFirstRunAndLengthFromLast()
    {
        var response = JsonNode.Parse(
            """
            { "musicResponsiveListItemRenderer": {
                "playlistItemData": { "videoId": "vid9" },
                "flexColumns": [
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Title" } ] } } },
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
                        { "text": "Artist" }, { "text": " • " }, { "text": "Album" }, { "text": " • " }, { "text": "3:20" }
                    ] } } }
                ]
            } }
            """
        );

        var song = Assert.Single(InnerTubeMusicParser.ParseSongs(response));

        Assert.Equal(new MusicEntry("vid9", "Title", "Artist", 200, false), song);
    }

    [Fact]
    public void ParseSongs_RowsWithoutVideoIdOrLength_AreDropped()
    {
        var response = JsonNode.Parse(
            """
            { "rows": [
                { "musicResponsiveListItemRenderer": { "flexColumns": [
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "NoId" } ] } } },
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "A" }, { "text": "1:00" } ] } } } ] } },
                { "musicResponsiveListItemRenderer": { "playlistItemData": { "videoId": "v" }, "flexColumns": [
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "NoLength" } ] } } } ] } }
            ] }
            """
        );

        Assert.Empty(InnerTubeMusicParser.ParseSongs(response));
    }
}

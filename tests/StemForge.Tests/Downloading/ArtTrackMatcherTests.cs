using System.Text.Json.Nodes;

namespace StemForge.Tests.Downloading;

public sealed class ArtTrackMatcherTests
{
    private static JsonNode? Fixture(string name) =>
        JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "InnerTube", name))
        );

    private static MusicEntry Video(string title = "STREETPOP", int length = 200) =>
        new("-pj24dQYGRc", title, "Wävė", length, IsArtTrack: false);

    private static MusicEntry Song(
        string videoId,
        string title = "STREETPOP",
        int length = 200,
        bool isArtTrack = true
    ) => new(videoId, title, "Wävė", length, isArtTrack);

    [Fact]
    public void A_recorded_search_yields_the_art_track_within_the_window()
    {
        var video = InnerTubeMusicParser.ParseWatchedVideo(Fixture("next-streetpop.json"));
        var songs = InnerTubeMusicParser.ParseSongs(Fixture("search-streetpop.json"));

        Assert.NotNull(video);
        Assert.True(ArtTrackMatcher.TryPick(video, songs, out var pick));
        Assert.Equal(
            new ArtTrackSuggestion("LcTV5ceBb_I", "STREETPOP (feat. RedDash)", "Wävė", 200, 0),
            pick
        );
    }

    [Theory]
    [InlineData(202, true)]
    [InlineData(198, true)]
    [InlineData(203, false)]
    [InlineData(197, false)]
    public void A_song_counts_only_within_two_seconds_of_the_video(int songLength, bool matches) =>
        Assert.Equal(
            matches,
            ArtTrackMatcher.TryPick(Video(), [Song("aaaaaaaaaaa", length: songLength)], out _)
        );

    [Fact]
    public void A_song_that_is_not_an_art_track_is_ignored() =>
        Assert.False(
            ArtTrackMatcher.TryPick(Video(), [Song("aaaaaaaaaaa", isArtTrack: false)], out _)
        );

    [Fact]
    public void No_candidates_gives_no_suggestion() =>
        Assert.False(ArtTrackMatcher.TryPick(Video(), [], out _));

    [Fact]
    public void A_different_title_is_not_a_match_even_at_the_same_length() =>
        Assert.False(
            ArtTrackMatcher.TryPick(Video(), [Song("aaaaaaaaaaa", title: "Sweet Time")], out _)
        );

    [Fact]
    public void Brackets_and_case_are_ignored_when_comparing_titles()
    {
        Assert.True(
            ArtTrackMatcher.TryPick(
                Video("Out Of My Head (Official Video)"),
                [Song("aaaaaaaaaaa", "Out of My Head (feat. Someone)")],
                out var pick
            )
        );
        Assert.Equal("aaaaaaaaaaa", pick.VideoId);
    }

    [Fact]
    public void The_closest_length_wins_and_the_difference_is_signed()
    {
        Assert.True(
            ArtTrackMatcher.TryPick(
                Video(),
                [Song("far11111111", length: 202), Song("near2222222", length: 199)],
                out var pick
            )
        );
        Assert.Equal("near2222222", pick.VideoId);
        Assert.Equal(-1, pick.LengthDifferenceSeconds);
    }

    [Theory]
    [InlineData("Song (Official Video)", "Song")]
    [InlineData("Song [Official Audio]", "Song")]
    [InlineData("Song (feat. Someone)", "Song (feat. Someone)")]
    public void The_search_text_drops_a_trailing_official_bracket(string title, string expected) =>
        Assert.Equal(expected, ArtTrackMatcher.WithoutOfficialSuffix(title));
}

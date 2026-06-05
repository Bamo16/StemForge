using StemForge.Helpers;
using StemForge.Models;

namespace StemForge.Tests.Helpers;

public sealed class AudioTaggerTests
{
    private static SourceTagInfo UrlSource() =>
        new()
        {
            Title = "Artist - Track",
            Artist = "Artist",
            SourceUrl = "https://www.youtube.com/watch?v=abc123",
            SourceCodec = "opus",
            SourceBitrateKbps = 160,
            SourceFormatId = "251",
        };

    [Fact]
    public void BuildProvenance_UrlJob_IncludesAllSourceFields()
    {
        var provenance = AudioTagger.BuildProvenance(UrlSource(), "Vocal - Full", "0.2.0");

        Assert.Contains("stemforge/0.2.0", provenance);
        Assert.Contains("preset: Vocal - Full", provenance);
        Assert.Contains("date: ", provenance);
        Assert.Contains("source: https://www.youtube.com/watch?v=abc123", provenance);
        Assert.Contains("codec: opus", provenance);
        Assert.Contains("bitrate: 160 kbps", provenance);
        Assert.Contains("format-id: 251", provenance);
    }

    [Fact]
    public void BuildProvenance_DescriptorIsLabeledPresetNotModel()
    {
        // #24: the descriptor field is now per-output preset info, labeled "preset:" not "model:".
        var provenance = AudioTagger.BuildProvenance(null, "Instrumental - Full", "0.2.0");

        Assert.Contains("preset: Instrumental - Full", provenance);
        Assert.DoesNotContain("model:", provenance);
    }

    [Fact]
    public void BuildProvenance_LocalFileJob_OmitsSourceFields()
    {
        // A local-file job has no URL/codec/bitrate/format-id — only title/artist + cover.
        var localSource = new SourceTagInfo { Title = "Track", Artist = "Artist" };

        var provenance = AudioTagger.BuildProvenance(localSource, "Vocal - Full", "0.2.0");

        Assert.Contains("stemforge/0.2.0", provenance);
        Assert.Contains("preset: Vocal - Full", provenance);
        Assert.DoesNotContain("source:", provenance);
        Assert.DoesNotContain("codec:", provenance);
        Assert.DoesNotContain("bitrate:", provenance);
        Assert.DoesNotContain("format-id:", provenance);
    }

    [Fact]
    public void BuildProvenance_NullSource_DoesNotThrow()
    {
        var provenance = AudioTagger.BuildProvenance(null, null, "0.2.0");

        Assert.Equal($"stemforge/0.2.0 | date: {DateTimeOffset.UtcNow:yyyy-MM-dd}", provenance);
    }

    [Fact]
    public void ReadAudioProperties_NonexistentPath_ReturnsAllNulls()
    {
        var (codec, bitrate, sampleRate, duration) = AudioTagger.ReadAudioProperties(
            "/nonexistent/path/file.flac"
        );

        Assert.Null(codec);
        Assert.Null(bitrate);
        Assert.Null(sampleRate);
        Assert.Null(duration);
    }

    [Fact]
    public void ReadAudioProperties_EmptyPath_ReturnsAllNulls()
    {
        var (codec, bitrate, sampleRate, duration) = AudioTagger.ReadAudioProperties(string.Empty);

        Assert.Null(codec);
        Assert.Null(bitrate);
        Assert.Null(sampleRate);
        Assert.Null(duration);
    }

    [Fact]
    public void FromYtDlpMetadata_UrlJob_PopulatesSourceProvenanceFields()
    {
        var meta = new YtDlpMetadata(
            SourceUrl: "https://www.youtube.com/watch?v=abc123",
            Title: "Track",
            Artist: "Artist",
            Uploader: "Uploader",
            SourceCodec: "opus",
            SourceBitrateKbps: 160,
            DurationSeconds: 200,
            FormatId: "251",
            MediaUrl: "https://media.example.com/audio"
        );

        var info = AudioTagger.FromYtDlpMetadata(meta, thumbPath: null);

        Assert.Equal("https://www.youtube.com/watch?v=abc123", info.SourceUrl);
        Assert.Equal("opus", info.SourceCodec);
        Assert.Equal(160, info.SourceBitrateKbps);
        Assert.Equal("251", info.SourceFormatId);
        Assert.Equal("Artist - Track", info.Title);
    }
}

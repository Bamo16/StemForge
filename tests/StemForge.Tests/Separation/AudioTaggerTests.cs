namespace StemForge.Tests.Separation;

public sealed class AudioTaggerTests : IDisposable
{
    // Minimal valid FLAC: magic + last-metadata STREAMINFO block (all-zero stream info).
    // TagLibSharp can open and Save() this file without errors.
    private static readonly byte[] _minimalFlac =
    [
        0x66,
        0x4C,
        0x61,
        0x43,
        0x80,
        0x00,
        0x00,
        0x22,
        .. new byte[34],
    ];

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"sftest-tagger-{Guid.NewGuid():N}"
    );

    public AudioTaggerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private string CreateFlacFile()
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.flac");
        File.WriteAllBytes(path, _minimalFlac);
        return path;
    }

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
    public void WithSourceUrl_FileWithItsOwnSource_ReplacesIt()
    {
        var info = new SourceTagInfo { Title = "Track", SourceUrl = "https://example.com/page" };

        var result = AudioTagger.WithSourceUrl(info, "track:file~3fa9c0d12be84471");

        Assert.Equal("track:file~3fa9c0d12be84471", result?.SourceUrl);
        Assert.Equal("Track", result?.Title);
    }

    [Fact]
    public void WithSourceUrl_FileWithNoTags_StillCarriesIt()
    {
        var provenance = AudioTagger.BuildProvenance(
            AudioTagger.WithSourceUrl(null, "track:file~3fa9c0d12be84471"),
            null,
            "0.2.0"
        );

        Assert.Contains("source: track:file~3fa9c0d12be84471", provenance);
    }

    [Fact]
    public void WithSourceUrl_NoOverride_LeavesTheInfoAlone()
    {
        var info = new SourceTagInfo { SourceUrl = "https://example.com/page" };

        Assert.Same(info, AudioTagger.WithSourceUrl(info, null));
        Assert.Null(AudioTagger.WithSourceUrl(null, string.Empty));
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
            Artists: ["Artist"],
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

    [Fact]
    public void ReadFromFile_AfterApplyToFileWithUrlSource_RecoversSourceProvenance()
    {
        // #4 of the KeyBPM handoff: a file downloaded via a URL job, then fed back in as a
        // local-file input (e.g. `download` followed by `separate` on the saved path), must not
        // silently lose its original SOURCE_URL/codec/bitrate/format-id.
        var path = CreateFlacFile();
        AudioTagger.ApplyToFile(path, UrlSource(), "Vocal - Full", "0.2.0");

        var recovered = AudioTagger.ReadFromFile(path);

        Assert.NotNull(recovered);
        Assert.Equal("https://www.youtube.com/watch?v=abc123", recovered.SourceUrl);
        Assert.Equal("opus", recovered.SourceCodec);
        Assert.Equal(160, recovered.SourceBitrateKbps);
        Assert.Equal("251", recovered.SourceFormatId);
    }

    [Fact]
    public void ReadFromFile_AfterApplyToFileWithLocalSource_HasNoSourceProvenance()
    {
        // A stem produced from a local-file job never had exact-source fields to begin with;
        // re-reading it must degrade to null rather than fabricate provenance.
        var path = CreateFlacFile();
        var localSource = new SourceTagInfo { Title = "Track", Artist = "Artist" };
        AudioTagger.ApplyToFile(path, localSource, "Vocal - Full", "0.2.0");

        var recovered = AudioTagger.ReadFromFile(path);

        Assert.NotNull(recovered);
        Assert.Null(recovered.SourceUrl);
        Assert.Null(recovered.SourceCodec);
        Assert.Null(recovered.SourceBitrateKbps);
        Assert.Null(recovered.SourceFormatId);
    }

    [Fact]
    public void ReadFromFile_UnrelatedComment_DoesNotMisparseAsProvenance()
    {
        // A file with an ordinary user/DAW comment (not written by this tool) must not have
        // its Comment text misinterpreted as provenance fields.
        var path = CreateFlacFile();
        using (var f = TagLib.File.Create(path))
        {
            f.Tag.Comment = "source: my personal notes, not a URL";
            f.Save();
        }

        var recovered = AudioTagger.ReadFromFile(path);

        Assert.NotNull(recovered);
        Assert.Null(recovered.SourceUrl);
    }

    // ── A file another process holds open ─────────────────────────────────────

    private static FileStream HoldOpen(string path) =>
        new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public async Task A_file_held_open_is_tagged_once_the_holder_lets_go()
    {
        var path = CreateFlacFile();
        var holder = HoldOpen(path);
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                await holder.DisposeAsync();
            },
            TestContext.Current.CancellationToken
        );

        AudioTagger.ApplyToFile(
            path,
            UrlSource(),
            "Vocal - Full",
            "0.2.0",
            [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(200), 5)]
        );
        await release;

        Assert.Equal(
            "https://www.youtube.com/watch?v=abc123",
            AudioTagger.ReadFromFile(path)?.SourceUrl
        );
    }

    [Fact]
    public void A_file_held_open_throughout_fails_after_every_retry()
    {
        var path = CreateFlacFile();
        TimeSpan[] delays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)];
        var clock = System.Diagnostics.Stopwatch.StartNew();

        AudioTagException failure;
        using (HoldOpen(path))
            failure = Assert.Throws<AudioTagException>(() =>
                AudioTagger.ApplyToFile(path, UrlSource(), "Vocal - Full", "0.2.0", delays)
            );

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(100), "it gave up without retrying");
        Assert.Contains(Path.GetFileName(path), failure.Message);
        Assert.Null(AudioTagger.ReadFromFile(path)?.SourceUrl);
    }

    [Fact]
    public void A_source_held_open_throughout_cannot_be_read()
    {
        var path = CreateFlacFile();

        using (HoldOpen(path))
            Assert.Throws<AudioTagException>(() =>
                AudioTagger.ReadFromFile(path, [TimeSpan.FromMilliseconds(10)])
            );
    }
}

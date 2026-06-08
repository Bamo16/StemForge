using System.Text.Json;
using StemForge.Core.Models;

namespace StemForge.Tests.Models;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_GpuVariant_IsCpu()
    {
        var s = new AppSettings();
        Assert.Equal(GpuVariant.Cpu, s.GpuVariant);
    }

    [Fact]
    public void Defaults_FirstRunComplete_IsFalse()
    {
        Assert.False(new AppSettings().FirstRunComplete);
    }

    [Fact]
    public void Defaults_PathsAreNull()
    {
        var s = new AppSettings();
        Assert.Null(s.UvPath);
        Assert.Null(s.AudioSeparatorPath);
        Assert.Null(s.YtdlpPath);
        Assert.Null(s.FfmpegPath);
        Assert.Null(s.OutputDirectory);
        Assert.Null(s.ModelsDirectory);
    }

    [Fact]
    public void MigrateLegacyToolPaths_DrainsLegacyPropsIntoOverrides()
    {
        var s = new AppSettings { UvPath = @"C:\Tools\uv.exe", YtdlpPath = @"C:\Tools\yt-dlp.exe" };

        s.MigrateLegacyToolPaths();

        Assert.Equal(@"C:\Tools\uv.exe", s.GetToolPathOverride(ToolKind.Uv));
        Assert.Equal(@"C:\Tools\yt-dlp.exe", s.GetToolPathOverride(ToolKind.Ytdlp));
        Assert.Null(s.GetToolPathOverride(ToolKind.Ffmpeg));
        // Legacy properties cleared so the next save writes only the new shape.
        Assert.Null(s.UvPath);
        Assert.Null(s.YtdlpPath);
    }

    [Fact]
    public void MigrateLegacyToolPaths_DoesNotClobberExistingOverride()
    {
        var s = new AppSettings { UvPath = @"C:\old\uv.exe" };
        s.SetToolPathOverride(ToolKind.Uv, @"C:\new\uv.exe");

        s.MigrateLegacyToolPaths();

        Assert.Equal(@"C:\new\uv.exe", s.GetToolPathOverride(ToolKind.Uv));
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"stemforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "settings.json");

        try
        {
            var original = new AppSettings
            {
                GpuVariant = GpuVariant.Cuda,
                OutputDirectory = @"C:\Music\Stems",
                ModelsDirectory = @"C:\Models",
                YtdlpPath = @"C:\Tools\yt-dlp.exe",
                FfmpegPath = @"C:\Tools\ffmpeg.exe",
                FirstRunComplete = true,
            };

            await File.WriteAllTextAsync(
                path,
                System.Text.Json.JsonSerializer.Serialize(
                    original,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters =
                        {
                            new System.Text.Json.Serialization.JsonStringEnumConverter(),
                        },
                    }
                ),
                TestContext.Current.CancellationToken
            );

            var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                json,
                new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                }
            )!;

            Assert.Equal(GpuVariant.Cuda, loaded.GpuVariant);
            Assert.Equal(@"C:\Music\Stems", loaded.OutputDirectory);
            Assert.Equal(@"C:\Models", loaded.ModelsDirectory);
            Assert.Equal(@"C:\Tools\yt-dlp.exe", loaded.YtdlpPath);
            Assert.Equal(@"C:\Tools\ffmpeg.exe", loaded.FfmpegPath);
            Assert.True(loaded.FirstRunComplete);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SourceGenContext_SerializesEnumsAsStrings()
    {
        var original = new AppSettings
        {
            GpuVariant = GpuVariant.Cuda,
            DrumStemLocation = DrumStemLocation.CacheOnly,
            DefaultAudioFormat = AudioFormat.Flac,
        };
        original.SetToolPathOverride(ToolKind.Ytdlp, @"C:\Tools\yt-dlp.exe");

        var json = JsonSerializer.Serialize(original, AppSettingsJsonContext.Default.AppSettings);

        // Enums (including the dictionary key) serialize by name, not by numeric value, and the
        // output is indented.
        Assert.Contains("\"DrumStemLocation\": \"CacheOnly\"", json);
        Assert.Contains("\"GpuVariant\": \"Cuda\"", json);
        Assert.Contains("\"Ytdlp\":", json);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void SourceGenContext_RoundTripsEnumsAndOverrides()
    {
        var original = new AppSettings
        {
            GpuVariant = GpuVariant.Cuda,
            DrumStemLocation = DrumStemLocation.CacheOnly,
            DefaultAudioFormat = AudioFormat.Flac,
        };
        original.SetToolPathOverride(ToolKind.Ytdlp, @"C:\Tools\yt-dlp.exe");

        var json = JsonSerializer.Serialize(original, AppSettingsJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)!;

        Assert.Equal(GpuVariant.Cuda, loaded.GpuVariant);
        Assert.Equal(DrumStemLocation.CacheOnly, loaded.DrumStemLocation);
        Assert.Equal(AudioFormat.Flac, loaded.DefaultAudioFormat);
        Assert.Equal(@"C:\Tools\yt-dlp.exe", loaded.GetToolPathOverride(ToolKind.Ytdlp));
    }
}

using StemForge.Models;

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
}

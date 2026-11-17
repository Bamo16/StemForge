using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using StemForge.Cli.Json;

namespace StemForge.Cli.Commands;

internal sealed class ArtTrackCommand : AsyncCommand<ArtTrackCommand.Settings>
{
    /// <summary>The <c>art-track</c> output: <c>Found</c> false means YouTube Music has no matching song.</summary>
    private sealed record ArtTrackResult(
        bool Found,
        string? VideoId,
        string? Title,
        string? Artist,
        int? LengthSeconds,
        int? LengthDifferenceSeconds
    );

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<videoId>")]
        public string VideoId { get; set; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        if (
            !YtUrlHelper.TryNormalize(settings.VideoId, out var normalized)
            || !normalized.StartsWith(
                "https://music.youtube.com/watch?v=",
                StringComparison.Ordinal
            )
        )
        {
            Console.Error.WriteLine("Expected a YouTube video id or link.");

            return 1;
        }

        var services = new ServiceCollection();
        services.AddStemForgeCore();
        await using var provider = services.BuildServiceProvider();

        try
        {
            var found = await provider
                .GetRequiredService<IArtTrackFinder>()
                .FindAsync(
                    normalized["https://music.youtube.com/watch?v=".Length..],
                    cancellationToken
                );

            CliJson.Write(
                found is null
                    ? new ArtTrackResult(false, null, null, null, null, null)
                    : new ArtTrackResult(
                        true,
                        found.VideoId,
                        found.Title,
                        found.Artist,
                        found.LengthSeconds,
                        found.LengthDifferenceSeconds
                    )
            );

            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }
    }
}

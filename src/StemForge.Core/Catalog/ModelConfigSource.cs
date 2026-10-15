using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace StemForge.Core.Catalog;

/// <summary>
/// Reads a model's stems from its yaml config: from the models directory, then StemForge's cache,
/// then downloaded (the config only) from where audio-separator would get it.
/// </summary>
public sealed partial class ModelConfigSource(
    IHttpClientFactory factory,
    AppPaths paths,
    string? cacheDirectory = null
) : IModelConfigSource
{
    // The URLs audio-separator's Separator.download_model_files tries, in its order.
    private const string UvrConfigs =
        "https://github.com/TRvlvr/model_repo/releases/download/all_public_uvr_models/mdx_model_data/mdx_c_configs";
    private const string UvrVipConfigs =
        "https://github.com/Anjok0109/ai_magic/releases/download/v5/mdx_model_data/mdx_c_configs";
    private const string AudioSeparatorConfigs =
        "https://github.com/nomadkaraoke/python-audio-separator/releases/download/model-configs";

    private readonly IHttpClientFactory _factory = factory;
    private readonly AppPaths _paths = paths;
    private readonly string _cacheDirectory = cacheDirectory ?? paths.ModelConfigCacheDirectory;

    // Answers that won't change this session: stems read from a config, or null when every URL
    // said the config doesn't exist. A network failure is not an answer and is never stored.
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>?> _answers = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Set by the first network failure, so an offline session doesn't wait on every model in turn.
    private volatile bool _offline;

    [GeneratedRegex(@"^\s*instruments:\s*(\[(?<Flow>[^\]]*)\])?\s*$", RegexOptions.ExplicitCapture)]
    private static partial Regex InstrumentsKey { get; }

    [GeneratedRegex(@"^\s*-\s*(?<Item>.+?)\s*$", RegexOptions.ExplicitCapture)]
    private static partial Regex BlockItem { get; }

    public async Task<IReadOnlyList<string>?> TryGetConfigStemsAsync(
        ModelInfo model,
        CancellationToken ct = default
    )
    {
        if (model.Files.FirstOrDefault(IsConfig) is not { } config)
            return null;

        if (_answers.TryGetValue(config, out var known))
            return known;

        if (ReadLocal(config) is { } local)
            return _answers[config] = ParseInstruments(local);

        if (_offline)
            return null;

        try
        {
            var fetched = await FetchAsync(model, config, ct).ConfigureAwait(false);
            if (fetched is not null)
                SaveToCache(config, fetched);

            return _answers[config] = fetched is null ? null : ParseInstruments(fetched);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            _offline = true;
            AppLogger.Debug("ModelConfig", $"Config fetch failed, not retrying: {ex.Message}");

            return null;
        }
    }

    /// <summary>The lowercased <c>instruments</c> list from a model config, in flow or block style.</summary>
    internal static IReadOnlyList<string>? ParseInstruments(string yaml)
    {
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (InstrumentsKey.Match(lines[i].TrimEnd('\r')) is not { Success: true } key)
                continue;

            var items = key.Groups["Flow"] is { Success: true } flow
                ? flow.Value.Split(',')
                : lines
                    .Skip(i + 1)
                    .Select(line => BlockItem.Match(line.TrimEnd('\r')))
                    .TakeWhile(item => item.Success)
                    .Select(item => item.Groups["Item"].Value);

            List<string> stems =
            [
                .. items
                    .Select(item => item.Trim().Trim('\'', '"').ToLowerInvariant())
                    .Where(item => item.Length > 0),
            ];

            return stems.Count > 0 ? stems : null;
        }

        return null;
    }

    private static bool IsConfig(string file) =>
        file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private string? ReadLocal(string config)
    {
        foreach (var dir in (string[])[_paths.ModelsDirectory, _cacheDirectory])
        {
            if (new FileInfo(Path.Combine(dir, config)) is { Exists: true } file)
                return File.ReadAllText(file.FullName);
        }

        return null;
    }

    private async Task<string?> FetchAsync(ModelInfo model, string config, CancellationToken ct)
    {
        var uvr = model.FriendlyName.Contains("VIP") ? UvrVipConfigs : UvrConfigs;
        var http = _factory.CreateClient("model-config");

        foreach (var prefix in (string[])[uvr, AudioSeparatorConfigs])
        {
            using var response = await http.GetAsync($"{prefix}/{config}", ct)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        return null;
    }

    private void SaveToCache(string config, string yaml)
    {
        var temp = Path.Combine(_cacheDirectory, $"{config}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(temp, yaml);
            File.Move(temp, Path.Combine(_cacheDirectory, config), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Debug("ModelConfig", $"Could not cache {config}: {ex.Message}");
            File.Delete(temp);
        }
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace StemForge.Services;

/// <summary>
/// Result of a version check against the GitHub Releases API.
/// </summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion);

/// <summary>
/// Fetches the latest release tag from a remote source without making real network calls in tests.
/// </summary>
public interface IReleaseFetcher
{
    /// <summary>
    /// Returns the latest release tag (e.g. "v0.3.0"), or <see langword="null"/> if the check
    /// failed or no releases exist.
    /// </summary>
    Task<string?> FetchLatestTagAsync(CancellationToken ct = default);
}

/// <summary>
/// Compares the running version against the latest GitHub release and reports whether an update is
/// available. All failures are swallowed: a network error, rate-limit response, or malformed tag
/// all result in <c>UpdateAvailable = false</c>.
/// </summary>
public sealed class UpdateCheckService(IAppInfo appInfo, IReleaseFetcher fetcher)
{
    /// <summary>
    /// Performs the version check. Never throws; returns <c>UpdateAvailable = false</c> on any
    /// error so callers need not handle exceptions.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var tag = await fetcher.FetchLatestTagAsync(ct).ConfigureAwait(false);
            if (tag is null)
                return new UpdateCheckResult(false, null);

            // Strip a leading "v" that is conventional in GitHub release tags (e.g. "v0.2.0").
            var normalized = tag.TrimStart('v', 'V');

            if (!Version.TryParse(normalized, out var latestVersion))
                return new UpdateCheckResult(false, null);

            if (!Version.TryParse(appInfo.ShortVersion, out var runningVersion))
                return new UpdateCheckResult(false, null);

            var updateAvailable = latestVersion > runningVersion;
            return new UpdateCheckResult(updateAvailable, normalized);
        }
        catch
        {
            return new UpdateCheckResult(false, null);
        }
    }
}

/// <summary>
/// Fetches the latest release tag from the GitHub Releases API for a given owner/repo.
/// </summary>
public sealed class GitHubReleaseFetcher : IReleaseFetcher
{
    // Repository coordinates. A single constant here is acceptable: this is the app's own identity
    // and does not vary at runtime.
    internal const string RepoOwner = "Bamo16";
    internal const string RepoName = "StemForge";

    private static readonly string ApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private readonly HttpClient _http;

    public GitHubReleaseFetcher(IAppInfo appInfo)
    {
        _http = new HttpClient();
        // GitHub API requires a User-Agent header; use the app name and version.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"{appInfo.ProductName}/{appInfo.ShortVersion}"
        );
        // Accept header for the GitHub REST API v3.
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        // Short timeout: this is a best-effort background check on startup.
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<string?> FetchLatestTagAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http
                .GetFromJsonAsync<GitHubReleaseResponse>(ApiUrl, ct)
                .ConfigureAwait(false);
            return response?.TagName;
        }
        catch
        {
            return null;
        }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }
    }
}

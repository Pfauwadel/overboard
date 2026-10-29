using System.Text.Json;

namespace Overboard.Distribution;

public sealed record Release(string Version, string Url);

/// <summary>
/// Asks GitHub for the latest published release. Only works while the repository is
/// public: GitHub answers "not found" to anonymous requests on a private one.
/// </summary>
public static class UpdateChecker
{
    /// <returns>The latest release if newer than this build, otherwise null (also when GitHub cannot be reached).</returns>
    public static async Task<Release?> FindNewerReleaseAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Overboard/{AppInfo.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using var response = await http.GetAsync($"https://api.github.com/repos/{AppInfo.Repository}/releases/latest");
            if (!response.IsSuccessStatusCode) return null;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = json.RootElement.GetProperty("html_url").GetString() ?? AppInfo.ReleasesUrl;
            var latest = tag.TrimStart('v');
            return IsNewer(latest, AppInfo.Version) ? new Release(latest, url) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Compares x.y.z versions; anything else (pre-releases, dev builds) never counts as newer.</summary>
    public static bool IsNewer(string candidate, string current) =>
        System.Version.TryParse(candidate, out var a) && System.Version.TryParse(current, out var b) && a > b;
}

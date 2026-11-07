using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Calisto;

public sealed record UpdateResult(bool IsNewer, string? LatestVersion, string? DownloadUrl, string? Error);

/// <summary>Asks the GitHub releases API for the newest tag and compares it with the running version.</summary>
public static class UpdateChecker
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Current.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public static async Task<UpdateResult> CheckAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains("/OWNER/"))
            return new UpdateResult(false, null, null,
                "No update source is configured yet. Set the GitHub release URL in Settings.");
        try
        {
            using var response = await Http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return new UpdateResult(false, null, null, $"Server answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rootEl = doc.RootElement;
            string tag = rootEl.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string? page = rootEl.TryGetProperty("html_url", out var h) ? h.GetString() : null;

            // Prefer a direct .exe asset when the release has one.
            if (rootEl.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var dl))
                    { page = dl.GetString() ?? page; break; }
                }
            }

            var m = Regex.Match(tag, @"\d+(\.\d+){1,3}");
            if (!m.Success || !Version.TryParse(m.Value, out var latest))
                return new UpdateResult(false, null, null, $"Could not read a version number from release tag '{tag}'.");

            var current = AppInfo.Current;
            bool newer = Normalize(latest) > Normalize(current);
            return new UpdateResult(newer, latest.ToString(3), page, null);
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, null, null, ex.Message);
        }
    }

    private static Version Normalize(Version v) =>
        new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), 0);
}

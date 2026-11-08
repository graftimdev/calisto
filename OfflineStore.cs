using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calisto;

/// <summary>
/// Everything that works without internet: downloaded dictionary packs, the community contributions file,
/// and a cache of definitions looked up earlier on this PC.
/// </summary>
public static class OfflineStore
{
    public sealed class PackEntry { [JsonPropertyName("d")] public List<string> Definitions { get; set; } = new(); }
    public sealed class Pack
    {
        [JsonPropertyName("lang")] public string Lang { get; set; } = "en";
        [JsonPropertyName("built")] public string Built { get; set; } = "";
        [JsonPropertyName("entries")] public Dictionary<string, List<string>> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    public sealed class ContributionEntry
    {
        [JsonPropertyName("word")] public string Word { get; set; } = "";
        [JsonPropertyName("definition")] public string Definition { get; set; } = "";
        [JsonPropertyName("language")] public string Language { get; set; } = "en";
        [JsonPropertyName("by")] public string? By { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
    }
    public sealed class CacheEntry { public string Text { get; set; } = ""; public string Source { get; set; } = ""; public DateTime Saved { get; set; } }

    private static readonly List<Pack> Packs = new();
    private static List<ContributionEntry> Contributions = new();
    private static Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static string PacksDir => Path.Combine(Settings.Folder, "packs");
    private static string ContributionsPath => Path.Combine(Settings.Folder, "contributions.json");
    private static string CachePath => Path.Combine(Settings.Folder, "cache.json");

    public static bool HasPack(string lang) => Packs.Any(p => p.Lang == lang);
    public static int PackEntryCount(string lang) => Packs.FirstOrDefault(p => p.Lang == lang)?.Entries.Count ?? 0;
    public static int ContributionCount => Contributions.Count;
    public static int CachedCount => Cache.Count;

    public static void Load()
    {
        try
        {
            if (Directory.Exists(PacksDir))
                foreach (var f in Directory.GetFiles(PacksDir, "pack-*.json"))
                {
                    var p = JsonSerializer.Deserialize<Pack>(File.ReadAllText(f));
                    if (p is not null) Packs.Add(p);
                }
        }
        catch { }
        try { if (File.Exists(ContributionsPath)) Contributions = JsonSerializer.Deserialize<List<ContributionEntry>>(File.ReadAllText(ContributionsPath)) ?? new(); } catch { }
        try { if (File.Exists(CachePath)) Cache = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(CachePath)) ?? new(StringComparer.OrdinalIgnoreCase); } catch { }
    }

    /// <summary>Candidates from packs and community contributions for a term.</summary>
    public static IEnumerable<Candidate> Candidates(string term)
    {
        foreach (var p in Packs)
            if (p.Entries.TryGetValue(term, out var defs))
                foreach (var d in defs) yield return new Candidate(d, d, $"Offline dictionary ({p.Lang})", p.Lang);
        foreach (var c in Contributions)
            if (string.Equals(c.Word, term, StringComparison.OrdinalIgnoreCase))
                yield return new Candidate(c.Definition, c.Definition, "Community contribution", c.Language);
    }

    public static LookupResult? Cached(string term, string lang) =>
        Cache.TryGetValue(lang + "|" + term, out var e) ? new LookupResult(e.Text, e.Source + " (saved earlier)") : null;

    public static void Remember(string term, string lang, LookupResult result)
    {
        lock (Gate)
        {
            Cache[lang + "|" + term] = new CacheEntry { Text = result.Text, Source = result.Source, Saved = DateTime.UtcNow };
            if (Cache.Count > 5000)
                foreach (var old in Cache.OrderBy(k => k.Value.Saved).Take(500).Select(k => k.Key).ToList()) Cache.Remove(old);
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(Cache));
            }
            catch { }
        }
    }

    /// <summary>Downloads the community contributions file once a day.</summary>
    public static async Task SyncContributionsAsync(bool force = false)
    {
        var last = App.Settings.ContributionsSyncedUtc;
        if (!force && last is not null && DateTime.UtcNow - last < TimeSpan.FromHours(24)) return;
        try
        {
            var json = await Http.GetStringAsync(AppInfo.ContributionsUrl);
            var list = JsonSerializer.Deserialize<List<ContributionEntry>>(json);
            if (list is null) return;
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllText(ContributionsPath, json);
            Contributions = list;
            App.Settings.ContributionsSyncedUtc = DateTime.UtcNow;
            App.Settings.Save();
        }
        catch { /* offline or file not published yet */ }
    }

    /// <summary>Downloads the English dictionary pack from the latest release. Reports progress 0..1.</summary>
    public static async Task<string?> DownloadPackAsync(string lang, IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            var url = AppInfo.PackDownloadUrl.Replace("pack-en.json", $"pack-{lang}.json");
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return $"No pack for '{lang}' is published yet ({(int)resp.StatusCode}).";
            var total = resp.Content.Headers.ContentLength ?? -1;
            Directory.CreateDirectory(PacksDir);
            var tmp = Path.Combine(PacksDir, $"pack-{lang}.json.part");
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            var pack = JsonSerializer.Deserialize<Pack>(File.ReadAllText(tmp));
            if (pack is null || pack.Entries.Count == 0) { File.Delete(tmp); return "The downloaded file is not a valid dictionary pack."; }
            var final = Path.Combine(PacksDir, $"pack-{lang}.json");
            File.Move(tmp, final, overwrite: true);
            Packs.RemoveAll(p => p.Lang == lang);
            Packs.Add(pack);
            return null;
        }
        catch (OperationCanceledException) { return "Download cancelled."; }
        catch (Exception ex) { return ex.Message; }
    }

    public static void DeletePack(string lang)
    {
        try { File.Delete(Path.Combine(PacksDir, $"pack-{lang}.json")); } catch { }
        Packs.RemoveAll(p => p.Lang == lang);
    }
}

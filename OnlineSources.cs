using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Calisto;

/// <summary>A possible meaning of an abbreviation, from any source.</summary>
public sealed record Candidate(string Expansion, string Gloss, string Source, string Lang);

/// <summary>Free, keyless meaning sources: Wiktionary and Wikipedia in the language of the surrounding text (and English).</summary>
public static class OnlineSources
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        // Wikimedia requires a descriptive user agent with a way to reach the operator.
        var project = AppInfo.DefaultUpdateUrl.Replace("https://api.github.com/repos/", "https://github.com/").Replace("/releases/latest", "");
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            $"Calisto/{AppInfo.Current.ToString(3)} (desktop abbreviation lookup; +{project}) .NET/{Environment.Version.Major}");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    /// <summary>Set when the last request was refused by a rate limiter, so the UI can say so.</summary>
    public static volatile bool RateLimited;

    /// <summary>Queries all sources in parallel and returns every candidate found (unranked).</summary>
    public static async Task<List<Candidate>> FetchAsync(string abbr, string lang, CancellationToken ct)
    {
        RateLimited = false;
        var langs = lang == "en" ? new[] { "en" } : new[] { lang, "en" };
        var tasks = new List<Task<List<Candidate>>>();
        foreach (var l in langs)
        {
            tasks.Add(Safe(() => WiktionaryAsync(abbr, l, ct)));
            tasks.Add(Safe(() => WikipediaAsync(abbr, l, ct)));
        }
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    /// <summary>
    /// Fetches a Wikipedia intro for candidates that only have a bare expansion, so the ranker can compare their
    /// vocabulary with the surrounding text ("Prime minister" mentions parliament; "Private message" mentions online).
    /// </summary>
    public static async Task<List<Candidate>> EnrichAsync(List<Candidate> candidates, string lang, CancellationToken ct)
    {
        var bare = candidates
            .Where(c => c.Gloss.Length <= c.Expansion.Length + 25 && c.Expansion.Length <= 60)
            .Select(c => c.Expansion).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        if (bare.Count == 0) return candidates;

        var langs = lang == "en" ? new[] { "en" } : new[] { lang, "en" };
        var intros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in langs)
        {
            try
            {
                foreach (var (title, intro) in await IntrosAsync(bare, l, ct))
                    if (!intros.ContainsKey(title)) intros[title] = intro;
            }
            catch { }
        }

        return candidates.Select(c =>
            intros.TryGetValue(StripParens(c.Expansion), out var intro) && intro.Length > c.Gloss.Length
                ? c with { Gloss = c.Gloss == c.Expansion ? intro : c.Gloss + " " + intro }
                : c).ToList();
    }

    private static async Task<Dictionary<string, string>> IntrosAsync(List<string> titles, string lang, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = string.Join("|", titles.Select(t => Uri.EscapeDataString(StripParens(t))));
        var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&exsentences=3&redirects=1&format=json&titles={query}";
        var json = await GetAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var q)) return result;

        // Map resolved titles back to what we asked for (redirects and capitalisation normalisation).
        var back = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in new[] { "normalized", "redirects" })
            if (q.TryGetProperty(kind, out var arr))
                foreach (var e in arr.EnumerateArray())
                {
                    var from = e.GetProperty("from").GetString() ?? ""; var to = e.GetProperty("to").GetString() ?? "";
                    back[to] = back.TryGetValue(from, out var orig) ? orig : from;
                }

        if (q.TryGetProperty("pages", out var pages))
            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("extract", out var ex)) continue;
                var title = page.Value.GetProperty("title").GetString() ?? "";
                var text = (ex.GetString() ?? "").Trim();
                if (text.Length == 0 || Regex.IsMatch(text, @"may refer to|может означать|может относиться", RegexOptions.IgnoreCase)) continue;
                var asked = back.TryGetValue(title, out var o) ? o : title;
                result[asked] = text.Length > 400 ? text[..400] : text;
            }
        return result;
    }

    private static async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, ct);
        if ((int)resp.StatusCode == 429 || resp.StatusCode == HttpStatusCode.Forbidden) { RateLimited = true; throw new HttpRequestException("rate limited"); }
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.StartsWith("You are making too many requests")) { RateLimited = true; throw new HttpRequestException("rate limited"); }
        return body;
    }

    private static string StripParens(string s) => Regex.Replace(s, @"\s*\(.*?\)", "").Trim();

    private static async Task<List<Candidate>> Safe(Func<Task<List<Candidate>>> f)
    {
        try { return await f(); } catch { return new List<Candidate>(); }
    }

    private static string Titles(string abbr)
    {
        var variants = new[] { abbr, abbr.ToUpperInvariant(), abbr.ToLowerInvariant(), abbr.Replace(".", "") }
            .Where(v => v.Length > 0).Distinct();
        return string.Join("|", variants.Select(Uri.EscapeDataString));
    }

    // ---------------- Wiktionary ----------------

    private static readonly Regex DefOf = new(
        @"(?:initialism|abbreviation|acronym|abbr\.?|short|clipping|contraction|сокращение|сокр\.?|аббревиатура|abkürzung|abréviation|sigle|abreviatura|sigla)\s*(?:of|for|from|от|für|de|von)?\s*[:\-–—]?\s*(?<x>[^.;\n(◆◇□]{2,90})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Section headings that Wiktionary's plain-text extract sometimes emits as bare lines.
    private static readonly HashSet<string> HeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Noun", "Verb", "Adjective", "Adverb", "Proper noun", "Interjection", "Phrase", "Abbreviation", "Initialism", "Acronym", "Symbol",
        "Etymology", "Pronunciation", "Anagrams", "References", "See also", "Synonyms", "Antonyms", "Translations", "Derived terms",
        "Related terms", "Usage notes", "Further reading", "Hyponyms", "Hypernyms", "Quotations", "Declension", "Alternative forms",
        "Aussprache", "Bedeutungen", "Herkunft", "Beispiele", "Übersetzungen", "Worttrennung", "Abkürzung", "Substantiv", "Oberbegriffe",
        "Charakteristische Wortkombinationen", "Wortbildungen", "Referenzen und weiterführende Informationen", "Sinnverwandte Wörter",
        "Значение", "Синонимы", "Антонимы", "Гиперонимы", "Гипонимы", "Произношение", "Этимология", "Перевод", "Библиография", "Семантические свойства",
        "Морфологические и синтаксические свойства", "Родственные слова", "Фразеологизмы и устойчивые сочетания", "Существительное", "Прилагательное",
        "Prononciation", "Étymologie", "Nom commun", "Pronunciación", "Etimología", "Sustantivo", "Pronuncia", "Etimologia", "Sostantivo"
    };

    private static async Task<List<Candidate>> WiktionaryAsync(string abbr, string lang, CancellationToken ct)
    {
        var url = $"https://{lang}.wiktionary.org/w/api.php?action=query&prop=extracts&explaintext=1&redirects=1&format=json&titles={Titles(abbr)}";
        using var doc = JsonDocument.Parse(await GetAsync(url, ct));
        var list = new List<Candidate>();
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return list;
        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("extract", out var ex)) continue;
            var text = ex.GetString() ?? "";
            string section = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("=")) { section = line.Trim('=', ' ').ToLowerInvariant(); continue; }
                if (section is "etymology" or "pronunciation" or "anagrams" or "see also" or "references" or "произношение" or "этимология") continue;
                if (line.Length < 4 || line.Length > 240) continue;
                if (HeadingWords.Contains(line)) { section = line.ToLowerInvariant(); continue; }
                if (Regex.IsMatch(line, @"^(IPA|Rhymes|Hyphenation|Homophone|Audio|МФА|\[|,|;|\.)", RegexOptions.IgnoreCase)) continue;
                if (!char.IsLetter(line[0]) && !char.IsDigit(line[0])) continue;

                var cleaned = Regex.Replace(line, @"^\(?\d+[.)]\s*", "");
                var m = DefOf.Match(cleaned);
                string expansion = m.Success ? m.Groups["x"].Value.Trim().TrimEnd('.', ',') : FirstClause(cleaned);
                expansion = Regex.Replace(expansion, @"\s+", " ");
                if (expansion.Length < 2) continue;
                list.Add(new Candidate(Capitalize(expansion), cleaned, $"Wiktionary ({lang})", lang));
            }
        }
        return list;
    }

    // ---------------- Wikipedia ----------------

    private static async Task<List<Candidate>> WikipediaAsync(string abbr, string lang, CancellationToken ct)
    {
        var list = new List<Candidate>();
        var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&prop=extracts|pageprops&exintro=1&explaintext=1&redirects=1&format=json&titles={Titles(abbr)}";
        using (var doc = JsonDocument.Parse(await GetAsync(url, ct)))
        {
            if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    if (!page.Value.TryGetProperty("extract", out var ex)) continue;
                    var text = (ex.GetString() ?? "").Trim();
                    if (text.Length == 0) continue;
                    var title = page.Value.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    bool disamb = page.Value.TryGetProperty("pageprops", out var pp) && pp.TryGetProperty("disambiguation", out _)
                                  || Regex.IsMatch(text, @"may refer to|может означать|может относиться|kann stehen für|peut désigner|puede referirse|significati", RegexOptions.IgnoreCase);
                    if (disamb)
                    {
                        foreach (var raw in text.Split('\n').Skip(1))
                        {
                            var line = raw.Trim();
                            if (line.Length < 4 || line.Length > 240 || line.StartsWith("=")) continue;
                            // "ИИН — индивидуальный идентификационный номер" or "Bachelor of Science, an academic degree"
                            var body = Regex.Replace(line, @"^[^\s—–:-]{1,12}\s*[—–:-]\s*", "");
                            list.Add(new Candidate(Capitalize(FirstClause(body)), line, $"Wikipedia ({lang})", lang));
                        }
                    }
                    else
                    {
                        // Ordinary article: the title (or the bold lead phrase) is the expansion.
                        var first = FirstSentence(text);
                        var expansion = title.Length > 0 && !title.Equals(abbr, StringComparison.OrdinalIgnoreCase) ? title : FirstClause(first);
                        list.Add(new Candidate(Capitalize(expansion), first, $"Wikipedia ({lang})", lang));
                    }
                }
            }
        }

        // Full-text search adds pages whose title is not exactly the abbreviation.
        var surl = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&srlimit=6&format=json&srsearch={Uri.EscapeDataString(abbr)}";
        using (var doc = JsonDocument.Parse(await GetAsync(surl, ct)))
        {
            if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("search", out var hits))
            {
                foreach (var h in hits.EnumerateArray())
                {
                    var title = h.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var snippet = h.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                    snippet = WebUtility.HtmlDecode(Regex.Replace(snippet, "<.*?>", ""));
                    if (title.Length == 0 || title.Equals(abbr, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new Candidate(title, snippet, $"Wikipedia search ({lang})", lang));
                }
            }
        }
        return list;
    }

    // ---------------- helpers ----------------

    private static string FirstSentence(string text)
    {
        // A sentence ends at . ! ? followed by a capital letter or the end; "(каз. жеке...)" does not end one.
        // ...and "сокр." / "англ." / "e.g." style short lower-case tokens before the dot do not end one either.
        var m = Regex.Match(text, @"^.{20,}?(?<![\s(]\p{Ll}{1,5})[.!?](?=\s+\p{Lu}|\s*$)", RegexOptions.Singleline);
        var s = m.Success ? m.Value : text;
        return s.Length > 260 ? s[..260] + "..." : s.Trim();
    }

    private static string FirstClause(string text)
    {
        var s = Regex.Split(text, @"\s[—–]\s|[.;:(◆◇□]|,\s")[0].Trim();
        return s.Length > 90 ? s[..90] : s;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}

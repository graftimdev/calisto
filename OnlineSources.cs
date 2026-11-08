using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Calisto;

/// <summary>A possible meaning of a term, from any source. Expansion is the short answer, Gloss the longer description.</summary>
public sealed record Candidate(string Expansion, string Gloss, string Source, string Lang);

/// <summary>Free, keyless meaning sources: Wiktionary and Wikipedia in the requested languages.</summary>
public static class OnlineSources
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        // Wikimedia requires a descriptive user agent with a way to reach the operator.
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            $"Calisto/{AppInfo.Current.ToString(3)} (desktop dictionary lookup; +{AppInfo.RepoUrl}) .NET/{Environment.Version.Major}");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    /// <summary>Set when the last request was refused by a rate limiter, so the UI can say so.</summary>
    public static volatile bool RateLimited;

    /// <summary>Queries Wiktionary and Wikipedia in each language in parallel and returns every candidate found (unranked).</summary>
    public static async Task<List<Candidate>> FetchAsync(string term, IReadOnlyList<string> langs, LookupMode mode, string textLang, CancellationToken ct)
    {
        RateLimited = false;
        var tasks = new List<Task<List<Candidate>>>();
        foreach (var l in langs)
        {
            tasks.Add(Safe(() => WiktionaryAsync(term, l, mode, textLang, ct)));
            tasks.Add(Safe(() => WikipediaAsync(term, l, mode, ct)));
        }
        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    /// <summary>
    /// Fetches a Wikipedia intro for candidates that only have a bare expansion, so the ranker can compare their
    /// vocabulary with the surrounding text ("Prime minister" mentions parliament; "Private message" mentions online).
    /// </summary>
    public static async Task<List<Candidate>> EnrichAsync(List<Candidate> candidates, IReadOnlyList<string> langs, CancellationToken ct)
    {
        var bare = candidates
            .Where(c => c.Gloss.Length <= c.Expansion.Length + 25 && c.Expansion.Length <= 60)
            .Select(c => c.Expansion).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        if (bare.Count == 0) return candidates;

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
                if (text.Length == 0 || IsDisambiguationText(text)) continue;
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

    private static string Titles(string term)
    {
        var variants = new[] { term, term.ToUpperInvariant(), term.ToLowerInvariant(), Capitalize(term.ToLowerInvariant()), term.Replace(".", "") }
            .Where(v => v.Length > 0).Distinct();
        return string.Join("|", variants.Select(Uri.EscapeDataString));
    }

    // ---------------- Wiktionary ----------------

    private static readonly Regex DefOf = new(
        @"(?:initialism|abbreviation|acronym|abbr\.?|short|clipping|contraction|сокращение|сокр\.?|аббревиатура|abkürzung|abréviation|sigle|abreviatura|sigla)\s*(?:of|for|from|от|für|de|von)?\s*[:\-–—]?\s*(?<x>[^.;\n(◆◇□]{2,90})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Sections of a Wiktionary page that do not hold definitions (several languages).
    private static readonly HashSet<string> SkipSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "etymology", "pronunciation", "anagrams", "see also", "references", "synonyms", "antonyms", "translations", "derived terms",
        "related terms", "usage notes", "further reading", "hyponyms", "hypernyms", "quotations", "declension", "conjugation", "inflection",
        "alternative forms", "descendants", "coordinate terms", "statistics", "external links",
        "aussprache", "herkunft", "beispiele", "übersetzungen", "worttrennung", "oberbegriffe", "unterbegriffe", "charakteristische wortkombinationen",
        "wortbildungen", "referenzen und weiterführende informationen", "sinnverwandte wörter", "gegenwörter", "redewendungen",
        "синонимы", "антонимы", "гиперонимы", "гипонимы", "произношение", "этимология", "перевод", "библиография", "родственные слова",
        "фразеологизмы и устойчивые сочетания", "морфологические и синтаксические свойства", "анаграммы", "метаграммы",
        "prononciation", "étymologie", "traductions", "synonymes", "antonymes", "références", "voir aussi",
        "pronunciación", "etimología", "traducciones", "sinónimos", "antónimos", "véase también", "pronuncia", "etimologia", "traduzione", "sinonimi"
    };

    private static async Task<List<Candidate>> WiktionaryAsync(string term, string wikiLang, LookupMode mode, string textLang, CancellationToken ct)
    {
        // English Wiktionary has a structured definitions endpoint: clean senses, no quotations or etymology to filter out.
        if (wikiLang == "en")
        {
            var structured = await WiktionaryEnStructuredAsync(term, mode, textLang, ct);
            if (structured.Count > 0) return structured;
        }
        return await WiktionaryExtractAsync(term, wikiLang, mode, textLang, ct);
    }

    private static async Task<List<Candidate>> WiktionaryEnStructuredAsync(string term, LookupMode mode, string textLang, CancellationToken ct)
    {
        var list = new List<Candidate>();
        var variants = new[] { term, term.ToLowerInvariant(), mode == LookupMode.Abbreviation ? term.ToUpperInvariant() : Capitalize(term.ToLowerInvariant()) }
            .Distinct().Take(2).ToList();
        var bodies = await Task.WhenAll(variants.Select(async v =>
        {
            try { return await GetAsync($"https://en.wiktionary.org/api/rest_v1/page/definition/{Uri.EscapeDataString(v)}?redirect=true", ct); }
            catch { return ""; }
        }));
        foreach (var body in bodies.Where(b => b.Length > 0))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
            foreach (var langEntry in doc.RootElement.EnumerateObject())
            {
                // Keys are the language codes of the word's language. In word mode, only the text's language (or English) counts.
                if (mode == LookupMode.Word && langEntry.Name != textLang && langEntry.Name != "en") continue;
                var senseLang = langEntry.Name == "en" ? "en" : "en";   // definitions are written in English either way
                foreach (var pos in langEntry.Value.EnumerateArray())
                {
                    var partOfSpeech = pos.TryGetProperty("partOfSpeech", out var p) ? p.GetString() ?? "" : "";
                    if (!pos.TryGetProperty("definitions", out var defs)) continue;
                    foreach (var d in defs.EnumerateArray())
                    {
                        var html = d.TryGetProperty("definition", out var dd) ? dd.GetString() ?? "" : "";
                        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<.*?>", "")).Trim();
                        text = Regex.Replace(text, @"\s+", " ");
                        if (text.Length < 3) continue;
                        if (mode == LookupMode.Abbreviation)
                        {
                            var m = DefOf.Match(text);
                            string expansion = m.Success ? m.Groups["x"].Value.Trim().TrimEnd('.', ',') : FirstClause(text);
                            if (expansion.Length < 2) continue;
                            list.Add(new Candidate(Capitalize(expansion), text, "Wiktionary (en)", senseLang));
                        }
                        else
                        {
                            var sense = text.Length > 200 ? text[..200] + "..." : text;
                            var gloss = partOfSpeech.Length > 0 ? $"({partOfSpeech.ToLowerInvariant()}) {sense}" : sense;
                            list.Add(new Candidate(Capitalize(sense), gloss, "Wiktionary (en)", langEntry.Name == "en" ? "en" : textLang));
                        }
                    }
                }
            }
        }
        return list;
    }

    private static async Task<List<Candidate>> WiktionaryExtractAsync(string term, string wikiLang, LookupMode mode, string textLang, CancellationToken ct)
    {
        // Full-page extracts come back for one page per request, so each spelling variant is fetched separately.
        var variants = (mode == LookupMode.Word
                ? new[] { term, term.ToLowerInvariant(), Capitalize(term.ToLowerInvariant()) }
                : new[] { term, term.ToUpperInvariant(), term.Replace(".", "") })
            .Where(v => v.Length > 0).Distinct().Take(3).ToList();
        var pages = await Task.WhenAll(variants.Select(async v =>
        {
            try
            {
                var url = $"https://{wikiLang}.wiktionary.org/w/api.php?action=query&prop=extracts&explaintext=1&redirects=1&format=json&titles={Uri.EscapeDataString(v)}";
                using var doc = JsonDocument.Parse(await GetAsync(url, ct));
                if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var ps)) return "";
                foreach (var page in ps.EnumerateObject())
                    if (page.Value.TryGetProperty("extract", out var ex)) return ex.GetString() ?? "";
                return "";
            }
            catch { return ""; }
        }));

        var list = new List<Candidate>();
        // In word mode only the section for the text's language counts ("== English ==" on en.wiktionary, "== Русский ==" on ru.wiktionary).
        string? wantedSection = mode == LookupMode.Word ? LanguageDetector.SectionName(wikiLang, textLang) : null;

        foreach (var text in pages.Where(p => p.Length > 0).Distinct())
        {
            string section = "", language = "";
            bool anyLanguageHeading = text.Contains("\n== ") || text.StartsWith("== ");
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("="))
                {
                    int level = line.TakeWhile(ch => ch == '=').Count();
                    var name = line.Trim('=', ' ');
                    if (level <= 2) { language = name; section = ""; }
                    else section = Regex.Replace(name.ToLowerInvariant(), @"\s*\d+$", "");   // "Etymology 2" -> "etymology"
                    continue;
                }
                // Quotations start with a year ("2019, VOA Learning English ...").
                if (Regex.IsMatch(line, @"^\d{4}\b")) continue;
                if (wantedSection is not null && anyLanguageHeading && !language.Equals(wantedSection, StringComparison.OrdinalIgnoreCase)) continue;
                if (SkipSections.Contains(section)) continue;
                if (line.Length < 4 || line.Length > 260) continue;
                if (Regex.IsMatch(line, @"^(IPA|Rhymes|Hyphenation|Homophone|Audio|МФА|\[|,|;|\.)", RegexOptions.IgnoreCase)) continue;
                if (!char.IsLetter(line[0]) && !char.IsDigit(line[0])) continue;
                // Headword lines such as "apple (plural apples)", "Landesregierung f (genitive ...)" or "bs (uncountable)".
                if (Regex.IsMatch(line, "^" + Regex.Escape(term) + @"\s*(\p{L}{1,3}\s*)?(\(|$|,|;)", RegexOptions.IgnoreCase)) continue;
                // Part-of-speech and inflection lines ("Междомет.", "Сущ., неодуш.", "Noun", "f pl").
                if (Regex.IsMatch(line, @"^(Междомет|Сущ|Прил|Глаг|Нареч|Част|Союз|Предл|Местоим|Числ|Межд|Прич|Дееприч)\.", RegexOptions.IgnoreCase)) continue;
                // Inline labels that are not definitions ("Synonyms: ...", "See also", "Usage notes", "Синонимы: ...").
                if (Regex.IsMatch(line, @"^(Synonyms?|Antonyms?|Hyponyms?|Hypernyms?|Coordinate terms?|See also|Usage notes?|Derived terms?|Related terms?|Translations?|Alternative forms?|Near-synonyms?|Meronyms?|Holonyms?|Synonyme?|Gegenwörter|Oberbegriffe|Unterbegriffe|Beispiele|Синонимы?|Антонимы?|Гиперонимы?|Гипонимы?|Пример(ы)?)\s*[:：]", RegexOptions.IgnoreCase)) continue;
                if (line.Length < 15 && !line.Contains(' ')) continue;

                var cleaned = Regex.Replace(line, @"^\(?\d+[.)]\s*", "");
                cleaned = Regex.Split(cleaned, @"\s*[◆◇□]")[0].Trim();
                if (cleaned.Length < 3) continue;

                if (mode == LookupMode.Abbreviation)
                {
                    var m = DefOf.Match(cleaned);
                    string expansion = m.Success ? m.Groups["x"].Value.Trim().TrimEnd('.', ',') : FirstClause(cleaned);
                    expansion = Regex.Replace(expansion, @"\s+", " ");
                    if (expansion.Length < 2) continue;
                    list.Add(new Candidate(Capitalize(expansion), cleaned, $"Wiktionary ({wikiLang})", wikiLang));
                }
                else
                {
                    // Each definition line is one sense; the sense text is the answer itself.
                    var sense = cleaned.Length > 200 ? cleaned[..200] + "..." : cleaned;
                    list.Add(new Candidate(Capitalize(sense), sense, $"Wiktionary ({wikiLang})", wikiLang));
                }
            }
        }
        return list;
    }

    // ---------------- Wikipedia ----------------

    private static async Task<List<Candidate>> WikipediaAsync(string term, string lang, LookupMode mode, CancellationToken ct)
    {
        var list = new List<Candidate>();
        var url = $"https://{lang}.wikipedia.org/w/api.php?action=query&prop=extracts|pageprops&exintro=1&explaintext=1&redirects=1&format=json&titles={Titles(term)}";
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
                    bool disamb = page.Value.TryGetProperty("pageprops", out var pp) && pp.TryGetProperty("disambiguation", out _) || IsDisambiguationText(text);
                    if (disamb)
                    {
                        foreach (var raw in text.Split('\n').Skip(1))
                        {
                            var line = raw.Trim();
                            if (line.Length < 4 || line.Length > 240 || line.StartsWith("=")) continue;
                            var body = Regex.Replace(line, @"^[^\s—–:-]{1,12}\s*[—–:-]\s*", "");
                            list.Add(new Candidate(Capitalize(FirstClause(body)), line, $"Wikipedia ({lang})", lang));
                        }
                    }
                    else
                    {
                        var first = FirstSentence(text);
                        string expansion = mode == LookupMode.Word
                            ? first                                                                 // the first sentence is the definition
                            : (title.Length > 0 && !title.Equals(term, StringComparison.OrdinalIgnoreCase) ? title : FirstClause(first));
                        if (expansion.Length < 20 && mode == LookupMode.Word) continue;            // a bare title says nothing
                        list.Add(new Candidate(Capitalize(expansion), first, $"Wikipedia ({lang})", lang));
                    }
                }
            }
        }

        // Full-text search adds pages whose title is not exactly the term.
        var surl = $"https://{lang}.wikipedia.org/w/api.php?action=query&list=search&srlimit=6&format=json&srsearch={Uri.EscapeDataString(term)}";
        using (var doc = JsonDocument.Parse(await GetAsync(surl, ct)))
        {
            if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("search", out var hits))
            {
                foreach (var h in hits.EnumerateArray())
                {
                    var title = h.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var snippet = h.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
                    snippet = WebUtility.HtmlDecode(Regex.Replace(snippet, "<.*?>", ""));
                    if (title.Length == 0 || title.Equals(term, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new Candidate(title, snippet, $"Wikipedia search ({lang})", lang));
                }
            }
        }
        return list;
    }

    // ---------------- helpers ----------------

    private static bool IsDisambiguationText(string text) =>
        Regex.IsMatch(text, @"may (also )?refer (also )?to|most commonly refers to|может означать|может относиться|многозначный термин|kann stehen für|steht für|peut désigner|puede referirse|significati", RegexOptions.IgnoreCase);

    private static string FirstSentence(string text)
    {
        // A sentence ends at . ! ? followed by a capital letter or the end; "сокр." / "англ." / "e.g." style tokens do not end one.
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

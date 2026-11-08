using System.Text.RegularExpressions;

namespace Calisto;

public enum LookupMode { Abbreviation, Word }

/// <summary>Picks the meaning that fits the surrounding text: initials that spell the abbreviation and shared vocabulary win.</summary>
public static class Ranker
{
    private static readonly HashSet<string> SkipWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "the", "a", "an", "and", "or", "for", "to", "in", "on", "de", "du", "des", "la", "le", "les", "der", "die", "das", "und", "von",
        "и", "в", "на", "по", "для", "от", "с", "о", "об", "у", "за", "к", "из", "при", "или", "а", "же"
    };

    /// <summary>Score is the total; Fit is only the part earned by the letters matching the abbreviation (or the sense base in word mode).</summary>
    public sealed record Scored(Candidate Candidate, double Score, double Fit);

    public static List<Scored> Rank(string term, string context, string contextLang, IEnumerable<Candidate> candidates,
        LookupMode mode, string? forcedLang = null)
    {
        var letters = Regex.Replace(term, @"[^\p{L}\p{N}]", "").ToLowerInvariant();
        bool typedLower = term.Any(char.IsLetter) && !term.Any(char.IsUpper);   // "bs", "idk": chat shorthand
        var ctxWords = Words(context).Where(w => w.Length >= 4 && !w.Equals(term, StringComparison.OrdinalIgnoreCase))
            .Select(Stem).ToHashSet();

        var scored = new List<Scored>();
        int dictIndex = 0, senseIndex = 0;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c.Expansion)) continue;
            if (Regex.IsMatch(c.Expansion, @"\((disambiguation|Begriffsklärung|значения|homonymie|desambiguación|disambigua)\)", RegexOptions.IgnoreCase)) continue;

            // A fixed language: only meanings written in that language.
            if (forcedLang is not null && !LanguageMatches(c, forcedLang)) continue;

            double s = 0, fit = 0;
            if (mode == LookupMode.Abbreviation)
            {
                var core = Regex.Replace(c.Expansion, @"\s*\(.*?\)", "").Trim();
                if (core.Equals(term, StringComparison.OrdinalIgnoreCase) || core.StartsWith(term + " ", StringComparison.OrdinalIgnoreCase)) continue;
                if (!char.IsLetter(core.FirstOrDefault())) continue;
                var expWords = Words(core.Replace('-', ' ')).Where(w => !SkipWords.Contains(w)).ToList();
                if (expWords.Count == 0 || letters.Length == 0) continue;
                var initials = string.Concat(expWords.Select(w => w[0])).ToLowerInvariant();
                var allLetters = string.Concat(expWords).ToLowerInvariant();
                bool firstMatches = initials[0] == letters[0];
                double initialsRatio = SubsequenceRatio(letters, initials);
                if (initials == letters) s += 10;
                else if (initials.Length > 0 && (initials.StartsWith(letters) || letters.StartsWith(initials)) && Math.Abs(initials.Length - letters.Length) <= 1) s += 7;
                else if (firstMatches && initialsRatio >= 0.75 && letters.Length >= 3) s += 6;                 // "ОСАГО" vs a longer official title
                else if (firstMatches && IsSubsequence(letters, allLetters)) s += expWords.Count == 1 ? 5 : 4;   // "bs" -> "bullshit", "NRW" -> "Nordrhein-Westfalen"
                else if (firstMatches && initialsRatio >= 0.6 && letters.Length >= 3) s += 3;
                else if (c.Source.StartsWith("Wiktionary") || IsOffline(c)) s += 2;   // a definition without matching initials (a plain word sense)
                else continue;                                                         // unrelated article: drop it
                fit = s;

                // Lower-case shorthand usually means the casual sense ("bs" is bullshit, not Bachelor of Science).
                var label = c.Expansion + " " + (c.Gloss.Length > 70 ? c.Gloss[..70] : c.Gloss);
                bool casual = Regex.IsMatch(label, @"\b(slang|vulgar|informal|colloquial|internet|chat|texting|nonsense|разг\.|сленг|жарг\.)", RegexOptions.IgnoreCase);
                if (typedLower && casual) s += 5;
                else if (!typedLower && casual && c.Source.StartsWith("Wiktionary")) s -= 1;
            }
            else
            {
                // Word mode: dictionary senses first (in page order), then an encyclopedia article about the term.
                if (c.Source.StartsWith("Wiktionary")) { fit = 6 - Math.Min(3, 0.4 * senseIndex++); }
                else if (c.Source.StartsWith("Wikipedia search")) fit = 1;
                else if (c.Source.StartsWith("Wikipedia")) fit = 5;
                else if (IsOffline(c) || c.Source == "Built-in dictionary") fit = 5;
                else fit = 3;
                s += fit;
            }

            // Shared vocabulary with the surrounding text.
            var candWords = Words(c.Expansion + " " + c.Gloss).Where(w => w.Length >= 4).Select(Stem).ToHashSet();
            int overlap = candWords.Count(ctxWords.Contains);
            s += Math.Min(6, overlap * 1.5);

            // Language and source: a meaning in the text's own language beats a foreign one with the same initials.
            if (LanguageMatches(c, contextLang)) s += 5; else if (contextLang != "en") s -= 1;
            if (c.Source == "Built-in dictionary") { s += 1.5; if (dictIndex++ == 0) s += 0.5; }   // first dictionary sense is the most common one
            if (c.Source == "Community contribution") s += 1;
            if (c.Source.StartsWith("Wikipedia search")) s -= 2;
            if (mode == LookupMode.Abbreviation && c.Expansion.Length > 70) s -= 1;

            scored.Add(new Scored(c, s, fit));
        }

        // Merge duplicates (same expansion from several sources): keep the best score.
        return scored
            .GroupBy(x => Normalize(x.Candidate.Expansion))
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    private static bool IsOffline(Candidate c) => c.Source.StartsWith("Offline") || c.Source == "Community contribution";

    /// <summary>Dictionary entries are tagged "en" but may be Russian text; judge those by script instead.</summary>
    private static bool LanguageMatches(Candidate c, string lang)
    {
        if (c.Source == "Built-in dictionary")
        {
            var script = LanguageDetector.ScriptOf(c.Expansion);
            return script == "cyr" ? lang is "ru" or "uk" or "kk" or "be" or "sr" or "bg" : lang == "en" || script == "other";
        }
        return c.Lang == lang;
    }

    private static List<string> Words(string s) => Regex.Matches(s, @"\p{L}[\p{L}\p{N}'’-]*").Select(m => m.Value).ToList();
    private static string Stem(string w) { w = w.ToLowerInvariant(); return w.Length > 6 ? w[..6] : w; }
    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    private static bool IsSubsequence(string small, string big) => SubsequenceRatio(small, big) >= 1.0;

    /// <summary>Share of the abbreviation's letters that appear, in order, in the candidate string.</summary>
    private static double SubsequenceRatio(string small, string big)
    {
        if (small.Length == 0) return 0;
        int i = 0;
        foreach (var ch in big) { if (i < small.Length && small[i] == ch) i++; }
        return (double)i / small.Length;
    }
}

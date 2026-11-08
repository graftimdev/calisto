using System.Text.RegularExpressions;

namespace Calisto;

/// <summary>Guesses the language of a piece of text from its script and a few very common words. Returns a Wikipedia language code.</summary>
public static class LanguageDetector
{
    /// <summary>Languages offered in the picker: code and display name.</summary>
    public static readonly (string Code, string Name)[] Choices =
    {
        ("auto", "Auto (detect from text)"), ("en", "English"), ("ru", "Русский"), ("uk", "Українська"), ("kk", "Қазақша"),
        ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"), ("it", "Italiano"), ("pt", "Português"), ("nl", "Nederlands"),
        ("pl", "Polski"), ("tr", "Türkçe"), ("el", "Ελληνικά"), ("ar", "العربية"), ("he", "עברית"), ("ja", "日本語"), ("zh", "中文"), ("ko", "한국어")
    };

    /// <summary>How each Wiktionary names a language in its level-2 section headings (wiki code -> language code -> heading).</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> SectionNames = new()
    {
        ["en"] = new() { ["en"] = "English", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["kk"] = "Kazakh", ["de"] = "German", ["fr"] = "French", ["es"] = "Spanish", ["it"] = "Italian", ["pt"] = "Portuguese", ["nl"] = "Dutch", ["pl"] = "Polish", ["tr"] = "Turkish", ["el"] = "Greek", ["ar"] = "Arabic", ["he"] = "Hebrew", ["ja"] = "Japanese", ["zh"] = "Chinese", ["ko"] = "Korean" },
        ["ru"] = new() { ["ru"] = "Русский", ["en"] = "Английский", ["uk"] = "Украинский", ["kk"] = "Казахский", ["de"] = "Немецкий", ["fr"] = "Французский", ["es"] = "Испанский", ["it"] = "Итальянский" },
        ["de"] = new() { ["de"] = "Deutsch", ["en"] = "Englisch", ["ru"] = "Russisch", ["fr"] = "Französisch", ["es"] = "Spanisch", ["it"] = "Italienisch" },
        ["fr"] = new() { ["fr"] = "Français", ["en"] = "Anglais", ["ru"] = "Russe", ["de"] = "Allemand", ["es"] = "Espagnol", ["it"] = "Italien" },
        ["es"] = new() { ["es"] = "Español", ["en"] = "Inglés", ["ru"] = "Ruso", ["de"] = "Alemán", ["fr"] = "Francés", ["it"] = "Italiano" },
    };

    public static string? SectionName(string wikiLang, string lang) =>
        SectionNames.TryGetValue(wikiLang, out var m) && m.TryGetValue(lang, out var n) ? n : null;

    public static string DisplayName(string code) => Choices.FirstOrDefault(c => c.Code == code).Name ?? code;

    private static readonly Dictionary<string, string[]> LatinMarkers = new()
    {
        ["en"] = ["the", "and", "is", "of", "to", "for", "with", "that", "this", "you", "are", "not", "it's", "some"],
        ["de"] = ["der", "die", "das", "und", "ist", "nicht", "mit", "für", "ein", "eine", "auf", "sich", "wird"],
        ["fr"] = ["le", "la", "les", "et", "est", "des", "une", "pour", "dans", "que", "pas", "sur", "vous"],
        ["es"] = ["el", "la", "los", "las", "y", "es", "de", "que", "para", "con", "una", "por", "del"],
        ["it"] = ["il", "la", "di", "che", "e", "è", "per", "con", "una", "del", "non", "sono", "gli"],
        ["pt"] = ["o", "a", "os", "as", "e", "é", "de", "que", "para", "com", "uma", "não", "do"],
        ["nl"] = ["de", "het", "een", "en", "van", "is", "niet", "met", "voor", "dat", "zijn", "ook"],
        ["pl"] = ["i", "w", "nie", "jest", "się", "na", "to", "że", "z", "do", "dla", "jak"],
        ["tr"] = ["ve", "bir", "bu", "için", "ile", "de", "da", "çok", "olarak", "var", "ne"],
        ["kk"] = ["және", "бұл", "үшін", "мен", "болып", "туралы"],
    };

    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";
        int cyr = 0, latin = 0, greek = 0, arabic = 0, hebrew = 0, cjk = 0, kana = 0, hangul = 0, thai = 0;
        foreach (var c in text)
        {
            if (c is >= 'Ѐ' and <= 'ӿ') cyr++;
            else if (char.IsLetter(c) && c < 'ɐ') latin++;
            else if (c is >= 'Ͱ' and <= 'Ͽ') greek++;
            else if (c is >= '؀' and <= 'ۿ') arabic++;
            else if (c is >= '֐' and <= '׿') hebrew++;
            else if (c is >= '぀' and <= 'ヿ') kana++;
            else if (c is >= '가' and <= '힯') hangul++;
            else if (c is >= '一' and <= '鿿') cjk++;
            else if (c is >= '฀' and <= '๿') thai++;
        }
        int max = new[] { cyr, latin, greek, arabic, hebrew, cjk + kana, hangul, thai }.Max();
        if (max == 0) return "en";
        if (max == greek) return "el";
        if (max == arabic) return "ar";
        if (max == hebrew) return "he";
        if (max == hangul) return "ko";
        if (max == thai) return "th";
        if (max == cjk + kana) return kana > 0 ? "ja" : "zh";
        if (max == cyr)
        {
            if (Regex.IsMatch(text, "[әіңғүұқөһӘІҢҒҮҰҚӨҺ]")) return "kk";
            if (Regex.IsMatch(text, "[їєґЇЄҐ]")) return "uk";
            if (Regex.IsMatch(text, "[ўЎ]")) return "be";
            if (Regex.IsMatch(text, "[ћђџЋЂЏ]")) return "sr";
            return "ru";
        }

        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).ToHashSet();
        string best = "en"; int bestHits = 0;
        foreach (var (lang, markers) in LatinMarkers)
        {
            int hits = markers.Count(words.Contains);
            if (hits > bestHits) { bestHits = hits; best = lang; }
        }
        return best;
    }

    /// <summary>Script-based guess for a single short token (used to keep dictionary entries in the right language).</summary>
    public static string ScriptOf(string token)
    {
        foreach (var c in token)
        {
            if (c is >= 'Ѐ' and <= 'ӿ') return "cyr";
            if (char.IsLetter(c) && c < 'ɐ') return "lat";
        }
        return "other";
    }
}

using System.Text.RegularExpressions;

namespace Calisto;

/// <summary>Heuristics that decide whether a token on screen looks like an abbreviation.</summary>
public static class AbbreviationDetector
{
    public static readonly char[] Punctuation = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '“', '”', '’', '‘' };

    // Candidate tokens: words of 2-8 letters/digits (ALL-CAPS such as "API", mixed case such as "SaaS" or "YoY",
    // optionally with & or / as in "P&L", "CI/CD") or dotted forms like "e.g." / "U.S.".
    private static readonly Regex TokenRegex = new(
        @"(?<![\p{L}\p{N}])(?:\p{L}[\p{L}\p{N}&/;]{0,6}[\p{L}\p{N}]|(?:\p{L}\.){2,4})(?![\p{L}\p{N}])",
        RegexOptions.Compiled);

    // Ordinary words that are often written in capitals (menus, headings) and are not abbreviations.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "A","I","AN","THE","AND","OR","OF","TO","IN","ON","AT","BY","FOR","IS","IT","AS","BE","DO","GO","NO","SO","UP","US","WE",
        "ALL","ANY","ARE","BUT","CAN","DID","FROM","GET","HAS","HAD","HIM","HER","HIS","HOW","ITS","LET","MAY","NEW","NOT","NOW",
        "OFF","OLD","ONE","OUR","OUT","OWN","SAY","SEE","SHE","TOO","TWO","USE","WAS","WAY","WHO","WHY","YES","YET","YOU",
        "WITH","THIS","THAT","THEN","THAN","WHEN","WHAT","WILL","YOUR","HERE","THERE","HAVE","MORE","SOME","INTO","ONLY","OVER",
        "ALSO","BACK","BEEN","JUST","LIKE","MAKE","MOST","MUCH","NEXT","OPEN","SAVE","EDIT","FILE","VIEW","HELP","HOME","MENU",
        "NEWS","SIGN","LOGIN","SEARCH","CLOSE","ABOUT","ERROR","WARNING","TOTAL","FREE","SALE","TODAY","READ","SHOP","PLAY","STOP",
        "START","ENTER","EXIT","BUY","ADD","RUN","SET","TOP","END","MAX","MIN","OK","HI","HEY","WOW","LOL",
        "II","III","IV","VI","VII","VIII","IX","XI","XII","XIII","XIV","XV","XX","XXI",
        // Russian words often written in capitals in menus and headings
        "ДА","НЕТ","ИЛИ","НЕ","ВЫ","МЫ","ОН","ОНА","ВСЕ","ВСЁ","ЕЩЁ","ЕЩЕ","ТАК","КАК","ЧТО","ЭТО","ТУТ","ТАМ","ГДЕ","КТО","ВОТ","ПРИ",
        "ДЛЯ","БЕЗ","ПОД","НАД","ПРО","ВХОД","ВЫХОД","МЕНЮ","ПОИСК","ФАЙЛ","ВИД","ПРАВКА","СПРАВКА","ГЛАВНАЯ","НОВОЕ","ВСЕ","ДАЛЕЕ","НАЗАД",
        "СОХРАНИТЬ","ОТМЕНА","ЗАКРЫТЬ","ОТКРЫТЬ","КУПИТЬ","АКЦИЯ","СКИДКА","НОВОСТИ","ВОЙТИ","ОК"
    };

    public static IEnumerable<string> FindDistinct(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TokenRegex.Matches(text))
        {
            var token = m.Value;
            if (!LooksLikeAbbreviation(token, text, m.Index)) continue;
            if (seen.Add(token)) yield return token;
        }
    }

    public static bool LooksLikeAbbreviation(string token, string text, int index)
    {
        if (token.Length < 2) return false;
        if (AbbreviationLookup.IsKnown(token)) return true;     // dictionary entries and lower-case slang like "bs", "idk"
        if (StopWords.Contains(token)) return false;

        int upper = token.Count(char.IsUpper), lower = token.Count(char.IsLower), digits = token.Count(char.IsDigit);
        if (upper == 0) return token.Contains('.') && token.Length >= 4; // dotted forms like "e.g."
        if (upper == 1 && lower == 0 && digits == 0) return false;        // single capital letter
        if (upper + lower + digits < 2) return false;

        // Mixed case: accept compact forms like "SaaS", "YoY", "PhD", "K8s"; reject ordinary CamelCase words ("GitHub").
        if (lower > 0 && (upper < 2 || lower > 2)) return false;

        // Skip tokens inside an ALL-CAPS line or sentence (headings, shouting), where capitals carry no signal.
        if (IsInsideCapsRun(text, index, token.Length)) return false;

        return true;
    }

    private static bool IsInsideCapsRun(string text, int index, int length)
    {
        // Look at the enclosing line, bounded by line breaks (and sentence ends, to keep headings separate).
        int start = index, end = index + length;
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r' && index - start < 200) start--;
        while (end < text.Length && text[end] != '\n' && text[end] != '\r' && end - index < 200) end++;

        int letters = 0, uppers = 0;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) uppers++; }
        }
        // A caps run needs clearly more capital letters than the token itself contributes.
        return letters >= length + 6 && uppers >= letters * 0.85;
    }

    /// <summary>Index of the n-th whole-word occurrence of a token, or -1.</summary>
    public static int IndexOfWord(string text, string token, int startAt)
    {
        var m = Regex.Match(text.Substring(Math.Min(startAt, text.Length)),
            @"(?<![\p{L}\p{N}])" + Regex.Escape(token) + @"(?![\p{L}\p{N}])");
        return m.Success ? startAt + m.Index : -1;
    }

    public static string ContextAround(string text, int index, int length)
    {
        if (string.IsNullOrEmpty(text)) return "";
        index = Math.Clamp(index, 0, text.Length);
        int start = Math.Max(0, index - 160);
        int end = Math.Min(text.Length, index + length + 160);
        var slice = text.Substring(start, end - start).Replace('\r', ' ').Replace('\n', ' ');
        return Regex.Replace(slice, @"\s+", " ").Trim();
    }
}

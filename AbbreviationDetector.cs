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

    /// <summary>Ordinary words that interfaces like to shout in capitals (PLAY, READY, НАЗАД): never abbreviations unless in the dictionary.</summary>
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase);

    static AbbreviationDetector()
    {
        const string en = @"a i an the and or of to in on at by for is it as be do go no so up us we all any are but can did from get has had him her
his how its let may new not now off old one our out own say see she too two use was way who why yes yet you with this that then than when
what will your here there have more some into only over also back been just like make most much next open save edit file view help home menu
news sign login search close about error warning total free sale today read shop play stop start enter exit buy add run set top end max min ok
hi hey wow ready go pause resume retry quit cancel apply done skip back next prev previous first last page tab link copy paste cut undo redo
send reply post share like love follow join leave mute unmute volume sound music video photo image gallery camera map maps call chat mail
inbox sent draft spam trash delete remove clear reset update upgrade install download upload import export print scan zoom fit full screen
mode dark light theme color size font bold italic text title name date time year month week day hour minute second now soon later never
always often user users account profile settings options tools system network wifi bluetooth battery power sleep lock unlock sign out log
out in on off yes no true false none auto manual default custom basic pro plus premium team player players game games level score win lose
loss best worst high low fast slow easy hard normal expert beginner tutorial guide tips info more less show hide view list grid table chart
row column cell data file files folder folders path drive disk cloud sync backup restore version history recent favorites favourite star
rate review rating comment comments question answer support contact feedback report bug issue fix patch build release beta alpha demo trial
sale deal offer price cost pay card cash order cart checkout ship free gift code promo discount coupon tax total subtotal item items qty
north south east west left right center centre up down over under inside outside near far big small large tiny huge long short wide
narrow red green blue black white gray grey yellow orange pink purple brown gold silver hot cold warm cool dry wet rain snow sun moon
star sky sea land fire water air earth wind king queen hero boss enemy ally friend team squad guild clan party solo coop versus arena
quest mission story campaign survival sandbox classic ranked casual loading saving wait please thanks sorry welcome goodbye hello
continue confirm accept decline agree disagree allow deny block unblock enable disable turn press hold tap click drag drop swipe scroll
select unselect check uncheck toggle switch move resize rotate flip crop filter sort search find replace browse explore discover watch
listen speak talk write draw paint type learn teach study work job jobs career hire apply careers company about press blog events
store shop stores shopping deals brands categories category new arrivals trending popular featured recommended best sellers
enjoy great good bad nice cool fine okay sure maybe perhaps really very quite rather almost nearly even still already again once twice
ever soon late early today tonight tomorrow yesterday week weekend month year";
        const string ru = @"да нет или не вы мы он она все всё ещё еще так как что это тут там где кто вот при для без под над про вход выход меню поиск файл вид
правка справка главная новое далее назад сохранить отмена закрыть открыть купить акция скидка новости войти ок играть игра старт стоп пауза
продолжить выйти выход настройки параметры помощь о программе профиль аккаунт пользователь пароль логин регистрация подписка подписаться
отписаться лайк нравится поделиться отправить ответить написать удалить очистить обновить загрузить скачать установить удалить копировать
вставить вырезать отменить повторить печать печатать сканировать звук музыка видео фото камера карта карты звонок чат почта входящие
исходящие черновики спам корзина готово пропустить применить принять отклонить согласен разрешить запретить включить выключить вкл выкл
вверх вниз влево вправо лево право верх низ центр начало конец первый последний следующий предыдущий страница вкладка ссылка
имя дата время год месяц неделя день час минута секунда сейчас скоро позже никогда всегда часто быстро медленно легко сложно
цена стоимость оплата оплатить заказ корзина доставка бесплатно подарок код промокод скидка налог итого сумма товар товары
красный зелёный синий чёрный белый серый жёлтый оранжевый розовый фиолетовый коричневый день ночь утро вечер
привет пока спасибо пожалуйста извините добро пожаловать уровень очки счёт победа поражение игрок игроки команда миссия
уровни рекорд рекорды лучший новый старый большой маленький длинный короткий широкий узкий горячий холодный";
        foreach (var w in Regex.Split(en + " " + ru, @"\s+")) if (w.Length > 0) CommonWords.Add(w);
    }

    /// <summary>Distinct abbreviation-looking tokens in a text. Strict mode (button and menu labels) requires stronger evidence.</summary>
    public static IEnumerable<string> FindDistinct(string text, bool strict = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TokenRegex.Matches(text))
        {
            var token = m.Value;
            if (!LooksLikeAbbreviation(token, text, m.Index, strict)) continue;
            if (seen.Add(token)) yield return token;
        }
    }

    public static bool LooksLikeAbbreviation(string token, string text, int index, bool strict = false)
    {
        if (token.Length < 2) return false;
        if (AbbreviationLookup.IsKnown(token)) return true;     // dictionary entries and lower-case slang like "bs", "idk"
        if (CommonWords.Contains(token)) return false;           // PLAY, READY, НАЗАД: a word that happens to be in capitals

        int upper = token.Count(char.IsUpper), lower = token.Count(char.IsLower), digits = token.Count(char.IsDigit);
        if (upper == 0) return token.Contains('.') && token.Length >= 4; // dotted forms like "e.g."
        if (upper == 1 && lower == 0 && digits == 0) return false;        // single capital letter
        if (upper + lower + digits < 2) return false;

        // Mixed case: accept compact forms like "SaaS", "YoY", "PhD", "K8s"; reject ordinary CamelCase words ("GitHub").
        if (lower > 0 && (upper < 2 || lower > 2)) return false;

        // An ALL-CAPS token that reads like a pronounceable word (SCRUM, LOADING) is more likely a shouted word than an acronym.
        if (lower == 0 && digits == 0 && token.Length >= (strict ? 4 : 5) && LooksPronounceable(token)) return false;

        // Skip tokens inside an ALL-CAPS line or sentence (headings, shouting), where capitals carry no signal.
        if (IsInsideCapsRun(text, index, token.Length)) return false;

        // Button or menu labels: a lone capitalised word is usually just a label.
        if (strict && token.Length >= 4 && lower == 0 && HasVowelPattern(token)) return false;

        return true;
    }

    private const string Vowels = "aeiouyаеёиоуыэюяäöüéèêáíóúıі";

    /// <summary>True when vowels and consonants alternate the way real words do (no 3 consonants in a row, vowel ratio 25-70%).</summary>
    private static bool LooksPronounceable(string token)
    {
        var t = token.ToLowerInvariant();
        int vowels = t.Count(c => Vowels.Contains(c));
        if (vowels == 0 || vowels == t.Length) return false;
        double ratio = (double)vowels / t.Length;
        if (ratio < 0.25 || ratio > 0.7) return false;
        int run = 0;
        foreach (var c in t) { if (Vowels.Contains(c)) run = 0; else if (++run >= 3) return false; }
        return true;
    }

    private static bool HasVowelPattern(string token) => token.ToLowerInvariant().Count(c => Vowels.Contains(c)) >= 1 && LooksPronounceable(token);

    private static bool IsInsideCapsRun(string text, int index, int length)
    {
        // Look at the enclosing line, bounded by line breaks.
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

    /// <summary>Index of the next whole-word occurrence of a token, or -1.</summary>
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

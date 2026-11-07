using System.Collections.Concurrent;
using Anthropic;
using Anthropic.Models.Messages;
using AnthropicTextBlock = Anthropic.Models.Messages.TextBlock;

namespace Calisto;

public sealed record LookupResult(string Text, string Source);

/// <summary>
/// Explains an abbreviation using the text around it. Works without any key: candidates come from the built-in
/// dictionary, Wiktionary and Wikipedia (in the language of the context), and are ranked by fit to the context.
/// With a Claude API key the same candidates are handed to Claude for a more precise answer.
/// </summary>
public static class AbbreviationLookup
{
    private static readonly ConcurrentDictionary<string, LookupResult> Cache = new();

    private const string SystemPrompt =
        "You explain abbreviations, acronyms and slang shorthand that a reader found on their screen, in any language. " +
        "You receive the abbreviation, the surrounding text, and candidate meanings found in dictionaries. " +
        "Reply in the language of the surrounding text, plain text, no markdown. " +
        "Line 1: the single most likely expansion for THIS context. " +
        "Line 2: one short sentence explaining what it means here. " +
        "If another expansion is also plausible here, add a final line starting with 'Also: ' listing at most two alternatives. " +
        "If the token is not really an abbreviation (a product name or a plain word), say so in one sentence.";

    public static bool IsKnown(string token) =>
        Dictionary.ContainsKey(token) || Slang.Contains(token);

    public static async Task<LookupResult> LookupAsync(string abbr, string context)
    {
        string key = abbr + "\u0001" + context;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var lang = LanguageDetector.Detect(context);
        var candidates = new List<Candidate>();
        foreach (var m in DictionaryMeanings(abbr))
            candidates.Add(new Candidate(m, m, "Built-in dictionary", "en"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            candidates.AddRange(await OnlineSources.FetchAsync(abbr, lang, cts.Token));
            // Give the strongest candidates a description so the context can be compared against them.
        // (Dictionary glosses equal their expansion, so they are the ones that get enriched.)
            var prelim = Ranker.Rank(abbr, context, lang, candidates).Take(6).Select(r => r.Candidate).ToList();
            var enriched = await OnlineSources.EnrichAsync(prelim, lang, cts.Token);
            candidates = enriched.Concat(candidates.Where(c => !prelim.Contains(c))).ToList();
        }
        catch { /* offline: dictionary only */ }

        var ranked = Ranker.Rank(abbr, context, lang, candidates);

        LookupResult result;
        var apiKey = App.Settings.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try { result = await AskClaudeAsync(apiKey, abbr, context, ranked); }
            catch (Exception ex)
            {
                var local = Compose(ranked);
                result = new LookupResult(local?.Text ?? "No definition found.",
                    (local?.Source ?? "") + " (Claude lookup failed: " + Shorten(ex.Message) + ")");
            }
        }
        else
        {
            result = Compose(ranked) ?? new LookupResult(
                OnlineSources.RateLimited ? "The online dictionaries are busy right now. Try again in a minute."
                : !IsOnlineLikely() ? "No definition found. You seem to be offline; only the built-in dictionary is available."
                : "No definition found for this abbreviation.",
                "Dictionary, Wiktionary and Wikipedia");
        }

        Cache[key] = result;
        return result;
    }

    private static bool IsOnlineLikely() => System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();

    /// <summary>Builds the answer text from the ranked candidates: best expansion, its gloss, then alternatives.</summary>
    private static LookupResult? Compose(List<Ranker.Scored> ranked)
    {
        if (ranked.Count == 0) return null;
        var best = ranked[0];
        var text = best.Candidate.Expansion;
        var gloss = System.Text.RegularExpressions.Regex.Split(best.Candidate.Gloss, @"\s*[◆◇□]").FirstOrDefault()?.Trim() ?? "";
        if (!gloss.Equals(best.Candidate.Expansion, StringComparison.OrdinalIgnoreCase) && gloss.Length > 0)
            text += "\n" + (gloss.Length > 220 ? gloss[..220] + "..." : gloss);

        var alts = ranked.Skip(1)
            .Where(r => r.Fit >= 5 && r.Score >= best.Score - 8)
            .Select(r => r.Candidate.Expansion)
            .Take(2).ToList();
        if (alts.Count > 0) text += "\nAlso: " + string.Join("; ", alts);

        return new LookupResult(text, best.Candidate.Source);
    }

    private static async Task<LookupResult> AskClaudeAsync(string apiKey, string abbr, string context, List<Ranker.Scored> ranked)
    {
        var hints = string.Join("\n", ranked.Take(8).Select(r => $"- {r.Candidate.Expansion} ({r.Candidate.Source})"));
        var client = new AnthropicClient { ApiKey = apiKey };
        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = "claude-opus-5-5",
            MaxTokens = 400,
            System = SystemPrompt,
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages =
            [
                new UserMessageParam
                {
                    Content = $"Abbreviation: {abbr}\nSurrounding text: \"{context}\"\nCandidate meanings found:\n{(hints.Length > 0 ? hints : "(none)")}"
                }
            ]
        });

        if (response.StopReason == "refusal")
            return Compose(ranked) ?? new LookupResult("The lookup service declined this request.", "Claude");

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<AnthropicTextBlock>().Select(t => t.Text)).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return Compose(ranked) ?? new LookupResult("No definition found.", "Claude");
        return new LookupResult(text, "Claude, using the surrounding text");
    }

    private static IEnumerable<string> DictionaryMeanings(string abbr)
    {
        if (Dictionary.TryGetValue(abbr, out var m)) return m;
        if (Dictionary.TryGetValue(abbr.ToUpperInvariant(), out m) && Slang.Contains(abbr)) return m;
        return [];
    }

    private static string Shorten(string s) => s.Length > 90 ? s[..90] + "..." : s;

    /// <summary>Shorthand that is usually typed in lower case; matched case-insensitively.</summary>
    private static readonly HashSet<string> Slang = new(StringComparer.OrdinalIgnoreCase)
    {
        "bs", "lol", "idk", "imo", "imho", "tbh", "smh", "brb", "omg", "wtf", "ngl", "irl", "afaik", "nvm", "ikr", "btw", "ofc",
        "pls", "plz", "thx", "gg", "afk", "fyi", "asap", "tldr", "tl;dr", "fml", "ffs", "lmao", "lmk", "rofl", "jk", "ily", "tba", "tbd",
        "wip", "wfh", "ttyl", "hbu", "wyd", "idc", "iirc", "dm", "ppl", "msg", "txt", "rn", "atm", "bf", "gf", "bff", "fomo", "yolo",
        "icymi", "eta", "eod", "ooo", "np", "ty", "tyvm", "gtg", "g2g", "omw", "wbu", "hmu", "fr", "iykyk", "nsfw", "sus", "mf",
        "спс", "пжл", "пж", "лол", "хз", "имхо", "кст", "мб", "чел", "инфа", "нзч", "збс", "хбз"
    };

    private static readonly Dictionary<string, string[]> Dictionary = new(StringComparer.Ordinal)
    {
        // Chat and slang
        ["BS"] = ["Bullshit (slang: nonsense)", "Bachelor of Science", "Balance sheet"], ["LOL"] = ["Laughing out loud"], ["IDK"] = ["I don't know"],
        ["IMO"] = ["In my opinion"], ["IMHO"] = ["In my humble opinion"], ["TBH"] = ["To be honest"], ["SMH"] = ["Shaking my head"],
        ["BRB"] = ["Be right back"], ["OMG"] = ["Oh my god"], ["WTF"] = ["What the f***"], ["NGL"] = ["Not gonna lie"], ["IRL"] = ["In real life"],
        ["AFAIK"] = ["As far as I know"], ["NVM"] = ["Never mind"], ["IKR"] = ["I know, right"], ["BTW"] = ["By the way"], ["OFC"] = ["Of course"],
        ["PLS"] = ["Please"], ["PLZ"] = ["Please"], ["THX"] = ["Thanks"], ["GG"] = ["Good game"], ["AFK"] = ["Away from keyboard"],
        ["FYI"] = ["For your information"], ["ASAP"] = ["As soon as possible"], ["TLDR"] = ["Too long; didn't read (summary)"], ["TL;DR"] = ["Too long; didn't read (summary)"],
        ["FML"] = ["F*** my life"], ["FFS"] = ["For f***'s sake"], ["LMAO"] = ["Laughing my a** off"], ["LMK"] = ["Let me know"], ["ROFL"] = ["Rolling on the floor laughing"],
        ["JK"] = ["Just kidding"], ["ILY"] = ["I love you"], ["TTYL"] = ["Talk to you later"], ["HBU"] = ["How about you"], ["WYD"] = ["What are you doing"],
        ["IDC"] = ["I don't care"], ["IIRC"] = ["If I recall correctly"], ["DM"] = ["Direct message"], ["PPL"] = ["People"], ["MSG"] = ["Message", "Monosodium glutamate"],
        ["TXT"] = ["Text message"], ["RN"] = ["Right now", "Registered nurse"], ["ATM"] = ["At the moment", "Automated teller machine"], ["BF"] = ["Boyfriend"],
        ["GF"] = ["Girlfriend"], ["BFF"] = ["Best friends forever"], ["FOMO"] = ["Fear of missing out"], ["YOLO"] = ["You only live once"],
        ["ICYMI"] = ["In case you missed it"], ["NP"] = ["No problem"], ["TY"] = ["Thank you"], ["TYVM"] = ["Thank you very much"], ["GTG"] = ["Got to go"],
        ["G2G"] = ["Got to go"], ["OMW"] = ["On my way"], ["WBU"] = ["What about you"], ["HMU"] = ["Hit me up (contact me)"], ["FR"] = ["For real"],
        ["IYKYK"] = ["If you know, you know"], ["NSFW"] = ["Not safe for work"], ["SUS"] = ["Suspicious"], ["MF"] = ["Motherf***er"],
        ["СПС"] = ["Спасибо"], ["ПЖЛ"] = ["Пожалуйста"], ["ПЖ"] = ["Пожалуйста"], ["ЛОЛ"] = ["Громко смеюсь (laughing out loud)"], ["ХЗ"] = ["Не знаю (грубо)"],
        ["ИМХО"] = ["По моему скромному мнению"], ["КСТ"] = ["Кстати"], ["МБ"] = ["Может быть"], ["ЧЕЛ"] = ["Человек"], ["ИНФА"] = ["Информация"],
        ["НЗЧ"] = ["Не за что"], ["ЗБС"] = ["Отлично (грубо)"], ["ХБЗ"] = ["Не знаю (грубо)"],
        // Russian / CIS official
        ["ИИН"] = ["Индивидуальный идентификационный номер"], ["БИН"] = ["Бизнес-идентификационный номер"], ["ИНН"] = ["Идентификационный номер налогоплательщика"],
        ["СНИЛС"] = ["Страховой номер индивидуального лицевого счёта"], ["ОГРН"] = ["Основной государственный регистрационный номер"],
        ["КПП"] = ["Код причины постановки на учёт", "Контрольно-пропускной пункт", "Коробка переключения передач"], ["РФ"] = ["Российская Федерация"],
        ["РК"] = ["Республика Казахстан"], ["РБ"] = ["Республика Беларусь"], ["ООО"] = ["Общество с ограниченной ответственностью"], ["ИП"] = ["Индивидуальный предприниматель"],
        ["ЗАО"] = ["Закрытое акционерное общество"], ["ОАО"] = ["Открытое акционерное общество"], ["ПАО"] = ["Публичное акционерное общество"], ["АО"] = ["Акционерное общество"],
        ["ТОО"] = ["Товарищество с ограниченной ответственностью"], ["НДС"] = ["Налог на добавленную стоимость"], ["НДФЛ"] = ["Налог на доходы физических лиц"],
        ["ФИО"] = ["Фамилия, имя, отчество"], ["МВД"] = ["Министерство внутренних дел"], ["ФСБ"] = ["Федеральная служба безопасности"],
        ["ГИБДД"] = ["Государственная инспекция безопасности дорожного движения"], ["ЖКХ"] = ["Жилищно-коммунальное хозяйство"], ["СМИ"] = ["Средства массовой информации"],
        ["ВУЗ"] = ["Высшее учебное заведение"], ["ЕГЭ"] = ["Единый государственный экзамен"], ["ЕНТ"] = ["Единое национальное тестирование"],
        ["ОРВИ"] = ["Острая респираторная вирусная инфекция"], ["МРОТ"] = ["Минимальный размер оплаты труда"], ["ПФР"] = ["Пенсионный фонд России"],
        ["ЦБ"] = ["Центральный банк"], ["МФЦ"] = ["Многофункциональный центр"], ["ЭЦП"] = ["Электронная цифровая подпись"], ["ЦОН"] = ["Центр обслуживания населения"],
        ["ГОСТ"] = ["Государственный стандарт"], ["СССР"] = ["Союз Советских Социалистических Республик"], ["США"] = ["Соединённые Штаты Америки"],
        ["ЕС"] = ["Европейский союз"], ["ООН"] = ["Организация Объединённых Наций"], ["НИИ"] = ["Научно-исследовательский институт"],
        ["ПО"] = ["Программное обеспечение"], ["ИИ"] = ["Искусственный интеллект"], ["БД"] = ["База данных"], ["ОС"] = ["Операционная система"],
        ["ТЗ"] = ["Техническое задание"], ["КПД"] = ["Коэффициент полезного действия"], ["ДТП"] = ["Дорожно-транспортное происшествие"], ["ОМС"] = ["Обязательное медицинское страхование"],
        ["УЗИ"] = ["Ультразвуковое исследование"], ["МРТ"] = ["Магнитно-резонансная томография"], ["ЭКГ"] = ["Электрокардиограмма"], ["ЗОЖ"] = ["Здоровый образ жизни"],
        ["Т.Е."] = ["То есть"], ["Т.Д."] = ["Так далее"], ["Т.П."] = ["Тому подобное"], ["Г."] = ["Год", "Город"], ["УЛ."] = ["Улица"], ["Д."] = ["Дом"], ["КВ."] = ["Квартира"],
        // General / everyday
        ["FAQ"] = ["Frequently asked questions"], ["ETA"] = ["Estimated time of arrival"], ["DIY"] = ["Do it yourself"], ["RSVP"] = ["Please reply (French: repondez s'il vous plait)"],
        ["TBD"] = ["To be determined"], ["TBA"] = ["To be announced"], ["TBC"] = ["To be confirmed"], ["N/A"] = ["Not applicable", "Not available"],
        ["AKA"] = ["Also known as"], ["e.g."] = ["For example (Latin: exempli gratia)"], ["i.e."] = ["That is (Latin: id est)"],
        ["etc."] = ["And so on (Latin: et cetera)"], ["vs."] = ["Versus"], ["a.m."] = ["Before noon (ante meridiem)"], ["p.m."] = ["After noon (post meridiem)"],
        ["AM"] = ["Before noon (ante meridiem)", "Amplitude modulation"], ["PM"] = ["After noon (post meridiem)", "Project manager", "Prime minister", "Private message"],
        ["DOB"] = ["Date of birth"], ["ID"] = ["Identification / identifier"], ["PIN"] = ["Personal identification number"],
        ["ZIP"] = ["Zone Improvement Plan postal code (US)", "ZIP compressed archive"], ["PO"] = ["Purchase order", "Post office"],
        ["OOO"] = ["Out of office"], ["EOD"] = ["End of day"], ["EOW"] = ["End of week"], ["WFH"] = ["Working from home"], ["PTO"] = ["Paid time off"],
        ["OOTB"] = ["Out of the box"], ["POV"] = ["Point of view"], ["PS"] = ["Postscript"], ["RIP"] = ["Rest in peace"], ["VIP"] = ["Very important person"],
        ["UFO"] = ["Unidentified flying object"], ["GPS"] = ["Global Positioning System"], ["PDF"] = ["Portable Document Format"], ["TV"] = ["Television"],
        ["DVD"] = ["Digital versatile disc"], ["USB"] = ["Universal Serial Bus"], ["Wi-Fi"] = ["Wireless networking (IEEE 802.11)"], ["LED"] = ["Light-emitting diode"],
        ["LCD"] = ["Liquid-crystal display"], ["AC"] = ["Air conditioning", "Alternating current"], ["DC"] = ["Direct current", "Washington, D.C."], ["HR"] = ["Human resources", "Hour"],
        ["CEO"] = ["Chief executive officer"], ["CFO"] = ["Chief financial officer"], ["CTO"] = ["Chief technology officer"], ["COO"] = ["Chief operating officer"],
        ["CMO"] = ["Chief marketing officer"], ["CIO"] = ["Chief information officer"], ["VP"] = ["Vice president"], ["SVP"] = ["Senior vice president"],
        ["MBA"] = ["Master of Business Administration"], ["PhD"] = ["Doctor of Philosophy"], ["MD"] = ["Doctor of Medicine", "Managing director"],
        ["BA"] = ["Bachelor of Arts", "Business analyst"], ["MS"] = ["Master of Science", "Microsoft", "Multiple sclerosis"],
        // Business / finance
        ["KPI"] = ["Key performance indicator"], ["ROI"] = ["Return on investment"], ["B2B"] = ["Business to business"], ["B2C"] = ["Business to consumer"],
        ["SaaS"] = ["Software as a service"], ["SLA"] = ["Service level agreement"], ["NDA"] = ["Non-disclosure agreement"], ["RFP"] = ["Request for proposal"],
        ["P&L"] = ["Profit and loss statement"], ["EBITDA"] = ["Earnings before interest, taxes, depreciation and amortization"],
        ["YoY"] = ["Year over year"], ["QoQ"] = ["Quarter over quarter"], ["MoM"] = ["Month over month"], ["YTD"] = ["Year to date"],
        ["Q1"] = ["First quarter"], ["Q2"] = ["Second quarter"], ["Q3"] = ["Third quarter"], ["Q4"] = ["Fourth quarter"],
        ["FY"] = ["Fiscal year"], ["GDP"] = ["Gross domestic product"], ["IPO"] = ["Initial public offering"], ["M&A"] = ["Mergers and acquisitions"],
        ["VAT"] = ["Value-added tax"], ["ARR"] = ["Annual recurring revenue"], ["MRR"] = ["Monthly recurring revenue"], ["CAC"] = ["Customer acquisition cost"],
        ["LTV"] = ["Lifetime value (of a customer)", "Loan-to-value"], ["CRM"] = ["Customer relationship management"], ["ERP"] = ["Enterprise resource planning"],
        ["OKR"] = ["Objectives and key results"], ["SOP"] = ["Standard operating procedure"], ["SME"] = ["Small and medium-sized enterprise", "Subject-matter expert"],
        ["PR"] = ["Public relations", "Pull request"], ["QA"] = ["Quality assurance"], ["QC"] = ["Quality control"], ["R&D"] = ["Research and development"],
        ["T&C"] = ["Terms and conditions"], ["ToS"] = ["Terms of service"], ["COGS"] = ["Cost of goods sold"], ["CPA"] = ["Certified public accountant", "Cost per acquisition"],
        ["ETF"] = ["Exchange-traded fund"], ["APR"] = ["Annual percentage rate"], ["ACH"] = ["Automated Clearing House (US bank transfer)"],
        ["IBAN"] = ["International Bank Account Number"], ["SWIFT"] = ["Bank identifier network (Society for Worldwide Interbank Financial Telecommunication)"],
        // Technology
        ["AI"] = ["Artificial intelligence"], ["ML"] = ["Machine learning"], ["LLM"] = ["Large language model"], ["NLP"] = ["Natural language processing"],
        ["API"] = ["Application programming interface"], ["SDK"] = ["Software development kit"], ["IDE"] = ["Integrated development environment"],
        ["UI"] = ["User interface"], ["UX"] = ["User experience"], ["URL"] = ["Uniform Resource Locator (web address)"], ["URI"] = ["Uniform Resource Identifier"],
        ["HTTP"] = ["Hypertext Transfer Protocol"], ["HTTPS"] = ["HTTP Secure (encrypted web traffic)"], ["HTML"] = ["Hypertext Markup Language"],
        ["CSS"] = ["Cascading Style Sheets"], ["JSON"] = ["JavaScript Object Notation"], ["XML"] = ["Extensible Markup Language"], ["SQL"] = ["Structured Query Language"],
        ["DB"] = ["Database"], ["OS"] = ["Operating system"], ["CPU"] = ["Central processing unit"], ["GPU"] = ["Graphics processing unit"],
        ["RAM"] = ["Random-access memory"], ["SSD"] = ["Solid-state drive"], ["HDD"] = ["Hard disk drive"], ["IP"] = ["Internet Protocol (address)", "Intellectual property"],
        ["DNS"] = ["Domain Name System"], ["VPN"] = ["Virtual private network"], ["LAN"] = ["Local area network"], ["WAN"] = ["Wide area network"],
        ["IoT"] = ["Internet of Things"], ["SSO"] = ["Single sign-on"], ["MFA"] = ["Multi-factor authentication"], ["2FA"] = ["Two-factor authentication"],
        ["OAuth"] = ["Open Authorization (login delegation standard)"], ["JWT"] = ["JSON Web Token"], ["SSH"] = ["Secure Shell"], ["FTP"] = ["File Transfer Protocol"],
        ["CLI"] = ["Command-line interface"], ["GUI"] = ["Graphical user interface"], ["CI"] = ["Continuous integration"], ["CD"] = ["Continuous delivery / deployment", "Compact disc"],
        ["CI/CD"] = ["Continuous integration and continuous delivery"], ["PRD"] = ["Product requirements document"], ["MVP"] = ["Minimum viable product", "Most valuable player"],
        ["POC"] = ["Proof of concept", "Point of contact"], ["SPA"] = ["Single-page application"], ["PWA"] = ["Progressive web app"], ["CDN"] = ["Content delivery network"],
        ["AWS"] = ["Amazon Web Services"], ["GCP"] = ["Google Cloud Platform"], ["VM"] = ["Virtual machine"], ["K8s"] = ["Kubernetes"],
        ["REST"] = ["Representational State Transfer (web API style)"], ["CRUD"] = ["Create, read, update, delete"], ["ORM"] = ["Object-relational mapping"],
        ["DOM"] = ["Document Object Model"], ["CSV"] = ["Comma-separated values"], ["PNG"] = ["Portable Network Graphics (image format)"], ["JPEG"] = ["JPEG image format"],
        ["GIF"] = ["Graphics Interchange Format"], ["SVG"] = ["Scalable Vector Graphics"], ["MP3"] = ["MPEG audio layer 3"], ["MP4"] = ["MPEG-4 video container"],
        ["iOS"] = ["Apple's iPhone operating system"], ["macOS"] = ["Apple's Mac operating system"], ["PaaS"] = ["Platform as a service"],
        ["IaaS"] = ["Infrastructure as a service"], ["GmbH"] = ["German limited liability company (Gesellschaft mit beschraenkter Haftung)"],
        ["mRNA"] = ["Messenger RNA"], ["APK"] = ["Android application package"], ["EXE"] = ["Windows executable file"], ["DLL"] = ["Dynamic-link library"],
        ["BIOS"] = ["Basic Input/Output System"], ["UEFI"] = ["Unified Extensible Firmware Interface"], ["GB"] = ["Gigabyte"], ["MB"] = ["Megabyte"], ["KB"] = ["Kilobyte"], ["TB"] = ["Terabyte"],
        ["FPS"] = ["Frames per second"], ["DPI"] = ["Dots per inch"], ["RGB"] = ["Red, green, blue"], ["HDMI"] = ["High-Definition Multimedia Interface"],
        ["NFC"] = ["Near-field communication"], ["SIM"] = ["Subscriber identity module"], ["SMS"] = ["Short Message Service (text message)"], ["OTP"] = ["One-time password"],
        ["CAPTCHA"] = ["Challenge test telling humans and bots apart"], ["RSS"] = ["Really Simple Syndication (feed)"], ["SEO"] = ["Search engine optimization"],
        ["CMS"] = ["Content management system"], ["CTA"] = ["Call to action"], ["CTR"] = ["Click-through rate"], ["UTM"] = ["Urchin Tracking Module (campaign link tags)"],
        ["GDPR"] = ["General Data Protection Regulation (EU privacy law)"], ["PII"] = ["Personally identifiable information"], ["RBAC"] = ["Role-based access control"],
        ["WYSIWYG"] = ["What you see is what you get"], ["RTFM"] = ["Read the manual"], ["LGTM"] = ["Looks good to me"], ["WIP"] = ["Work in progress"],
        // Science / medicine / misc
        ["DNA"] = ["Deoxyribonucleic acid"], ["RNA"] = ["Ribonucleic acid"], ["BMI"] = ["Body mass index"], ["BP"] = ["Blood pressure", "British Petroleum"],
        ["ER"] = ["Emergency room"], ["ICU"] = ["Intensive care unit"], ["MRI"] = ["Magnetic resonance imaging"], ["CT"] = ["Computed tomography"],
        ["OTC"] = ["Over the counter"], ["ADHD"] = ["Attention deficit hyperactivity disorder"], ["PTSD"] = ["Post-traumatic stress disorder"],
        ["COVID"] = ["Coronavirus disease"], ["WHO"] = ["World Health Organization"], ["CDC"] = ["Centers for Disease Control and Prevention"],
        ["NASA"] = ["National Aeronautics and Space Administration"], ["UN"] = ["United Nations"], ["EU"] = ["European Union"], ["UK"] = ["United Kingdom"],
        ["USA"] = ["United States of America"], ["NATO"] = ["North Atlantic Treaty Organization"], ["FBI"] = ["Federal Bureau of Investigation"],
        ["CIA"] = ["Central Intelligence Agency"], ["IRS"] = ["Internal Revenue Service (US tax agency)"], ["DMV"] = ["Department of Motor Vehicles"],
        ["NGO"] = ["Non-governmental organization"], ["STEM"] = ["Science, technology, engineering and mathematics"], ["GPA"] = ["Grade point average"],
        ["SAT"] = ["Standardized US college admission test"], ["EST"] = ["Eastern Standard Time"], ["PST"] = ["Pacific Standard Time"], ["GMT"] = ["Greenwich Mean Time"],
        ["UTC"] = ["Coordinated Universal Time"], ["CET"] = ["Central European Time"], ["MPH"] = ["Miles per hour"], ["KPH"] = ["Kilometres per hour"],
        ["RPM"] = ["Revolutions per minute"], ["BBQ"] = ["Barbecue"], ["BYOD"] = ["Bring your own device"], ["HQ"] = ["Headquarters"],
    };
}

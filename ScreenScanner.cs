using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Calisto;

/// <summary>One abbreviation found on screen. ScreenRect is in physical screen pixels.</summary>
public sealed record AbbreviationHit(string Abbreviation, string Context, Rect ScreenRect);

/// <summary>Reads the text of a window through UI Automation and locates abbreviations.</summary>
public static class ScreenScanner
{
    private const int MaxTextElements = 400;
    private const int MaxDistinctAbbreviations = 60;
    private const int MaxHits = 250;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(6);

    public static List<AbbreviationHit> Scan(IntPtr hwnd)
    {
        var deadline = DateTime.UtcNow + Budget;
        var hits = new List<AbbreviationHit>();
        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch { return hits; }

        var bounds = NativeMethods.GetWindowBounds(hwnd);
        if (bounds.IsEmpty)
            bounds = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        // 1) Elements that expose real text ranges (documents, edit boxes, browser pages).
        AutomationElementCollection? textElements = null;
        try
        {
            var textCondition = new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true);
            textElements = root.FindAll(TreeScope.Element | TreeScope.Descendants, textCondition);
        }
        catch { }

        int processed = 0;
        if (textElements is not null)
        {
            foreach (AutomationElement el in textElements)
            {
                if (DateTime.UtcNow > deadline || hits.Count >= MaxHits || processed++ >= MaxTextElements) break;
                try { ScanTextPattern(el, bounds, hits, deadline); } catch { /* element vanished mid-scan */ }
            }
        }

        // 2) Fallback: labels, links and buttons that only expose a Name.
        if (hits.Count == 0)
        {
            try
            {
                var nameCondition = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                var named = root.FindAll(TreeScope.Descendants, nameCondition);
                int n = 0;
                foreach (AutomationElement el in named)
                {
                    if (DateTime.UtcNow > deadline || hits.Count >= MaxHits || n++ >= MaxTextElements) break;
                    try { ScanName(el, bounds, hits); } catch { }
                }
            }
            catch { }
        }

        return Dedupe(hits);
    }

    private static void ScanTextPattern(AutomationElement el, Rect bounds, List<AbbreviationHit> hits, DateTime deadline)
    {
        if (el.GetCurrentPattern(TextPattern.Pattern) is not TextPattern tp) return;
        var doc = tp.DocumentRange;
        string text = doc.GetText(200_000);
        if (string.IsNullOrWhiteSpace(text)) return;

        var candidates = AbbreviationDetector.FindDistinct(text).Take(MaxDistinctAbbreviations).ToList();
        foreach (var abbr in candidates)
        {
            if (DateTime.UtcNow > deadline || hits.Count >= MaxHits) return;
            var search = doc.Clone();
            int searchFrom = 0;
            for (int guard = 0; guard < 40; guard++)
            {
                TextPatternRange? found;
                try { found = search.FindText(abbr, false, false); } catch { break; }
                if (found is null) break;

                // Next iteration searches after this occurrence.
                search.MoveEndpointByRange(TextPatternRangeEndpoint.Start, found, TextPatternRangeEndpoint.End);

                // Reject partial matches inside longer words (e.g. "AI" inside "RAIN").
                var word = found.Clone();
                word.ExpandToEnclosingUnit(TextUnit.Word);
                var wordText = word.GetText(64).Trim().Trim(AbbreviationDetector.Punctuation);
                if (!string.Equals(wordText, abbr, StringComparison.Ordinal)) continue;

                int idx = AbbreviationDetector.IndexOfWord(text, abbr, searchFrom);
                if (idx >= 0) searchFrom = idx + abbr.Length;
                string context = AbbreviationDetector.ContextAround(text, idx < 0 ? 0 : idx, abbr.Length);

                foreach (var r in found.GetBoundingRectangles())
                {
                    if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || r.Width > 600) continue;
                    if (!bounds.IntersectsWith(r)) continue;
                    hits.Add(new AbbreviationHit(abbr, context, r));
                    break;
                }
            }
        }
    }

    private static void ScanName(AutomationElement el, Rect bounds, List<AbbreviationHit> hits)
    {
        if (el.Current.IsOffscreen) return;
        string name = el.Current.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 2000) return;
        var rect = el.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || !bounds.IntersectsWith(rect)) return;

        foreach (var abbr in AbbreviationDetector.FindDistinct(name).Take(5))
        {
            int idx = name.IndexOf(abbr, StringComparison.Ordinal);
            string context = AbbreviationDetector.ContextAround(name, idx, abbr.Length);
            // Only the whole element's position is known, so mark its top-right corner.
            var dot = new Rect(rect.Right - 12, rect.Top, 12, Math.Min(rect.Height, 20));
            hits.Add(new AbbreviationHit(abbr, context, dot));
        }
    }

    private static List<AbbreviationHit> Dedupe(List<AbbreviationHit> hits)
    {
        var result = new List<AbbreviationHit>();
        foreach (var h in hits)
        {
            bool dup = result.Any(o => o.Abbreviation == h.Abbreviation &&
                                       Math.Abs(o.ScreenRect.X - h.ScreenRect.X) < 8 &&
                                       Math.Abs(o.ScreenRect.Y - h.ScreenRect.Y) < 8);
            if (!dup) result.Add(h);
        }
        return result;
    }
}

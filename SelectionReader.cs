using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Calisto;

/// <summary>The text the user has selected in another app, with its surroundings and where it is on screen (physical pixels).</summary>
public sealed record Selection(string Text, string Context, Rect? ScreenRect);

/// <summary>Reads the current selection: through UI Automation when the app exposes it, otherwise via a copy to the clipboard.</summary>
public static class SelectionReader
{
    public static async Task<Selection?> ReadAsync()
    {
        var viaUia = await Task.Run(ReadViaAutomation);
        if (viaUia is not null && viaUia.Text.Length > 0) return viaUia;
        return await ReadViaClipboardAsync();
    }

    private static Selection? ReadViaAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return null;
            if (focused.GetCurrentPattern(TextPattern.Pattern) is not TextPattern tp) return null;
            var ranges = tp.GetSelection();
            if (ranges is null || ranges.Length == 0) return null;
            var range = ranges[0];
            var text = Clean(range.GetText(2000));
            if (text.Length == 0) return null;

            // Context: the surrounding paragraph (or line) around the selection.
            string context = text;
            try
            {
                var around = range.Clone();
                around.ExpandToEnclosingUnit(TextUnit.Paragraph);
                var para = Clean(around.GetText(2000));
                if (para.Length < 20) { around = range.Clone(); around.ExpandToEnclosingUnit(TextUnit.Line); para = Clean(around.GetText(2000)); }
                if (para.Length > text.Length) context = para;
            }
            catch { }

            Rect? rect = null;
            try
            {
                var rects = range.GetBoundingRectangles();
                if (rects.Length > 0 && rects[0].Width > 0) rect = rects[0];
            }
            catch { }
            return new Selection(text, context, rect);
        }
        catch { return null; }
    }

    private static async Task<Selection?> ReadViaClipboardAsync()
    {
        // Remember what the clipboard held so the user does not lose it.
        string? previous = null;
        bool hadText = false;
        try { if (Clipboard.ContainsText()) { previous = Clipboard.GetText(); hadText = true; } } catch { }

        // Release the hotkey's own modifiers first, then send Ctrl+C.
        foreach (var vk in new[] { NativeMethods.VK_MENU, NativeMethods.VK_SHIFT, NativeMethods.VK_LWIN }) NativeMethods.KeyUp(vk);
        NativeMethods.KeyDown(NativeMethods.VK_CONTROL);
        NativeMethods.TapKey(NativeMethods.VK_C);
        NativeMethods.KeyUp(NativeMethods.VK_CONTROL);
        await Task.Delay(180);

        string copied = "";
        for (int attempt = 0; attempt < 3 && copied.Length == 0; attempt++)
        {
            try { if (Clipboard.ContainsText()) copied = Clipboard.GetText(); } catch { await Task.Delay(60); }
        }

        try { if (hadText && previous is not null && previous != copied) Clipboard.SetText(previous); } catch { }

        var text = Clean(copied);
        if (text.Length == 0 || text == Clean(previous ?? "")) return null;
        return new Selection(text, text, null);
    }

    private static string Clean(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim().Trim('​');
}

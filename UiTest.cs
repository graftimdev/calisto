using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Calisto;

/// <summary>
/// "Calisto.exe --uitest notepad": drives the real UI (menu, settings, passive tags, definition card) with the named
/// process's window in front and saves screenshots to %TEMP%\calisto-uitest\ so the flow can be checked without a human.
/// </summary>
public static class UiTest
{
    public static string OutDir => Path.Combine(Path.GetTempPath(), "calisto-uitest");

    public static async Task RunAsync(FloatingIconWindow icon, string processName)
    {
        Directory.CreateDirectory(OutDir);
        var log = new List<string>();
        try
        {
            var proc = Process.GetProcessesByName(processName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            log.Add(proc is null ? $"no window for '{processName}'" : $"target: {proc.MainWindowTitle}");

            await Task.Delay(600);
            Capture("1-icon");

            icon.OpenMenuForTest();
            await Task.Delay(600);
            Capture("2-menu");

            // Press every main-menu button through UI Automation, exactly like a mouse click would.
            foreach (var name in new[] { "Settings", "Add API key", "Check for updates" })
            {
                icon.OpenMenuForTest();
                await Task.Delay(400);
                bool clicked = await Task.Run(() => ClickOwnButton("Calisto menu", name));
                await Task.Delay(700);
                log.Add($"clicked '{name}': {clicked}");
                Capture($"3-after-{name.Replace(' ', '-')}");
                // back to the main page
                await Task.Run(() => ClickOwnButton("Calisto menu", "Back"));
                await Task.Delay(300);
            }
            icon.OpenSettingsForTest();
            await Task.Delay(600);
            Capture("3-settings");
            icon.HideMenuForTest();

            // Passive scan needs the target in front; bring it forward and give the scanner a few ticks.
            if (proc is not null)
            {
                SetForegroundWindow(proc.MainWindowHandle);
                await Task.Delay(3500);
                Capture("4-passive-tags");

                var hits = ScreenScanner.Scan(proc.MainWindowHandle);
                log.Add($"passive scan hits: {hits.Count}: {string.Join(" ", hits.Select(h => h.Abbreviation))}");
                if (hits.Count > 0)
                {
                    var h = hits[0];
                    DefinitionWindow.Show(h.Abbreviation, h.Context, h.ScreenRect);
                    await Task.Delay(4000);
                    Capture("5-definition");
                    // Click outside the card (bottom-left of the target window): the card must close itself without errors.
                    var b = NativeMethods.GetWindowBounds(proc.MainWindowHandle);
                    ClickAt((int)(b.Left + 60), (int)(b.Bottom - 60));
                    await Task.Delay(600);
                    log.Add("card closed by outside click: " + (DefinitionWindowOpen() ? "no" : "yes"));
                    DefinitionWindow.CloseCurrent();
                }

                // Word lookup: select the word "Spain" in the target through UI Automation, then run the hotkey handler.
                SetForegroundWindow(proc.MainWindowHandle);
                await Task.Delay(300);
                bool selected = await Task.Run(() => SelectWord(proc.MainWindowHandle, "Spain"));
                log.Add("selected word: " + selected);
                await Task.Delay(300);
                await icon.LookupSelectionForTest();
                await Task.Delay(3000);
                Capture("6-word-lookup");
                DefinitionWindow.CloseCurrent();
            }
            log.Add("ok");
        }
        catch (Exception ex) { log.Add("error: " + ex); }
        File.WriteAllLines(Path.Combine(OutDir, "log.txt"), log);
        Application.Current.Shutdown();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);   // left down
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);   // left up
    }

    private static bool DefinitionWindowOpen() =>
        Application.Current.Windows.OfType<DefinitionWindow>().Any(w => w.IsVisible);

    /// <summary>Finds a button by its visible text (or tooltip) in one of Calisto's own windows and invokes it.</summary>
    private static bool ClickOwnButton(string windowTitle, string buttonName)
    {
        try
        {
            var root = System.Windows.Automation.AutomationElement.RootElement;
            var win = root.FindFirst(System.Windows.Automation.TreeScope.Children,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, windowTitle));
            if (win is null) return false;
            var buttons = win.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Button));
            foreach (System.Windows.Automation.AutomationElement b in buttons)
            {
                var name = b.Current.Name ?? "";
                var help = b.Current.HelpText ?? "";
                if (!name.Contains(buttonName, StringComparison.OrdinalIgnoreCase) && !help.Contains(buttonName, StringComparison.OrdinalIgnoreCase)) continue;
                if (b.Current.IsOffscreen) continue;
                if (b.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern) is System.Windows.Automation.InvokePattern inv) { inv.Invoke(); return true; }
            }
            return false;
        }
        catch { return false; }
    }

    private static bool SelectWord(IntPtr hwnd, string word)
    {
        try
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            var cond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.IsTextPatternAvailableProperty, true);
            var el = root.FindFirst(System.Windows.Automation.TreeScope.Element | System.Windows.Automation.TreeScope.Descendants, cond);
            if (el?.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern) is not System.Windows.Automation.TextPattern tp) return false;
            var range = tp.DocumentRange.FindText(word, false, false);
            if (range is null) return false;
            range.Select();
            el.SetFocus();
            return true;
        }
        catch { return false; }
    }

    private static void Capture(string name)
    {
        int x = NativeMethods.GetSystemMetrics(76), y = NativeMethods.GetSystemMetrics(77);
        int w = NativeMethods.GetSystemMetrics(78), h = NativeMethods.GetSystemMetrics(79);
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, bmp.Size);
        bmp.Save(Path.Combine(OutDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}

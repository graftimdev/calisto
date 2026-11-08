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

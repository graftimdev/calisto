using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Calisto;

/// <summary>
/// "Calisto.exe --uitest notepad": drives the real UI (menu, scan, results) against the named process's window and
/// saves screenshots to %TEMP%\calisto-uitest\ so the flow can be checked without a human at the keyboard.
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

            if (proc is not null)
            {
                var scan = icon.ScanForTest(proc.MainWindowHandle);
                await Task.Delay(900);
                Capture("3-scanning");
                await scan;
                await Task.Delay(500);
                Capture("4-results");
                log.Add($"results: {icon.ResultCountForTest} abbreviations");

                await icon.ExpandFirstResultForTest();
                await Task.Delay(500);
                Capture("5-expanded");
            }

            icon.SetOpacityForTest(0.4);
            await Task.Delay(400);
            Capture("6-transparent");
            log.Add("ok");
        }
        catch (Exception ex) { log.Add("error: " + ex); }
        File.WriteAllLines(Path.Combine(OutDir, "log.txt"), log);
        Application.Current.Shutdown();
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

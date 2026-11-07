using System.Windows;

namespace Calisto;

public partial class App : Application
{
    public static Settings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = Settings.Load();

        // Diagnostic mode: "Calisto.exe --selftest notepad" scans that process's main window and writes the hits to %TEMP%.
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            RunSelfTest(e.Args[1]);
            Shutdown();
            return;
        }

        // Diagnostic mode: "Calisto.exe --lookup TERM "context sentence"" writes the lookup result to %TEMP%\calisto-lookup.txt.
        if (e.Args.Length >= 3 && e.Args[0] == "--lookup")
        {
            var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "calisto-lookup.txt");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Run on the thread pool: blocking the UI thread on an await would deadlock the WPF dispatcher.
            var r = Task.Run(() => AbbreviationLookup.LookupAsync(e.Args[1], e.Args[2])).GetAwaiter().GetResult();
            System.IO.File.WriteAllText(outPath, $"[{e.Args[1]}] lang={LanguageDetector.Detect(e.Args[2])} {sw.ElapsedMilliseconds} ms\n{r.Text}\n-- {r.Source}\n");
            Shutdown();
            return;
        }

        if (e.Args.Contains("--uninstall"))
        {
            var answer = e.Args.Contains("--silent") ? MessageBoxResult.Yes
                : MessageBox.Show("Remove Calisto from this PC?", "Calisto", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) Installer.Uninstall();
            Shutdown();
            return;
        }

        // Silent install (no windows, does not launch): used by scripts and the self-test.
        if (e.Args.Contains("--install"))
        {
            Installer.Install(launch: false);
            Shutdown();
            return;
        }

        bool uiTest = e.Args.Length >= 1 && e.Args[0] == "--uitest";

        // A downloaded copy offers to install itself so Calisto shows up in the Start menu like any other app.
        if (uiTest) { /* skip setup screens */ }
        else if (!Installer.IsRunningFromInstallDir && !Settings.Consented)
        {
            var setup = new SetupWindow();
            var choice = setup.ShowDialog() == true ? setup.Choice : SetupChoice.Cancel;
            switch (choice)
            {
                case SetupChoice.Cancel:
                    Shutdown();
                    return;
                case SetupChoice.Install:
                    Settings.Consented = true;
                    Settings.Save();
                    try
                    {
                        Installer.Install();            // copies the exe, adds Start menu entry, launches the installed copy
                        Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not install Calisto: " + ex.Message + "\n\nRunning without installing instead.",
                            "Calisto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;
                case SetupChoice.RunPortable:
                    Settings.Consented = true;
                    Settings.Save();
                    break;
            }
        }
        else if (!Settings.Consented)
        {
            var setup = new SetupWindow(installedCopy: true);
            if (setup.ShowDialog() != true) { Shutdown(); return; }
            Settings.Consented = true;
            Settings.Save();
        }

        var icon = new FloatingIconWindow();
        icon.Show();

        if (e.Args.Length >= 2 && e.Args[0] == "--uitest")
            _ = UiTest.RunAsync(icon, e.Args[1]);
    }

    private static void RunSelfTest(string processName)
    {
        var outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "calisto-selftest.txt");
        var lines = new List<string>();
        var proc = System.Diagnostics.Process.GetProcessesByName(processName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (proc is null) { lines.Add($"No window found for process '{processName}'."); }
        else
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hits = ScreenScanner.Scan(proc.MainWindowHandle);
            lines.Add($"Scanned '{proc.MainWindowTitle}' in {sw.ElapsedMilliseconds} ms, {hits.Count} hits:");
            lines.AddRange(hits.Select(h => $"  {h.Abbreviation,-10} ctx: {Truncate(h.Context, 70)}"));
        }
        System.IO.File.WriteAllLines(outPath, lines);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}

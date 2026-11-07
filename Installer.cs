using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Calisto;

/// <summary>
/// Makes the single downloaded Calisto.exe behave like an installed app: copies itself to the per-user Programs
/// folder, adds a Start menu shortcut (so Windows search finds "Calisto") and an entry in Apps and features.
/// No administrator rights are needed.
/// </summary>
public static class Installer
{
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);

    public static string InstalledExe => Path.Combine(InstallDir, AppInfo.Name + ".exe");

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;

    public static string CurrentExe => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    public static bool IsRunningFromInstallDir =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static void Install(bool launch = true)
    {
        Directory.CreateDirectory(InstallDir);

        // Stop an already-installed copy so the file can be replaced (this is also the update path).
        foreach (var p in Process.GetProcessesByName(AppInfo.Name))
        {
            if (p.Id == Environment.ProcessId) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        File.Copy(CurrentExe, InstalledExe, overwrite: true);
        CreateShortcut(ShortcutPath, InstalledExe);
        RegisterUninstall();

        if (launch)
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
    }

    public static void Uninstall()
    {
        try { File.Delete(ShortcutPath); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        Settings.DeleteAll();

        // The running exe cannot delete itself; a detached cmd removes the folder once this process has exited.
        var cmd = $"/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"{InstallDir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
        shortcut.IconLocation = targetExe + ",0";
        shortcut.Description = "Calisto - explains abbreviations on your screen";
        shortcut.Save();
    }

    private static void RegisterUninstall()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppInfo.Name);
        key.SetValue("DisplayVersion", AppInfo.Current.ToString(3));
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("Publisher", AppInfo.Name);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long size = 0;
        try { size = new FileInfo(InstalledExe).Length / 1024; } catch { }
        key.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
    }
}

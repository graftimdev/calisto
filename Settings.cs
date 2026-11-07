using System.IO;
using System.Text.Json;

namespace Calisto;

public sealed class Settings
{
    public bool Consented { get; set; }
    public string ApiKey { get; set; } = "";
    /// <summary>GitHub "latest release" API URL used by "Check for updates".</summary>
    public string UpdateUrl { get; set; } = AppInfo.DefaultUpdateUrl;
    /// <summary>Opacity (0.25-1.0) of the floating icon and the menu.</summary>
    public double Opacity { get; set; } = 0.85;
    public double? IconLeft { get; set; }
    public double? IconTop { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                s.Opacity = Math.Clamp(s.Opacity, 0.25, 1.0);
                if (string.IsNullOrWhiteSpace(s.UpdateUrl) || s.UpdateUrl.Contains("/OWNER/"))
                    s.UpdateUrl = AppInfo.DefaultUpdateUrl;   // settings saved by a pre-release build
                return s;
            }
        }
        catch { /* corrupt file: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: keep running with in-memory settings */ }
    }

    public static void DeleteAll()
    {
        try { Directory.Delete(Path.GetDirectoryName(FilePath)!, true); } catch { }
    }
}

public static class AppInfo
{
    public const string Name = "Calisto";
    /// <summary>GitHub repository ("owner/name") that hosts the releases. Only this line needs to change for a fork.</summary>
    public const string GitHubRepo = "graftimdev/Calisto";
    public const string DefaultUpdateUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
    public static Version Current => typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);
}

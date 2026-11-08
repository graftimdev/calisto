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

    /// <summary>Quietly scan the active window and tag abbreviations with a "def" button.</summary>
    public bool PassiveScan { get; set; } = true;
    /// <summary>"auto" or a Wikipedia language code ("en", "ru", ...). Definitions come only from that language when set.</summary>
    public string Language { get; set; } = "auto";
    /// <summary>Global hotkey that looks up the selected word, e.g. "Ctrl+Alt+C".</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+C";
    /// <summary>When the community contributions file was last downloaded.</summary>
    public DateTime? ContributionsSyncedUtc { get; set; }

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    private static string FilePath => Path.Combine(Folder, "settings.json");

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
                if (string.IsNullOrWhiteSpace(s.Hotkey)) s.Hotkey = "Ctrl+Alt+C";
                if (string.IsNullOrWhiteSpace(s.Language)) s.Language = "auto";
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
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile: keep running with in-memory settings */ }
    }

    public static void DeleteAll()
    {
        try { Directory.Delete(Folder, true); } catch { }
    }
}

public static class AppInfo
{
    public const string Name = "Calisto";
    /// <summary>GitHub repository ("owner/name") that hosts the releases. Only this line needs to change for a fork.</summary>
    public const string GitHubRepo = "graftimdev/Calisto";
    public const string DefaultUpdateUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
    public const string RepoUrl = "https://github.com/" + GitHubRepo;
    /// <summary>Raw URL of the community contributions file on the main branch.</summary>
    public const string ContributionsUrl = "https://raw.githubusercontent.com/" + GitHubRepo + "/main/contributions.json";
    /// <summary>Offline dictionary pack attached to the latest release.</summary>
    public const string PackDownloadUrl = RepoUrl + "/releases/latest/download/pack-en.json";
    public static Version Current => typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);
}

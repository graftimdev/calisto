using System.Text.RegularExpressions;

namespace Calisto;

/// <summary>Validates a user-submitted definition and turns it into a pre-filled GitHub issue.</summary>
public static class Contribution
{
    // Deliberately short: the goal is to block obvious abuse, not to censor dictionaries (swear words can have definitions).
    private static readonly string[] Blocked =
    {
        "nigger", "faggot", "retard", "kill yourself", "kys", "heil hitler", "хохол", "хач", "жид", "пидор", "сдохни"
    };

    public static string? Validate(string word, string definition)
    {
        word = word.Trim(); definition = definition.Trim();
        if (word.Length == 0) return "Enter the word or abbreviation.";
        if (word.Length > 40) return "The word is too long (40 characters max).";
        if (!word.Any(char.IsLetter)) return "The word needs at least one letter.";
        if (definition.Length < 3) return "Enter a definition.";
        if (definition.Length > 300) return "Keep the definition under 300 characters.";
        if (Regex.IsMatch(definition + word, @"https?://|www\.", RegexOptions.IgnoreCase)) return "Links are not accepted in definitions.";
        if (string.Equals(word, definition, StringComparison.OrdinalIgnoreCase)) return "The definition must say more than the word itself.";
        var lower = (word + " " + definition).ToLowerInvariant();
        if (Blocked.Any(b => lower.Contains(b))) return "That wording is not appropriate for a shared dictionary.";
        if (Regex.IsMatch(definition, @"(.)\1{6,}")) return "That does not look like a real definition.";
        return null;
    }

    public static string IssueUrl(string word, string definition, string language)
    {
        var title = $"[Contribution] {word.Trim()}";
        var body = $"Word: {word.Trim()}\nLanguage: {language}\nDefinition: {definition.Trim()}\n\n_Submitted from Calisto {AppInfo.Current.ToString(3)}._";
        return $"{AppInfo.RepoUrl}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}&labels={Uri.EscapeDataString("contribution")}";
    }
}

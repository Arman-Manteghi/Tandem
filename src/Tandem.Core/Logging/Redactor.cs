using System.Text.RegularExpressions;

namespace Tandem.Core.Logging;

/// <summary>Removes personal data and secrets from text before it reaches a log file.</summary>
public static partial class Redactor
{
    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Redact(string text) => Redact(text, UserProfile);

    internal static string Redact(string text, string userProfile)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Links may carry one-time sign-in codes: keep only the scheme.
        var result = ClaudeLinkPattern().Replace(text, "claude:<redacted>");

        if (!string.IsNullOrEmpty(userProfile))
        {
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    [GeneratedRegex(@"\bclaude:[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeLinkPattern();
}

namespace Tandem.Core.Claude;

/// <summary>
/// claude:// links are passed through to the official app exactly as received. They can carry
/// one-time sign-in codes, so they are never logged, stored or shown.
/// </summary>
public static class ClaudeLink
{
    public const string Scheme = "claude";
    public const int MaxLength = 8192;
    private const string Prefix = Scheme + ":";

    public static bool IsValid(string? link)
    {
        if (string.IsNullOrEmpty(link) || link.Length > MaxLength || link.Length <= Prefix.Length)
        {
            return false;
        }

        if (!link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var ch in link)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch) || ch == '"')
            {
                return false;
            }
        }

        return true;
    }
}

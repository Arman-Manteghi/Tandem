namespace Tandem.Core;

internal static class PathUtil
{
    /// <summary>Full path without a trailing separator, suitable for case-insensitive comparison.</summary>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));

    public static bool AreSame(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsStrictlyUnder(string normalizedPath, string normalizedRoot) =>
        normalizedPath.Length > normalizedRoot.Length + 1
        && normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
        && normalizedPath[normalizedRoot.Length] == Path.DirectorySeparatorChar;

    public static bool IsSameOrUnder(string normalizedPath, string normalizedRoot) =>
        string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
        || IsStrictlyUnder(normalizedPath, normalizedRoot);
}

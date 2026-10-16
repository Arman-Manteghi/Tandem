using Tandem.Core.Claude;

namespace Tandem.Core.Safety;

/// <summary>
/// Claude's MSIX package virtualizes writes under AppData. If a profile folder does not exist
/// yet when Claude starts, Windows silently redirects the whole profile into the package's
/// private LocalCache (see docs/FINDINGS.md). These helpers detect that situation.
/// </summary>
public static class MsixVirtualization
{
    public static string? GetPackageMirror(string path) => GetPackageMirror(
        path,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Where Windows would put <paramref name="path"/> if Claude's package created it, or null.</summary>
    internal static string? GetPackageMirror(string path, string roamingAppData, string localAppData)
    {
        var full = PathUtil.Normalize(path);
        var roaming = PathUtil.Normalize(roamingAppData);
        var local = PathUtil.Normalize(localAppData);
        var packages = Path.Combine(local, "Packages");
        var cache = Path.Combine(packages, ClaudePackage.FamilyName, "LocalCache");

        if (PathUtil.IsSameOrUnder(full, packages))
        {
            return null;
        }

        if (PathUtil.IsStrictlyUnder(full, roaming))
        {
            return Path.Combine(cache, "Roaming", Path.GetRelativePath(roaming, full));
        }

        if (PathUtil.IsStrictlyUnder(full, local))
        {
            return Path.Combine(cache, "Local", Path.GetRelativePath(local, full));
        }

        return null;
    }

    public static bool HasPackageMirror(string path) =>
        GetPackageMirror(path) is { } mirror && Directory.Exists(mirror);
}

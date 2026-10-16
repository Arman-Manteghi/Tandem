namespace Tandem.Core.Claude;

public enum ClaudeInstallKind
{
    NotInstalled,
    Msix,
    Squirrel,
}

/// <summary>Where the official app is. Re-resolved on every launch, so updates are transparent.</summary>
public sealed record ClaudeInstallation(
    ClaudeInstallKind Kind,
    Version? Version,
    string? AliasPath,
    string? ExecutablePath,
    string? PackageFullName)
{
    public static ClaudeInstallation None { get; } = new(ClaudeInstallKind.NotInstalled, null, null, null, null);

    public bool IsInstalled => Kind != ClaudeInstallKind.NotInstalled;
}

public static class ClaudeLocator
{
    public static ClaudeInstallation Locate()
    {
        var packages = PackageQueries.FindPackagesByFamily(ClaudePackage.FamilyName);
        if (packages.Count > 0)
        {
            var fullName = packages
                .OrderByDescending(p => PackageQueries.ParseVersion(p) ?? new Version(0, 0))
                .First();
            var installDir = PackageQueries.GetPackagePath(fullName);
            var exe = installDir is null ? null : Path.Combine(installDir, ClaudePackage.RelativeExecutable);
            var alias = ClaudePackage.AliasPath;
            return new ClaudeInstallation(
                ClaudeInstallKind.Msix,
                PackageQueries.ParseVersion(fullName),
                File.Exists(alias) ? alias : null,
                exe,
                fullName);
        }

        var squirrel = SquirrelLayout.FindLatest(ClaudePackage.SquirrelRoot);
        if (squirrel is not null)
        {
            return new ClaudeInstallation(ClaudeInstallKind.Squirrel, squirrel.Value.Version, null, squirrel.Value.Executable, null);
        }

        return ClaudeInstallation.None;
    }
}

/// <summary>Legacy Squirrel layout: %LOCALAPPDATA%\AnthropicClaude\app-&lt;version&gt;\claude.exe.</summary>
internal static class SquirrelLayout
{
    private const string ExecutableName = "claude.exe";

    public static (string Executable, Version Version)? FindLatest(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        (string, Version)? best = null;
        foreach (var dir in Directory.EnumerateDirectories(root, "app-*"))
        {
            var exe = Path.Combine(dir, ExecutableName);
            if (TryParseVersion(dir, out var version) && File.Exists(exe) && (best is null || version > best.Value.Item2))
            {
                best = (exe, version);
            }
        }

        return best;
    }

    public static bool IsAppExecutable(string imagePath, string root)
    {
        var dir = Path.GetDirectoryName(imagePath);
        return dir is not null
            && string.Equals(Path.GetFileName(imagePath), ExecutableName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(dir), PathUtil.Normalize(root), StringComparison.OrdinalIgnoreCase)
            && TryParseVersion(dir, out _);
    }

    private static bool TryParseVersion(string dir, out Version version)
    {
        var name = Path.GetFileName(dir);
        if (!name.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            version = new Version(0, 0);
            return false;
        }

        return Version.TryParse(name["app-".Length..], out version!);
    }
}

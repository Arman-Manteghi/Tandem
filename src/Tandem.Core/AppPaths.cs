namespace Tandem.Core;

/// <summary>
/// Folders and files owned by Tandem. None of them is inside a Claude data folder.
/// </summary>
public sealed class AppPaths
{
    public const string ProductName = "Tandem";

    public AppPaths(string roamingRoot, string localRoot)
    {
        RoamingDir = Path.Combine(roamingRoot, ProductName);
        LocalDir = Path.Combine(localRoot, ProductName);
    }

    public static AppPaths ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>%APPDATA%\Tandem: small configuration files.</summary>
    public string RoamingDir { get; }

    /// <summary>%LOCALAPPDATA%\Tandem: profile data folders, logs, generated icons, runtime state.</summary>
    public string LocalDir { get; }

    public string ProfilesFile => Path.Combine(RoamingDir, "profiles.json");

    public string SettingsFile => Path.Combine(RoamingDir, "settings.json");

    public string StateFile => Path.Combine(LocalDir, "state.json");

    public string ProfilesRoot => Path.Combine(LocalDir, "Profiles");

    public string LogsDir => Path.Combine(LocalDir, "logs");

    public string IconsDir => Path.Combine(LocalDir, "icons");

    public string ProfileDataDir(string profileId) => Path.Combine(ProfilesRoot, profileId);

    public string ProfileIconFile(string profileId) => Path.Combine(IconsDir, profileId + ".ico");

    /// <summary>Previous versions of setup files replaced by the automatic sync, one generation.</summary>
    public string ProfileBackupDir(string profileId) => Path.Combine(LocalDir, "Backups", profileId);

    /// <summary>True when <paramref name="path"/> is a folder Tandem itself created for a profile.</summary>
    public bool IsManagedProfileFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = PathUtil.Normalize(path);
        var root = PathUtil.Normalize(ProfilesRoot);
        return PathUtil.IsStrictlyUnder(full, root)
            && string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase);
    }
}

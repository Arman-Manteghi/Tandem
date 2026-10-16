using Tandem.Core.Logging;
using Tandem.Core.Profiles;

namespace Tandem.Core.Shell;

public enum ShortcutLocation
{
    Desktop,
    StartMenu,
}

/// <summary>
/// Per-profile shortcuts and the optional "start with Windows" shortcut. Shortcuts always point
/// at Tandem (which prepares the profile folder first), never at Claude directly.
/// File-based only: no registry Run keys.
/// </summary>
public sealed class Shortcuts(AppPaths paths, string tandemExePath)
{
    public const string LaunchSwitch = "--launch";
    public const string StartupSwitch = "--startup";

    public static string StartMenuFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppPaths.ProductName);

    public static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppPaths.ProductName + ".lnk");

    public string CreateForProfile(Profile profile, ShortcutLocation location)
    {
        var icon = EnsureProfileIcon(profile);
        var folder = location == ShortcutLocation.Desktop
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : StartMenuFolder;
        var path = Path.Combine(folder, SafeFileName($"{profile.DisplayName} (Tandem)") + ".lnk");
        ShellLink.Create(
            path,
            tandemExePath,
            $"{LaunchSwitch} {profile.Id}",
            $"Open the \"{profile.DisplayName}\" account in the Claude desktop app",
            icon);
        Log.Info($"Created a {location} shortcut for profile {profile.Id}.");
        return path;
    }

    /// <summary>Shortcuts created for a profile, found by their <c>--launch &lt;id&gt;</c> argument.</summary>
    public IReadOnlyList<string> FindForProfile(string profileId)
    {
        var result = new List<string>();
        var expected = $"{LaunchSwitch} {profileId}";
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StartMenuFolder })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                if (ShellLink.Read(file) is { } link
                    && PathUtil.AreSame(link.Target, tandemExePath)
                    && string.Equals(link.Arguments.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(file);
                }
            }
        }

        return result;
    }

    public static string AppShortcutPath => Path.Combine(StartMenuFolder, AppPaths.ProductName + ".lnk");

    public bool HasAppShortcut => File.Exists(AppShortcutPath);

    /// <summary>Start menu entry for Tandem itself, so it is always started outside Claude.</summary>
    public void CreateAppShortcut()
    {
        ShellLink.Create(AppShortcutPath, tandemExePath, string.Empty, "Switch between your Claude accounts", null);
        Log.Info("Created the Start menu shortcut for Tandem.");
    }

    public bool IsStartupEnabled => File.Exists(StartupShortcutPath);

    public void EnableStartup()
    {
        ShellLink.Create(StartupShortcutPath, tandemExePath, StartupSwitch, "Start Tandem with Windows", null);
        Log.Info("Enabled start with Windows.");
    }

    /// <summary>Moves Tandem's own startup shortcut to the Recycle Bin.</summary>
    public void DisableStartup()
    {
        RecycleBin.Move(StartupShortcutPath);
        Log.Info("Disabled start with Windows.");
    }

    public string EnsureProfileIcon(Profile profile)
    {
        var path = paths.ProfileIconFile(profile.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, IconFactory.CreateIco(profile.Color, IconFactory.Cream));
        return path;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? AppPaths.ProductName : cleaned;
    }
}

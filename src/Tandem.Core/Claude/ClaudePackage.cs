namespace Tandem.Core.Claude;

/// <summary>Identifiers of the official Claude desktop app on Windows (see docs/FINDINGS.md).</summary>
public static class ClaudePackage
{
    /// <summary>MSIX package family name (stable across updates).</summary>
    public const string FamilyName = "Claude_pzs8sxrjxfjjc";

    /// <summary>Application user model id of the main app inside the package.</summary>
    public const string Aumid = FamilyName + "!Claude";

    /// <summary>App execution alias declared by the package manifest.</summary>
    public const string AliasFileName = "claude-desktop.exe";

    /// <summary>Main executable, relative to the package install folder.</summary>
    public const string RelativeExecutable = @"app\Claude.exe";

    /// <summary>Legacy (pre-MSIX) Squirrel install folder under %LOCALAPPDATA%.</summary>
    public const string SquirrelFolderName = "AnthropicClaude";

    public const string UserDataDirSwitch = "user-data-dir";

    public static string AliasPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", AliasFileName);

    public static string SquirrelRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SquirrelFolderName);
}

using System.Runtime.InteropServices;
using Microsoft.Win32;
using Tandem.Core.Claude;
using Tandem.Core.Logging;
using Tandem.Core.Processes;

namespace Tandem.Core.Integration;

public enum LinkHandlerStatus
{
    /// <summary>Tandem is not offered as a handler for claude:// links.</summary>
    Off,

    /// <summary>Tandem is offered, but Windows still sends claude:// links to another app.</summary>
    WaitingForUserChoice,

    /// <summary>The user chose Tandem for claude:// links.</summary>
    Active,

    /// <summary>Registered for a Tandem.exe at a different path (the exe was moved).</summary>
    NeedsRepair,
}

/// <summary>
/// The opt-in sign-in helper. Tandem only writes its own keys under HKCU and never touches
/// Claude's keys or the protected UserChoice: the user picks Tandem in Windows Settings.
/// </summary>
public static class LinkHandlerRegistration
{
    public const string ProgId = "Tandem.Link";
    public const string RegisteredAppName = AppPaths.ProductName;
    public const string OpenUrlSwitch = "--open-url";

    private const string ProgIdKey = @"Software\Classes\" + ProgId;
    private const string AppKey = @"Software\" + AppPaths.ProductName;
    private const string CapabilitiesKey = AppKey + @"\Capabilities";
    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";
    private const string UserChoiceKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + ClaudeLink.Scheme + @"\UserChoice";

    public static string SettingsUriForTandem => "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(RegisteredAppName);

    public static string SettingsUriForClaude => "ms-settings:defaultapps?registeredAUMID=" + Uri.EscapeDataString(ClaudePackage.Aumid);

    /// <summary>The keys <see cref="Register"/> writes, for showing to the user before asking.</summary>
    public static IReadOnlyList<string> KeysWritten { get; } =
    [
        @"HKCU\" + ProgIdKey,
        @"HKCU\" + CapabilitiesKey,
        @"HKCU\" + RegisteredApplicationsKey + " → value \"" + RegisteredAppName + "\"",
    ];

    public static string? CurrentUserChoiceProgId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserChoiceKey);
        return key?.GetValue("ProgId") as string;
    }

    public static LinkHandlerStatus GetStatus(string exePath)
    {
        var command = ReadCommand();
        if (command is null)
        {
            return LinkHandlerStatus.Off;
        }

        var args = CommandLine.Split(command);
        if (args.Count == 0 || !PathUtil.AreSame(args[0], exePath))
        {
            return LinkHandlerStatus.NeedsRepair;
        }

        return string.Equals(CurrentUserChoiceProgId(), ProgId, StringComparison.OrdinalIgnoreCase)
            ? LinkHandlerStatus.Active
            : LinkHandlerStatus.WaitingForUserChoice;
    }

    public static void Register(string exePath)
    {
        var command = $"{CommandLine.Quote(exePath)} {OpenUrlSwitch} \"%1\"";
        var icon = $"{CommandLine.Quote(exePath)},0";

        using (var progId = Registry.CurrentUser.CreateSubKey(ProgIdKey))
        {
            progId.SetValue(string.Empty, "Tandem link");
            using (var defaultIcon = progId.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue(string.Empty, icon);
            }

            using (var application = progId.CreateSubKey("Application"))
            {
                application.SetValue("ApplicationName", AppPaths.ProductName);
                application.SetValue("ApplicationDescription", "Sends claude:// links to the right Claude account.");
                application.SetValue("ApplicationIcon", icon);
            }

            using var cmd = progId.CreateSubKey(@"shell\open\command");
            cmd.SetValue(string.Empty, command);
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppPaths.ProductName);
            capabilities.SetValue("ApplicationDescription", "Sends claude:// links to the right Claude account.");
            using var urls = capabilities.CreateSubKey("URLAssociations");
            urls.SetValue(ClaudeLink.Scheme, ProgId);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey))
        {
            registered.SetValue(RegisteredAppName, CapabilitiesKey);
        }

        NotifyAssociationsChanged();
        Log.Info("Sign-in helper registered (Tandem's own HKCU keys).");
    }

    /// <summary>Deletes only Tandem's own keys. Call after the user switched claude:// back to Claude.</summary>
    public static void Unregister()
    {
        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true))
        {
            registered?.DeleteValue(RegisteredAppName, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(ProgIdKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);
        using (var app = Registry.CurrentUser.OpenSubKey(AppKey))
        {
            if (app is not null && app.SubKeyCount == 0 && app.ValueCount == 0)
            {
                app.Dispose();
                Registry.CurrentUser.DeleteSubKey(AppKey, throwOnMissingSubKey: false);
            }
        }

        NotifyAssociationsChanged();
        Log.Info("Sign-in helper unregistered.");
    }

    private static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command");
        return key?.GetValue(string.Empty) as string;
    }

    private static void NotifyAssociationsChanged() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}

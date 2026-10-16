using System.ComponentModel;
using System.Diagnostics;
using Tandem.Core.Logging;
using Tandem.Core.Processes;
using Tandem.Core.Profiles;
using Tandem.Core.Safety;

namespace Tandem.Core.Claude;

public enum LaunchMethod
{
    ExecutionAlias,
    PackageActivation,
    SquirrelExecutable,
}

public sealed record LaunchResult(int? ProcessId, LaunchMethod Method, ProfileFolderStatus FolderStatus);

public sealed class ClaudeNotInstalledException()
    : Exception("The Claude desktop app is not installed for this Windows user.");

/// <summary>
/// Starts the official app, unmodified, with a profile's data folder. If that profile is
/// already running, the app's own single-instance handling brings its window to the front.
/// </summary>
public static class ClaudeLauncher
{
    public static LaunchResult Launch(Profile profile, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var install = ClaudeLocator.Locate();
        if (!install.IsInstalled)
        {
            throw new ClaudeNotInstalledException();
        }

        var folderStatus = ProfileFolderStatus.Existing;
        if (!profile.IsDefault)
        {
            folderStatus = ProfileFolder.EnsureReady(profile.DataFolder!);
        }

        var args = LaunchArguments.Build(profile, link);
        var (pid, method) = Start(install, args);
        Log.Info($"Started profile {profile.Id} via {method}{(link is null ? string.Empty : " with a link")}, " +
                 $"app version {install.Version?.ToString() ?? "unknown"}.");
        return new LaunchResult(pid, method, folderStatus);
    }

    private static (int? Pid, LaunchMethod Method) Start(ClaudeInstallation install, IReadOnlyList<string> args)
    {
        switch (install.Kind)
        {
            case ClaudeInstallKind.Msix:
                if (install.AliasPath is not null)
                {
                    try
                    {
                        return (StartExecutable(install.AliasPath, args), LaunchMethod.ExecutionAlias);
                    }
                    catch (Win32Exception ex)
                    {
                        Log.Warn("Starting through the app execution alias failed; using package activation.", ex);
                    }
                }

                return (ApplicationActivation.Activate(ClaudePackage.Aumid, CommandLine.Join(args)), LaunchMethod.PackageActivation);

            case ClaudeInstallKind.Squirrel:
                return (StartExecutable(install.ExecutablePath!, args), LaunchMethod.SquirrelExecutable);

            default:
                throw new ClaudeNotInstalledException();
        }
    }

    private static int? StartExecutable(string fileName, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        return process?.Id;
    }
}

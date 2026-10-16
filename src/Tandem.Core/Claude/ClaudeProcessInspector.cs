using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Tandem.Core.Processes;

namespace Tandem.Core.Claude;

/// <summary>A running process of the official app.</summary>
/// <param name="UserDataDir">The <c>--user-data-dir</c> it was started with; null = Default profile.</param>
public sealed record ClaudeProcessInfo(int ProcessId, DateTime StartTimeUtc, bool IsMain, string? UserDataDir);

/// <summary>
/// Recognizes processes of the official app and reads the two switches Tandem cares about.
/// Everything else in the command line is discarded immediately.
/// </summary>
public static class ClaudeProcessInspector
{
    public static ClaudeProcessInfo? Inspect(int processId)
    {
        using var process = ProcessQuery.Open(processId);
        return process is null ? null : Inspect(processId, process);
    }

    /// <summary>All main (browser) processes of the official app, one per running profile.</summary>
    public static IReadOnlyList<ClaudeProcessInfo> FindMainProcesses()
    {
        var result = new List<ClaudeProcessInfo>();
        foreach (var process in Process.GetProcessesByName("Claude"))
        {
            using (process)
            {
                if (Inspect(process.Id) is { IsMain: true } info)
                {
                    result.Add(info);
                }
            }
        }

        return result;
    }

    public static bool IsClaudeAppProcess(int processId)
    {
        using var process = ProcessQuery.Open(processId);
        return process is not null && IsClaudeApp(process);
    }

    private static ClaudeProcessInfo? Inspect(int processId, SafeProcessHandle process)
    {
        if (!IsClaudeApp(process))
        {
            return null;
        }

        var commandLine = ProcessQuery.GetCommandLine(process);
        var start = ProcessQuery.GetStartTimeUtc(process);
        if (commandLine is null || start is null)
        {
            return null;
        }

        var args = CommandLine.Split(commandLine);
        var isMain = !CommandLine.HasSwitch(args, "type");
        var userDataDir = CommandLine.GetSwitchValue(args, ClaudePackage.UserDataDirSwitch);
        return new ClaudeProcessInfo(
            processId,
            start.Value,
            isMain,
            string.IsNullOrWhiteSpace(userDataDir) ? null : userDataDir);
    }

    /// <summary>
    /// The app's own executable only: helper programs that the app starts (for example the
    /// Claude Code CLI, also named claude.exe) can share the package identity.
    /// </summary>
    private static bool IsClaudeApp(SafeProcessHandle process)
    {
        var image = ProcessQuery.GetImagePath(process);
        if (image is null)
        {
            return false;
        }

        if (ProcessQuery.GetPackageFamilyName(process) == ClaudePackage.FamilyName
            && ProcessQuery.GetPackageFullName(process) is { } fullName
            && PackageQueries.GetPackagePath(fullName) is { } installDir)
        {
            return PathUtil.AreSame(image, Path.Combine(installDir, ClaudePackage.RelativeExecutable));
        }

        return SquirrelLayout.IsAppExecutable(image, ClaudePackage.SquirrelRoot);
    }
}

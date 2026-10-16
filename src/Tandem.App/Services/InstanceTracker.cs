using Tandem.App.Native;
using Tandem.Core.Claude;
using Tandem.Core.Profiles;

namespace Tandem.App.Services;

/// <summary>A running Claude instance and the profile it belongs to (null = a folder Tandem does not manage).</summary>
internal sealed record RunningInstance(ClaudeProcessInfo Process, Profile? Profile)
{
    public int ProcessId => Process.ProcessId;
}

/// <summary>Whether an account's Claude is running, and whether it has a window.</summary>
public enum AccountRunState
{
    NotRunning,

    /// <summary>Its window was closed, but Claude keeps running in the background (tray).</summary>
    Background,

    /// <summary>It has a window (also when minimized).</summary>
    Open,
}

/// <summary>Maps running Claude processes and windows to profiles. Read-only: never touches the app.</summary>
internal sealed class InstanceTracker(ProfileStore profiles)
{
    public RunningInstance? FromProcess(int processId)
    {
        var info = ClaudeProcessInspector.Inspect(processId);
        return info is { IsMain: true } ? new RunningInstance(info, ProfileMatcher.Match(profiles.Profiles, info.UserDataDir)) : null;
    }

    public RunningInstance? FromWindow(IntPtr hwnd) => hwnd == IntPtr.Zero ? null : FromProcess(WindowTools.GetProcessId(hwnd));

    public IReadOnlyList<RunningInstance> Snapshot()
    {
        var all = profiles.Profiles;
        return ClaudeProcessInspector.FindMainProcesses()
            .Select(p => new RunningInstance(p, ProfileMatcher.Match(all, p.UserDataDir)))
            .ToList();
    }

    public RunningInstance? FindRunning(Profile profile) =>
        Snapshot().FirstOrDefault(i => i.Profile?.Id == profile.Id);

    /// <summary>The state of every running account; accounts that are missing are not running.</summary>
    public Dictionary<string, AccountRunState> States()
    {
        var result = new Dictionary<string, AccountRunState>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in Snapshot())
        {
            if (instance.Profile is { } profile)
            {
                var state = WindowTools.FindMainWindow(instance.ProcessId) != IntPtr.Zero ? AccountRunState.Open : AccountRunState.Background;
                if (!result.TryGetValue(profile.Id, out var existing) || state > existing)
                {
                    result[profile.Id] = state;
                }
            }
        }

        return result;
    }

    public AccountRunState StateOf(Profile profile) =>
        States().TryGetValue(profile.Id, out var state) ? state : AccountRunState.NotRunning;
}

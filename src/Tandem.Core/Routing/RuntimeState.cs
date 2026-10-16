using System.Text.Json;
using Tandem.Core.Logging;
using Tandem.Core.Storage;

namespace Tandem.Core.Routing;

/// <summary>An account that was just added or asked to sign in again, waiting for its sign-in link.</summary>
public sealed record PendingSignIn(string ProfileId, DateTimeOffset Expires);

/// <summary>Small runtime state shared by the tray app and the short-lived link handler process.</summary>
public sealed record RuntimeState
{
    public string? LastActiveProfileId { get; init; }

    public PendingSignIn? PendingSignIn { get; init; }

    /// <summary>When each account's window was last in front (Tandem's own bookkeeping).</summary>
    public IReadOnlyDictionary<string, DateTimeOffset>? LastUsed { get; init; }

    public DateTimeOffset? LastUsedOf(string profileId) =>
        LastUsed is not null && LastUsed.TryGetValue(profileId, out var at) ? at : null;

    public RuntimeState WithUse(string profileId, DateTimeOffset at)
    {
        var map = LastUsed is null ? new Dictionary<string, DateTimeOffset>() : new Dictionary<string, DateTimeOffset>(LastUsed);
        map[profileId] = at;
        return this with { LastActiveProfileId = profileId, LastUsed = map };
    }

    public RuntimeState Without(string profileId)
    {
        Dictionary<string, DateTimeOffset>? map = null;
        if (LastUsed is not null)
        {
            map = new Dictionary<string, DateTimeOffset>(LastUsed);
            map.Remove(profileId);
        }

        return this with
        {
            LastActiveProfileId = LastActiveProfileId == profileId ? null : LastActiveProfileId,
            PendingSignIn = PendingSignIn?.ProfileId == profileId ? null : PendingSignIn,
            LastUsed = map,
        };
    }
}

/// <summary>%LOCALAPPDATA%\Tandem\state.json, guarded by a named mutex across Tandem processes.</summary>
public sealed class StateStore(AppPaths paths)
{
    /// <summary>How long an account that was just added (or asked to sign in again) waits for its sign-in link.</summary>
    public static readonly TimeSpan SignInWindow = TimeSpan.FromMinutes(30);
    private const string MutexName = @"Local\Tandem.State";

    public RuntimeState Read() => WithLock(ReadUnlocked);

    public RuntimeState Update(Func<RuntimeState, RuntimeState> change) => WithLock(() =>
    {
        var current = ReadUnlocked();
        var next = change(current);
        if (next != current)
        {
            JsonFile.Write(paths.StateFile, next);
        }

        return next;
    });

    private RuntimeState ReadUnlocked()
    {
        if (!File.Exists(paths.StateFile))
        {
            return new RuntimeState();
        }

        try
        {
            return JsonFile.Read<RuntimeState>(paths.StateFile) ?? new RuntimeState();
        }
        catch (JsonException ex)
        {
            Log.Warn("state.json was unreadable; starting with empty runtime state.", ex);
            return new RuntimeState();
        }
    }

    private static T WithLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, MutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}

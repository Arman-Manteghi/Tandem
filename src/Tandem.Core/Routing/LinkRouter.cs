using Tandem.Core.Profiles;

namespace Tandem.Core.Routing;

public enum LinkFallback
{
    /// <summary>The account whose Claude window was used last.</summary>
    LastActive,

    /// <summary>Always the original account.</summary>
    Default,
}

public enum LinkRouteReason
{
    PendingSignIn,
    LastActive,
    Default,
}

public sealed record LinkRoute(Profile Profile, LinkRouteReason Reason);

/// <summary>Decides which account a claude:// link belongs to.</summary>
public static class LinkRouter
{
    public static LinkRoute Route(IReadOnlyList<Profile> profiles, RuntimeState state, DateTimeOffset now, LinkFallback fallback)
    {
        var defaultProfile = profiles.First(p => p.IsDefault);

        if (state.PendingSignIn is { } pending && pending.Expires > now && Find(profiles, pending.ProfileId) is { } waiting)
        {
            return new LinkRoute(waiting, LinkRouteReason.PendingSignIn);
        }

        if (fallback == LinkFallback.LastActive && Find(profiles, state.LastActiveProfileId) is { } last)
        {
            return new LinkRoute(last, last.IsDefault ? LinkRouteReason.Default : LinkRouteReason.LastActive);
        }

        return new LinkRoute(defaultProfile, LinkRouteReason.Default);
    }

    /// <summary>A pending sign-in is used once: the state after routing a link.</summary>
    public static RuntimeState AfterRouting(RuntimeState state, LinkRoute route) =>
        route.Reason == LinkRouteReason.PendingSignIn ? state with { PendingSignIn = null } : state;

    private static Profile? Find(IReadOnlyList<Profile> profiles, string? id) =>
        id is null ? null : profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}

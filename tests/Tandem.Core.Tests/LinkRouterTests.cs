using Tandem.Core.Profiles;
using Tandem.Core.Routing;

namespace Tandem.Core.Tests;

public sealed class LinkRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Profile[] Profiles =
    [
        new() { Id = Profile.DefaultId, DisplayName = "Default", Color = "#E07A5F" },
        new() { Id = "workwork", DisplayName = "Work", Color = "#3B82C4", DataFolder = @"C:\p\workwork" },
        new() { Id = "homehome", DisplayName = "Home", Color = "#2A9D8F", DataFolder = @"C:\p\homehome" },
    ];

    [Fact]
    public void Pending_sign_in_wins_over_everything()
    {
        var state = new RuntimeState { LastActiveProfileId = "homehome", PendingSignIn = new("workwork", Now.AddMinutes(5)) };

        var route = LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive);

        Assert.Equal("workwork", route.Profile.Id);
        Assert.Equal(LinkRouteReason.PendingSignIn, route.Reason);
    }

    [Fact]
    public void Expired_pending_sign_in_is_ignored()
    {
        var state = new RuntimeState { LastActiveProfileId = "homehome", PendingSignIn = new("workwork", Now.AddSeconds(-1)) };

        var route = LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive);

        Assert.Equal("homehome", route.Profile.Id);
        Assert.Equal(LinkRouteReason.LastActive, route.Reason);
    }

    [Fact]
    public void Pending_sign_in_for_a_deleted_profile_is_ignored()
    {
        var state = new RuntimeState { PendingSignIn = new("deletedx", Now.AddMinutes(5)) };

        Assert.True(LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive).Profile.IsDefault);
    }

    [Fact]
    public void Without_pending_sign_in_the_last_active_account_is_used()
    {
        var state = new RuntimeState { LastActiveProfileId = "workwork" };

        Assert.Equal("workwork", LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive).Profile.Id);
    }

    [Fact]
    public void Default_fallback_ignores_the_last_active_account()
    {
        var state = new RuntimeState { LastActiveProfileId = "workwork" };

        var route = LinkRouter.Route(Profiles, state, Now, LinkFallback.Default);

        Assert.True(route.Profile.IsDefault);
        Assert.Equal(LinkRouteReason.Default, route.Reason);
    }

    [Fact]
    public void Unknown_last_active_account_falls_back_to_default()
    {
        var state = new RuntimeState { LastActiveProfileId = "deletedx" };

        Assert.True(LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive).Profile.IsDefault);
    }

    [Fact]
    public void Pending_sign_in_is_used_only_once()
    {
        var state = new RuntimeState { LastActiveProfileId = "homehome", PendingSignIn = new("workwork", Now.AddMinutes(5)) };
        var first = LinkRouter.Route(Profiles, state, Now, LinkFallback.LastActive);

        var after = LinkRouter.AfterRouting(state, first);

        Assert.Null(after.PendingSignIn);
        Assert.Equal("homehome", after.LastActiveProfileId);
        Assert.Equal("homehome", LinkRouter.Route(Profiles, after, Now, LinkFallback.LastActive).Profile.Id);
    }
}

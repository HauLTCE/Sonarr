using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Web;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// The three tiers: bot-wide, per guild, per user (docs/09).
/// </summary>
/// <remarks>
/// <see cref="PanelGate"/> is the whole decision, extracted from the endpoints so it can be asserted
/// without a web server or a gateway connection. What the endpoints still own is gathering the three
/// facts it needs — the session, whether the caller manages <em>the guild in the route</em>, and the
/// CSRF match. The last of those is covered by <see cref="PanelCookieTests"/>; the middle one is
/// <c>GatewayGuildAuthority</c>, which needs a live gateway and so is verified against the running
/// bot rather than here.
/// <para>The case that matters most here is the negative one: a manager of guild A reaching guild B.
/// That is the bug this tier could introduce, so it is pinned first.</para>
/// </remarks>
public class PanelGateTests
{
    private const ulong GuildA = 111UL;

    private const ulong GuildB = 222UL;

    private static PanelUser User(bool isAdmin = false) =>
        new(42UL, "hash", isAdmin, DateTimeOffset.UtcNow.AddHours(1));

    // ------------------------------------------------------------------ the user tier

    [Fact]
    public void NoSessionReachesNothing()
    {
        Assert.False(PanelGate.Allows(null, PanelScope.Guild, managesGuild: true, write: false, csrfOk: true));
        Assert.False(PanelGate.Allows(null, PanelScope.BotWide, managesGuild: true, write: false, csrfOk: true));
    }

    [Fact]
    public void APlainUserReachesNoAdminRouteAtAll()
    {
        // Logged in, no tier: every admin route is closed, read or write, guild-scoped or not.
        foreach (PanelScope scope in new[] { PanelScope.Guild, PanelScope.BotWide })
        {
            Assert.False(PanelGate.Allows(User(), scope, managesGuild: false, write: false, csrfOk: true));
            Assert.False(PanelGate.Allows(User(), scope, managesGuild: false, write: true, csrfOk: true));
        }
    }

    // ------------------------------------------------------------------ the guild tier

    [Fact]
    public void AGuildManagerReachesGuildScopedRoutes()
    {
        Assert.True(PanelGate.Allows(User(), PanelScope.Guild, managesGuild: true, write: false, csrfOk: true));
        Assert.True(PanelGate.Allows(User(), PanelScope.Guild, managesGuild: true, write: true, csrfOk: true));
    }

    [Fact]
    public void AGuildManagerCannotReachAnotherGuild()
    {
        // The route names the guild, and managesGuild is resolved against *that* id. A manager of A
        // asking about B arrives here with managesGuild false, which is the whole isolation story.
        bool asksAboutOwnGuild = PanelGate.Allows(
            User(), PanelScope.Guild, managesGuild: Manages(GuildA, GuildA), write: false, csrfOk: true);

        bool asksAboutSomeoneElses = PanelGate.Allows(
            User(), PanelScope.Guild, managesGuild: Manages(GuildA, GuildB), write: false, csrfOk: true);

        Assert.True(asksAboutOwnGuild);
        Assert.False(asksAboutSomeoneElses);
    }

    [Fact]
    public void AGuildManagerCannotReachBotWideRoutes()
    {
        // The audit log spans guilds, so there is no guild id a manager could qualify against.
        // managesGuild is deliberately true here: even so, the bot-wide scope must refuse.
        Assert.False(PanelGate.Allows(User(), PanelScope.BotWide, managesGuild: true, write: false, csrfOk: true));
    }

    // ------------------------------------------------------------------ the bot tier

    [Fact]
    public void TheBotAdminReachesEverything()
    {
        // Note managesGuild is false throughout: the bot tier must not depend on the gateway
        // answering, or an admin would be locked out whenever the member cache is cold.
        Assert.True(PanelGate.Allows(User(true), PanelScope.Guild, managesGuild: false, write: false, csrfOk: true));
        Assert.True(PanelGate.Allows(User(true), PanelScope.Guild, managesGuild: false, write: true, csrfOk: true));
        Assert.True(PanelGate.Allows(User(true), PanelScope.BotWide, managesGuild: false, write: false, csrfOk: true));
        Assert.True(PanelGate.Allows(User(true), PanelScope.BotWide, managesGuild: false, write: true, csrfOk: true));
    }

    // ------------------------------------------------------------------ CSRF

    [Fact]
    public void WritesNeedCsrfAtEveryTier()
    {
        Assert.False(PanelGate.Allows(User(true), PanelScope.BotWide, managesGuild: false, write: true, csrfOk: false));
        Assert.False(PanelGate.Allows(User(true), PanelScope.Guild, managesGuild: false, write: true, csrfOk: false));
        Assert.False(PanelGate.Allows(User(), PanelScope.Guild, managesGuild: true, write: true, csrfOk: false));
    }

    [Fact]
    public void ReadsDoNotNeedCsrf()
    {
        // A GET with no header is a normal first page load, not an attack.
        Assert.True(PanelGate.Allows(User(true), PanelScope.BotWide, managesGuild: false, write: false, csrfOk: false));
        Assert.True(PanelGate.Allows(User(), PanelScope.Guild, managesGuild: true, write: false, csrfOk: false));
    }

    // ------------------------------------------------------------------ fail closed

    [Fact]
    public void AnUnknownScopeIsRefusedEvenForTheBotAdmin()
    {
        // Guards the switch's default arm: a scope added later must be closed until it is handled,
        // not silently inherit admin access.
        PanelScope invented = (PanelScope)99;

        Assert.False(PanelGate.Allows(User(true), invented, managesGuild: true, write: false, csrfOk: true));
    }

    /// <summary>
    /// Stands in for the live resolver: true only when the route's guild is the one they manage.
    /// </summary>
    private static bool Manages(ulong managedGuild, ulong routeGuild) => managedGuild == routeGuild;
}

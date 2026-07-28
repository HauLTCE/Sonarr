using Sonarr.Domain.Abstractions;

namespace Sonarr.Domain.Web;

/// <summary>
/// The three tiers the panel recognises, and the one decision that separates them.
/// </summary>
/// <remarks>
/// <para><b>Bot</b> — on <see cref="AdminAllowList"/>; reaches everything, including the routes that
/// span guilds. <b>Guild</b> — holds Manage Server in one specific guild; reaches that guild's
/// config, flags, cases and stats and nothing about any other guild. <b>User</b> — a valid session;
/// reaches only their own data, which is <c>/api/me/*</c> and never this gate at all.</para>
/// <para>This is a pure function over plain values so the rule can be tested without a web server
/// or a gateway connection. The endpoints read cookies and resolve Manage Server; the decision of
/// whether that adds up to access is made here, once, for every admin route.</para>
/// <para>Fails closed on every unknown: no session, no tier, an unresolvable guild, a missing CSRF
/// header on a write. A cache miss must never read as permission.</para>
/// </remarks>
public static class PanelGate
{
    /// <summary>
    /// Whether this request may proceed.
    /// </summary>
    /// <param name="user">The session behind the request, or null when there is none.</param>
    /// <param name="scope">
    /// <see cref="PanelScope.BotWide"/> for routes that cross guilds (the audit log), otherwise
    /// <see cref="PanelScope.Guild"/> for a route carrying a guild id.
    /// </param>
    /// <param name="managesGuild">
    /// Whether the user holds Manage Server in <em>the guild this route names</em>, resolved live
    /// for this request. Ignored when <paramref name="scope"/> is bot-wide, and must be false
    /// whenever the answer could not be determined.
    /// </param>
    /// <param name="write">True for the methods that change something, which additionally need CSRF.</param>
    /// <param name="csrfOk">Result of the double-submit cookie check.</param>
    public static bool Allows(
        PanelUser? user, PanelScope scope, bool managesGuild, bool write, bool csrfOk)
    {
        if (user is null)
        {
            return false;
        }

        // The bot tier is a superset of every guild tier, so it short-circuits both scopes. A guild
        // manager only ever satisfies a route that names a guild they manage — the guild id comes
        // from the route, so checking it here is what stops one admin reading another guild.
        bool tier = scope switch
        {
            PanelScope.BotWide => user.IsAdmin,
            PanelScope.Guild => user.IsAdmin || managesGuild,
            _ => false,
        };

        return tier && (!write || csrfOk);
    }
}

/// <summary>What a route is asking for, which decides which tier can satisfy it.</summary>
public enum PanelScope
{
    /// <summary>Spans guilds, so only the bot tier can reach it.</summary>
    BotWide,

    /// <summary>Named one guild in its route, so a manager of that guild can reach it.</summary>
    Guild,
}

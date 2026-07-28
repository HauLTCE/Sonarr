namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Answers "does this person manage that server, right now" from the live gateway.
/// </summary>
/// <remarks>
/// Deliberately not cached and deliberately not folded into the session. Discord permissions change
/// — a role gets taken away mid-session — and docs/09 already commits to admin status being decided
/// per request rather than at login. This is the same rule extended to the middle tier.
/// <para>Manage Server is the bar because that is what the slash side already enforces:
/// <c>PermissionsModule</c> carries <c>[RequireUserPermission(GuildPermission.ManageGuild)]</c>, so
/// using anything else here would let the web panel and the commands disagree about who is an admin.</para>
/// <para>Implementations must return false when they cannot tell — an uncached guild, an unknown
/// member, a gateway that is still connecting. Fail closed.</para>
/// </remarks>
public interface IGuildAuthority
{
    /// <summary>True only when this user demonstrably holds Manage Server in this guild.</summary>
    ValueTask<bool> ManagesGuildAsync(ulong userId, ulong guildId, CancellationToken ct = default);

    /// <summary>
    /// Of the guilds handed in, the ones this user manages. One call so the guild picker does not
    /// need a round trip per server.
    /// </summary>
    ValueTask<IReadOnlySet<ulong>> ManagedGuildsAsync(
        ulong userId, IReadOnlyCollection<ulong> candidates, CancellationToken ct = default);
}

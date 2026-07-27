using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Discord.Moderation;

/// <summary>
/// The Discord → domain translation for the security guard: reads permissions and role
/// positions off the gateway objects so <c>ModerationGuard</c> can decide without ever seeing
/// a Discord type (docs/02-architecture.md).
/// </summary>
public static class HierarchyReader
{
    /// <summary>
    /// Builds the snapshot for an action needing <paramref name="permission"/>.
    /// <paramref name="target"/> is <c>null</c> when the target is not a guild member (a ban by
    /// id, or someone who already left) — the guard treats that as "no role position".
    /// </summary>
    public static HierarchySnapshot Read(
        SocketGuild guild,
        SocketGuildUser actor,
        IUser? targetUser,
        SocketGuildUser? target,
        GuildPermission permission)
    {
        ArgumentNullException.ThrowIfNull(guild);
        ArgumentNullException.ThrowIfNull(actor);

        SocketGuildUser? self = guild.CurrentUser;

        return new HierarchySnapshot(
            ActorHasPermission: actor.GuildPermissions.Has(permission)
                                || actor.GuildPermissions.Administrator
                                || actor.Id == guild.OwnerId,
            BotHasPermission: self is not null
                              && (self.GuildPermissions.Has(permission) || self.GuildPermissions.Administrator),
            ActorIsOwner: actor.Id == guild.OwnerId,
            ActorTopRole: TopRole(actor),
            BotTopRole: self is null ? 0 : TopRole(self),
            TargetTopRole: target is null ? null : TopRole(target),
            TargetIsOwner: targetUser is not null && targetUser.Id == guild.OwnerId,
            TargetIsBot: targetUser is not null && self is not null && targetUser.Id == self.Id);
    }

    /// <summary>
    /// Highest role position. @everyone is position 0, so a member with no roles scores 0 and
    /// cannot outrank anybody — which is the correct answer.
    /// </summary>
    private static int TopRole(SocketGuildUser member)
        => member.Roles.Count == 0 ? 0 : member.Roles.Max(r => r.Position);
}

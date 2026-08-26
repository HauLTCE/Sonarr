using System.Globalization;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// Channel, role and member names read straight off the gateway cache. Singleton because
/// <see cref="DiscordSocketClient"/> is one.
/// </summary>
/// <remarks>
/// No REST fallback and no cache of its own. The gateway already holds channels and roles for every
/// guild it is in, so this is a dictionary walk; a name that cannot be found is not worth an HTTP
/// request, because the caller's fallback — show the id — is what it did before this type existed.
/// </remarks>
public sealed class GatewayGuildDirectory(DiscordSocketClient client) : IGuildDirectory
{
    public ValueTask<GuildDirectory?> GetAsync(ulong guildId, CancellationToken ct = default)
    {
        SocketGuild? guild = client.GetGuild(guildId);
        if (guild is null)
        {
            return ValueTask.FromResult<GuildDirectory?>(null);
        }

        // Text channels only: every ChannelId config key is somewhere she posts, so offering a voice
        // or category channel would offer a value that cannot work. Ordered as Discord orders them,
        // so the list reads like the sidebar the admin is looking at.
        List<NamedEntity> channels = [.. guild.TextChannels
            .OrderBy(c => c.Category?.Position ?? -1)
            .ThenBy(c => c.Position)
            .Select(c => new NamedEntity(Id(c.Id), c.Name))];

        // @everyone is excluded: it is not a role anyone means to hand out, and autorole_id set to it
        // is a no-op. Highest first, matching how Discord's own role list reads.
        List<NamedEntity> roles = [.. guild.Roles
            .Where(r => !r.IsEveryone)
            .OrderByDescending(r => r.Position)
            .Select(r => new NamedEntity(Id(r.Id), r.Name))];

        return ValueTask.FromResult<GuildDirectory?>(new GuildDirectory(channels, roles));
    }

    public string? DisplayName(ulong guildId, ulong userId)
    {
        if (userId == 0)
        {
            return null;
        }

        // Nickname first: in a moderation log the name an admin recognises is the one they see in
        // the member list, not the global one.
        SocketGuildUser? member = guildId == 0 ? null : client.GetGuild(guildId)?.GetUser(userId);
        if (member is not null)
        {
            return member.Nickname ?? member.DisplayName;
        }

        // Bot-wide rows and members who left: the user cache still knows most of them, because
        // they are in some other guild she shares.
        SocketUser? user = client.GetUser(userId);

        return user?.GlobalName ?? user?.Username;
    }

    public string? GuildName(ulong guildId) => guildId == 0 ? null : client.GetGuild(guildId)?.Name;

    private static string Id(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}

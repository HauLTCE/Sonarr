using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Discord.Web;

/// <summary>
/// Resolves the panel's middle tier off the live gateway (docs/09). Registered singleton because
/// <see cref="DiscordSocketClient"/> is one, and Kestrel runs in the same process as the gateway.
/// </summary>
/// <remarks>
/// The gateway is configured with <c>GuildMembers</c> and <c>AlwaysDownloadUsers = true</c>, so the
/// member cache is normally warm and this costs a dictionary lookup rather than a REST call. When it
/// is cold — the first seconds after a reconnect, or a guild that has not synced — REST is the
/// fallback for the specific member being asked about, because locking a real admin out of their own
/// panel during a reconnect is worse than one HTTP request.
/// <para>Every unknown resolves to false: no guild, no member, a gateway still connecting, a
/// transport error. That is the whole point of this type, so the try/catch is the behaviour and not
/// defensive padding.</para>
/// </remarks>
public sealed class GatewayGuildAuthority(DiscordSocketClient client, ILogger<GatewayGuildAuthority> log)
    : IGuildAuthority
{
    /// <summary>
    /// The bar for the guild tier. Matches <c>PermissionsModule</c>'s
    /// <c>[RequireUserPermission(GuildPermission.ManageGuild)]</c> so the panel and the slash
    /// commands cannot disagree about who administers a server.
    /// </summary>
    public const GuildPermission Required = GuildPermission.ManageGuild;

    public async ValueTask<bool> ManagesGuildAsync(
        ulong userId, ulong guildId, CancellationToken ct = default)
    {
        if (userId == 0 || guildId == 0)
        {
            return false;
        }

        SocketGuild? guild = client.GetGuild(guildId);
        if (guild is null)
        {
            // Not a guild she is in. Nothing to manage, and no reason to ask Discord.
            return false;
        }

        // The owner always qualifies, whatever the role list says, and this holds even when the
        // member is not cached — so it is checked before anything that can miss.
        if (guild.OwnerId == userId)
        {
            return true;
        }

        try
        {
            // The cast is needed because the two branches are different concrete types; the
            // permission check only needs the interface both implement.
            IGuildUser? member = (IGuildUser?)guild.GetUser(userId)
                ?? await client.Rest.GetGuildUserAsync(
                    guildId, userId, new RequestOptions { CancelToken = ct });

            return member?.GuildPermissions.Has(Required) == true;
        }
        catch (global::Discord.Net.HttpException ex)
        {
            // 10007 = unknown member; anything else is a transport problem. Either way: not proven.
            log.LogInformation(
                ex, "Could not resolve Manage Server for {UserId} in {GuildId}; treating as not an admin",
                userId, guildId);
            return false;
        }
    }

    public async ValueTask<IReadOnlySet<ulong>> ManagedGuildsAsync(
        ulong userId, IReadOnlyCollection<ulong> candidates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        HashSet<ulong> managed = [];

        // Sequential on purpose: the list is a handful of guilds, the cached path is synchronous,
        // and fanning out REST fallbacks would be the slow case made concurrent for no gain.
        foreach (ulong guildId in candidates)
        {
            if (await ManagesGuildAsync(userId, guildId, ct))
            {
                managed.Add(guildId);
            }
        }

        return managed;
    }
}

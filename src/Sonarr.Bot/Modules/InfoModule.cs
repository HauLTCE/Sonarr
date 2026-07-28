using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Chat;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/serverinfo</c>, <c>/roleinfo</c>, <c>/avatar</c> and the avatar user context menu
/// (docs/07-commands.md#utility). Read from the gateway cache the bot already holds — no REST
/// calls — plus the one row of server-event memory that <c>/serverinfo</c> reports.
/// </summary>
public sealed class InfoModule(IGuildStateRepository guildState)
    : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("serverinfo", "Facts about this server.")]
    public async Task ServerInfoAsync()
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("There's no server here — run this in one.");
            return;
        }

        var report = new EmbedBuilder()
            .WithTitle(guild.Name)
            .WithThumbnailUrl(guild.IconUrl)
            .WithColor(new Color(0x5865F2))
            .AddField("Created", $"<t:{guild.CreatedAt.ToUnixTimeSeconds()}:D>", inline: true)
            .AddField("Owner", guild.OwnerId == 0 ? "unknown" : MentionUtils.MentionUser(guild.OwnerId), inline: true)
            .AddField("Members", guild.MemberCount.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField("Channels", $"{guild.TextChannels.Count} text · {guild.VoiceChannels.Count} voice", inline: true)
            .AddField("Roles", guild.Roles.Count.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField("Boosts", $"{guild.PremiumSubscriptionCount} (tier {(int)guild.PremiumTier})", inline: true)
            .WithFooter($"ID {guild.Id}");

        // Server-event memory (docs/10): the sampler only writes a row when the count is beaten,
        // so this is one small read and usually one line.
        IReadOnlyList<GuildEvent> events = await guildState.GetEventsAsync((long)guild.Id);
        if (events.FirstOrDefault(e => e.Kind == GuildEvent.OnlineRecord) is { } record)
        {
            report.AddField(
                "Busiest",
                $"{record.Value.ToString(CultureInfo.InvariantCulture)} online · <t:{record.At.ToUnixTimeSeconds()}:R>",
                inline: true);
        }

        await RespondAsync(embed: report.Build());
    }

    [SlashCommand("roleinfo", "Who has a role, what it can do, and where it sits.")]
    public async Task RoleInfoAsync([Summary("role", "The role to look at")] IRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Roles live in a server — run this in one.");
            return;
        }

        if (InputGuards.BelongsToGuild(role.Guild.Id, guild.Id, "role") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        var members = guild.Users.Count(u => u.Roles.Any(r => r.Id == role.Id));

        var report = new EmbedBuilder()
            .WithTitle(role.Name)
            // IRole.Color is deprecated; Colors.PrimaryColor is the same value under the new API.
            .WithColor(role.Colors.PrimaryColor == Color.Default ? new Color(0x99AAB5) : role.Colors.PrimaryColor)
            .AddField("Members", members.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField("Position", role.Position.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField("Mentionable", role.IsMentionable ? "yes" : "no", inline: true)
            .AddField("Hoisted", role.IsHoisted ? "yes" : "no", inline: true)
            .AddField("Managed", role.IsManaged ? "yes, by an integration" : "no", inline: true)
            .AddField("Created", $"<t:{role.CreatedAt.ToUnixTimeSeconds()}:D>", inline: true)
            .AddField("Key permissions", KeyPermissions(role.Permissions))
            .WithFooter($"ID {role.Id}")
            .Build();

        await RespondAsync(embed: report);
    }

    [SlashCommand("avatar", "Show someone's avatar, full size.")]
    public Task AvatarAsync([Summary("user", "Whose avatar. Defaults to yours.")] IUser? user = null)
        => ShowAvatarAsync(user ?? Context.User);

    /// <summary>The same thing from the right-click menu — no typing, no user picker.</summary>
    [UserCommand("Avatar")]
    public Task AvatarContextAsync(IUser user) => ShowAvatarAsync(user);

    private Task ShowAvatarAsync(IUser user)
    {
        // Guild avatar first: a server-specific avatar is the one people actually see here.
        var url = (user as IGuildUser)?.GetGuildAvatarUrl(size: 1024)
                  ?? user.GetDisplayAvatarUrl(size: 1024)
                  ?? user.GetDefaultAvatarUrl();

        Embed embed = new EmbedBuilder()
            .WithTitle((user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username)
            .WithImageUrl(url)
            .WithColor(new Color(0x5865F2))
            .WithUrl(url)
            .Build();

        return RespondAsync(embed: embed);
    }

    /// <summary>
    /// The permissions worth naming. The full list is 40+ flags and nobody reads it.
    /// </summary>
    private static string KeyPermissions(GuildPermissions permissions)
    {
        if (permissions.Administrator)
        {
            return "**Administrator** — everything.";
        }

        (GuildPermission Flag, string Label)[] notable =
        [
            (GuildPermission.ManageGuild, "Manage Server"),
            (GuildPermission.ManageRoles, "Manage Roles"),
            (GuildPermission.ManageChannels, "Manage Channels"),
            (GuildPermission.BanMembers, "Ban Members"),
            (GuildPermission.KickMembers, "Kick Members"),
            (GuildPermission.ModerateMembers, "Timeout Members"),
            (GuildPermission.ManageMessages, "Manage Messages"),
            (GuildPermission.MentionEveryone, "Mention Everyone"),
            (GuildPermission.ManageWebhooks, "Manage Webhooks"),
            (GuildPermission.ManageEvents, "Manage Events"),
        ];

        var found = new StringBuilder();
        foreach ((GuildPermission flag, var label) in notable)
        {
            if (permissions.Has(flag))
            {
                found.Append(found.Length == 0 ? string.Empty : ", ").Append(label);
            }
        }

        return found.Length == 0 ? "nothing notable" : found.ToString();
    }
}

using System.Globalization;
using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// The level-up side effects: one announcement, honouring the configured channel and the DM
/// toggle, plus the role rewards earned. Shared by the message and voice XP paths.
/// </summary>
/// <remarks>
/// Everything here is best-effort. A missing channel, a closed DM or a role above Sonarr's own
/// costs a debug line, never the award that was already committed to Postgres.
/// </remarks>
internal static class LevelUpNotice
{
    public static async Task AnnounceAsync(
        SocketGuild guild,
        ulong originChannelId,
        SocketGuildUser? member,
        ulong userId,
        XpAward award,
        LevelsPolicy policy,
        ILogger log)
    {
        var level = award.Level.ToString(CultureInfo.InvariantCulture);
        var text = $"<@{userId}> reached **level {level}**."
            + (award.StreakExtended && award.StreakDays > 1
                ? $" {award.StreakDays.ToString(CultureInfo.InvariantCulture)}-day streak going."
                : string.Empty);

        ulong targetChannelId = policy.LevelupChannelId ?? originChannelId;
        if (guild.GetTextChannel(targetChannelId) is { } channel)
        {
            await SendQuietlyAsync(() => channel.SendMessageAsync(text), log, "level-up announcement").ConfigureAwait(false);
        }

        if (policy.AnnounceByDm && member is not null)
        {
            await SendQuietlyAsync(
                () => member.SendMessageAsync($"You reached **level {level}** in **{guild.Name}**."),
                log,
                "level-up DM").ConfigureAwait(false);
        }
    }

    public static async Task GrantRolesAsync(
        SocketGuild guild,
        SocketGuildUser member,
        IReadOnlyList<ulong> roleIds,
        ILogger log)
    {
        SocketGuildUser? self = guild.CurrentUser;
        if (self is null || !self.GuildPermissions.ManageRoles)
        {
            log.LogDebug("Skipping level rewards in {GuildId}: no Manage Roles", guild.Id);
            return;
        }

        var ceiling = self.Roles.Count == 0 ? 0 : self.Roles.Max(r => r.Position);

        List<SocketRole> grantable = [];
        foreach (var roleId in roleIds)
        {
            SocketRole? role = guild.GetRole(roleId);
            if (role is null || role.IsManaged || role.Position >= ceiling || member.Roles.Any(r => r.Id == roleId))
            {
                continue;
            }

            grantable.Add(role);
        }

        if (grantable.Count == 0)
        {
            return;
        }

        // One REST call for the whole set rather than one per role — this runs on every level-up.
        await SendQuietlyAsync(
            () => member.AddRolesAsync(grantable, new RequestOptions { AuditLogReason = "Level reward" }),
            log,
            "level reward roles").ConfigureAwait(false);
    }

    private static async Task SendQuietlyAsync(Func<Task> action, ILogger log, string what)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        // global:: because this file's namespace starts with Sonarr.Bot.Discord, so a plain
        // 'Discord.Net' binds to Sonarr.Bot.Discord.Net.
        catch (global::Discord.Net.HttpException ex)
        {
            log.LogDebug(ex, "Could not deliver {What}", what);
        }
    }
}

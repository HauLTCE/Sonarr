using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Modules;

/// <summary>
/// Read-only moderation lookups: <c>/case</c>, <c>/modlog</c>, <c>/userinfo</c> and the
/// "User Info" context menu (docs/07-commands.md#moderation).
/// </summary>
/// <remarks>
/// Answers are ephemeral: a member's infraction history is nobody else's business in-channel
/// (docs/07-commands.md#design-rules).
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Moderation)]
public sealed class ModLookupModule(IModerationService moderation) : ModModuleBase(moderation)
{
    [SlashCommand("case", "Look up one moderation case by number.")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task CaseAsync([Summary("id", "The case number")][MinValue(1)] long id)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        CaseRecord? record = await Moderation.GetCaseAsync(guild.Id, id);
        if (record is null)
        {
            await RespondPersonalAsync($"No case #{id.ToString(CultureInfo.InvariantCulture)} in this server.");
            return;
        }

        StringBuilder text = new();
        text.Append("**Case #").Append(record.CaseId.ToString(CultureInfo.InvariantCulture)).Append("** — ")
            .Append(record.Action.PastTense()).Append('\n');
        text.Append("Member: <@").Append(record.TargetId.ToString(CultureInfo.InvariantCulture)).Append(">\n");
        text.Append("Mod: <@").Append(record.ActorId.ToString(CultureInfo.InvariantCulture)).Append(">\n");
        text.Append("When: ").Append(DurationText.Relative(record.CreatedAt)).Append('\n');

        if (record.ExpiresAt is { } expires)
        {
            text.Append("Expires: ").Append(DurationText.Relative(expires)).Append('\n');
        }

        text.Append("Reason: ").Append(record.Reason);

        foreach (KeyValuePair<string, string> entry in record.Context)
        {
            text.Append('\n').Append(entry.Key).Append(": ").Append(entry.Value);
        }

        await RespondPersonalAsync(text.ToString());
    }

    [SlashCommand("modlog", "Recent moderation cases, newest first.")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task ModLogAsync(
        [Summary("user", "Only this member's cases")] IUser? user = null,
        [Summary("page", "Which page")][MinValue(1)] int page = 1)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        CasePage result = await Moderation.GetModLogAsync(guild.Id, user?.Id, page);

        if (result.TotalCount == 0)
        {
            await RespondPersonalAsync(user is null
                ? "No cases in this server yet. Quiet place."
                : $"No cases for {user.Username}. Clean record.");
            return;
        }

        StringBuilder text = new();
        text.Append(user is null ? "**Mod log**" : $"**Mod log — {user.Username}**");
        text.Append(" (page ").Append(result.Page.ToString(CultureInfo.InvariantCulture))
            .Append('/').Append(result.PageCount.ToString(CultureInfo.InvariantCulture))
            .Append(", ").Append(result.TotalCount.ToString(CultureInfo.InvariantCulture))
            .Append(" total)");

        foreach (CaseRecord record in result.Cases)
        {
            text.Append("\n`#").Append(record.CaseId.ToString(CultureInfo.InvariantCulture)).Append("` ")
                .Append(record.Action.PastTense()).Append(" <@")
                .Append(record.TargetId.ToString(CultureInfo.InvariantCulture)).Append("> — ")
                .Append(Trim(record.Reason)).Append(' ')
                .Append(DurationText.Relative(record.CreatedAt));
        }

        await RespondPersonalAsync(text.ToString());
    }

    [SlashCommand("userinfo", "Account details and infraction history for a member.")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task UserInfoAsync([Summary("user", "Who to look up")] IUser user)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        await RespondPersonalAsync(await BuildUserInfoAsync(guild, user));
    }

    [UserCommand("User Info")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task UserInfoContextAsync(IUser user)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        await RespondPersonalAsync(await BuildUserInfoAsync(guild, user));
    }

    private async Task<string> BuildUserInfoAsync(SocketGuild guild, IUser user)
    {
        InfractionTally tally = await Moderation.GetTallyAsync(guild.Id, user.Id);
        SocketGuildUser? member = guild.GetUser(user.Id);

        StringBuilder text = new();
        text.Append("**").Append(user.Username).Append("** `")
            .Append(user.Id.ToString(CultureInfo.InvariantCulture)).Append("`\n");
        text.Append("Account created ").Append(DurationText.Relative(user.CreatedAt)).Append('\n');

        if (member is null)
        {
            text.Append("Not in the server right now.\n");
        }
        else
        {
            if (member.JoinedAt is { } joined)
            {
                text.Append("Joined ").Append(DurationText.Relative(joined)).Append('\n');
            }

            IEnumerable<string> roles = member.Roles
                .Where(r => !r.IsEveryone)
                .OrderByDescending(r => r.Position)
                .Take(10)
                .Select(r => r.Mention);

            var roleList = string.Join(", ", roles);
            text.Append("Roles: ").Append(roleList.Length == 0 ? "none" : roleList).Append('\n');

            if (member.TimedOutUntil is { } until && until > DateTimeOffset.UtcNow)
            {
                text.Append("Timed out until ").Append(DurationText.Relative(until)).Append('\n');
            }
        }

        text.Append("History: ")
            .Append(tally.Warns.ToString(CultureInfo.InvariantCulture)).Append(" warn(s), ")
            .Append(tally.Kicks.ToString(CultureInfo.InvariantCulture)).Append(" kick(s), ")
            .Append(tally.Bans.ToString(CultureInfo.InvariantCulture)).Append(" ban(s)");

        if (tally.LastCaseAt is { } last)
        {
            text.Append(" — last ").Append(DurationText.Relative(last));
        }

        return text.ToString();
    }

    private static string Trim(string reason)
        => reason.Length <= 60 ? reason : string.Concat(reason.AsSpan(0, 57), "...");
}

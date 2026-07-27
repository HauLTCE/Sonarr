using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Application.Moderation;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Modules;

/// <summary>
/// The member-targeted moderation commands (docs/07-commands.md#moderation): warn, kick, ban,
/// unban, tempban, timeout, untimeout. Every one is case-numbered.
/// </summary>
/// <remarks>
/// Security is checked twice on purpose. <see cref="DefaultMemberPermissionsAttribute"/> plus
/// <see cref="RequireBotPermissionAttribute"/> stop the obvious cases at Discord's edge, and
/// <c>ModerationGuard</c> inside the service re-checks permission and role hierarchy against
/// live state — the module alone is not trusted (docs/checklist.md — Moderation).
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Moderation)]
public sealed class ModActionsModule(IModerationService moderation) : ModModuleBase(moderation)
{
    [SlashCommand("warn", "Warn someone. Files a numbered case.")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task WarnAsync(
        [Summary("user", "Who to warn")] SocketGuildUser user,
        [Summary("reason", "Why — pick a template or type your own")]
        [Autocomplete(typeof(WarnReasonAutocompleteHandler))] string reason)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (InputGuards.Length(reason, ModerationLimits.MaxReasonLength, "reason") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        // A warn has no Discord side effect: the case row IS the warning.
        await RunAsync(guild, user, CaseAction.Warn, reason, GuildPermission.ModerateMembers);
    }

    [SlashCommand("kick", "Kick someone out. They can rejoin with an invite.")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    [RequireBotPermission(GuildPermission.KickMembers)]
    public async Task KickAsync(
        [Summary("user", "Who to kick")] SocketGuildUser user,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        await RunAsync(
            guild, user, CaseAction.Kick, reason, GuildPermission.KickMembers,
            ct => user.KickAsync(Audit(reason)));
    }

    [SlashCommand("ban", "Ban someone permanently.")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task BanAsync(
        [Summary("user", "Who to ban")] IUser user,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        await RunAsync(
            guild, user, CaseAction.Ban, reason, GuildPermission.BanMembers,
            ct => guild.AddBanAsync(user.Id, pruneDays: 0, reason: Audit(reason)));
    }

    [SlashCommand("tempban", "Ban someone for a while. The unban survives a restart.")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task TempBanAsync(
        [Summary("user", "Who to ban")] IUser user,
        [Summary("duration", "How long — 30m, 12h, 7d, 2w")] string duration,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (!DurationText.TryParse(duration, out TimeSpan span, out var problem))
        {
            await RespondInvalidAsync(problem);
            return;
        }

        if (span < ModerationLimits.MinTempBan || span > ModerationLimits.MaxTempBan)
        {
            await RespondInvalidAsync(
                $"A tempban runs from {DurationText.Describe(ModerationLimits.MinTempBan)} to " +
                $"{DurationText.Describe(ModerationLimits.MaxTempBan)}. Longer than that, just `/ban` them.");
            return;
        }

        if (Context.User is not SocketGuildUser actor)
        {
            await RespondInvalidAsync("I couldn't work out your roles here — try again in a moment.");
            return;
        }

        HierarchySnapshot hierarchy = HierarchyReader.Read(
            guild, actor, user, guild.GetUser(user.Id), GuildPermission.BanMembers);

        ModerationOutcome outcome = await Moderation.TempBanAsync(
            new NewCase(guild.Id, user.Id, actor.Id, CaseAction.TempBan, reason ?? string.Empty),
            span,
            hierarchy,
            ct => guild.AddBanAsync(user.Id, pruneDays: 0, reason: Audit(reason)));

        if (!outcome.Allowed)
        {
            await RespondInvalidAsync(ModerationGuard.Explain(outcome.Denial));
            return;
        }

        await RespondPublicAsync(Confirm(outcome.Case!, user));
    }

    [SlashCommand("unban", "Lift a ban.")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task UnbanAsync(
        [Summary("user", "Who to unban — paste their id if they're long gone")] IUser user,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (await guild.GetBanAsync(user.Id) is null)
        {
            await RespondInvalidAsync("They aren't banned here.");
            return;
        }

        await RunAsync(
            guild, user, CaseAction.Unban, reason, GuildPermission.BanMembers,
            ct => guild.RemoveBanAsync(user.Id));
    }

    [SlashCommand("timeout", "Mute someone for a while (Discord timeout).")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task TimeoutAsync(
        [Summary("user", "Who to time out")] SocketGuildUser user,
        [Summary("duration", "How long — up to 28d")] string duration,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (!DurationText.TryParse(duration, out TimeSpan span, out var problem))
        {
            await RespondInvalidAsync(problem);
            return;
        }

        if (span < ModerationLimits.MinTimeout || span > ModerationLimits.MaxTimeout)
        {
            await RespondInvalidAsync(
                $"Discord allows timeouts from {DurationText.Describe(ModerationLimits.MinTimeout)} to " +
                $"{DurationText.Describe(ModerationLimits.MaxTimeout)}.");
            return;
        }

        // Discord owns the expiry here, so no core.job is needed — the platform lifts it.
        await RunAsync(
            guild, user, CaseAction.Timeout, reason, GuildPermission.ModerateMembers,
            ct => user.SetTimeOutAsync(span, RequestOptions(reason)),
            expiresAt: DateTimeOffset.UtcNow + span);
    }

    [SlashCommand("untimeout", "End someone's timeout early.")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task UntimeoutAsync(
        [Summary("user", "Who to release")] SocketGuildUser user,
        [Summary("reason", "Why")] string? reason = null)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (user.TimedOutUntil is null || user.TimedOutUntil <= DateTimeOffset.UtcNow)
        {
            await RespondInvalidAsync("They aren't timed out.");
            return;
        }

        await RunAsync(
            guild, user, CaseAction.Untimeout, reason, GuildPermission.ModerateMembers,
            ct => user.RemoveTimeOutAsync(RequestOptions(reason)));
    }

    /// <summary>The reason as Discord's own audit-log entry, so the platform log matches ours.</summary>
    private static string Audit(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? ModerationLimits.NoReason : reason.Trim();

    private static RequestOptions RequestOptions(string? reason)
        => new() { AuditLogReason = Audit(reason) };
}

/// <summary>
/// Reason templates for <c>/warn</c> (docs/07-commands.md: "reason autocompletes from
/// templates"). Suggestions only — a mod can type anything.
/// </summary>
public sealed class WarnReasonAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = WarnReasonTemplates
            .Matching(typed)
            .Select(t => new AutocompleteResult(t, t));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}

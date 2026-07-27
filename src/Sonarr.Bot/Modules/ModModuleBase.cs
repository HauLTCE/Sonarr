using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Application.Moderation;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Modules;

/// <summary>
/// Shared plumbing for the moderation modules: the guild check, the hierarchy snapshot, the
/// case confirmation line. Thin translator work only — every rule lives in
/// <see cref="IModerationService"/> (docs/02-architecture.md).
/// </summary>
public abstract class ModModuleBase(IModerationService moderation)
    : SonarrModuleBase<SocketInteractionContext>
{
    protected IModerationService Moderation { get; } = moderation;

    /// <summary><c>null</c> in a DM, after telling the user why.</summary>
    protected async Task<SocketGuild?> RequireGuildAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild;
        }

        await RespondInvalidAsync("Moderation happens in a server — run this in one.");
        return null;
    }

    /// <summary>
    /// Runs one member-targeted action end to end: snapshot → service → reply. The service
    /// re-checks the hierarchy, so a role change between the precondition and here is caught.
    /// </summary>
    protected async Task RunAsync(
        SocketGuild guild,
        IUser target,
        CaseAction action,
        string? reason,
        GuildPermission permission,
        Func<CancellationToken, Task>? effect = null,
        DateTimeOffset? expiresAt = null)
    {
        if (Context.User is not SocketGuildUser actor)
        {
            await RespondInvalidAsync("I couldn't work out your roles here — try again in a moment.");
            return;
        }

        HierarchySnapshot hierarchy = HierarchyReader.Read(
            guild, actor, target, guild.GetUser(target.Id), permission);

        ModerationOutcome outcome = await Moderation.ApplyAsync(
            new NewCase(guild.Id, target.Id, actor.Id, action, reason ?? string.Empty, expiresAt),
            hierarchy,
            effect);

        if (!outcome.Allowed)
        {
            await RespondInvalidAsync(ModerationGuard.Explain(outcome.Denial));
            return;
        }

        await RespondPublicAsync(Confirm(outcome.Case!, target));
    }

    /// <summary>The public confirmation: what happened, to whom, and the case number.</summary>
    protected static string Confirm(CaseRecord record, IUser target)
    {
        var line = $"**Case #{record.CaseId}** — {target.Username} {record.Action.PastTense()}.";

        if (record.ExpiresAt is { } expires)
        {
            line += $" Ends {DurationText.Relative(expires)}.";
        }

        return $"{line}\nReason: {record.Reason}";
    }
}

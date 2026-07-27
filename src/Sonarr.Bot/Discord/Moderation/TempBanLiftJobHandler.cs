using System.Globalization;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Discord.Moderation;

/// <summary>
/// Lifts a tempban when its <c>core.job</c> row comes due. The row is the authority, so a ban set
/// before a restart still lifts afterwards (docs/08-background-services.md).
/// </summary>
/// <remarks>
/// Idempotent, because <see cref="JobScheduler"/> retries: an already-unbanned user, a guild
/// Sonarr has left, and a malformed payload all complete quietly rather than looping. Only a
/// genuinely transient failure (Discord 5xx, gateway not ready) is allowed to throw so the
/// scheduler's backoff gets a turn.
/// </remarks>
public sealed class TempBanLiftJobHandler(
    DiscordSocketClient client,
    IModCaseRepository cases,
    ILogger<TempBanLiftJobHandler> log) : IJobHandler
{
    public string Kind => TempBanJob.Kind;

    public async Task HandleAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (Snowflake(job, TempBanJob.GuildIdField) is not { } guildId
            || Snowflake(job, TempBanJob.UserIdField) is not { } userId)
        {
            // Nothing to act on and nothing to retry — a bad payload will never improve.
            log.LogError("Job {JobId} has no usable tempban payload; dropping the lift", job.JobId);
            return;
        }

        SocketGuild? guild = client.GetGuild(guildId);
        if (guild is null)
        {
            log.LogWarning(
                "Tempban lift for guild {GuildId} skipped — Sonarr isn't in it any more", guildId);
            return;
        }

        if (await guild.GetBanAsync(userId, new global::Discord.RequestOptions { CancelToken = ct }) is null)
        {
            log.LogInformation(
                "Tempban lift for {UserId} in {GuildId}: already unbanned, nothing to do", userId, guildId);
            return;
        }

        await guild.RemoveBanAsync(userId, new global::Discord.RequestOptions
        {
            CancelToken = ct,
            AuditLogReason = "Tempban expired",
        });

        // The lift is a case in its own right: /modlog should show the ban ending, not just starting.
        CaseRecord record = await cases.AddAsync(
            new NewCase(
                guildId,
                userId,
                client.CurrentUser?.Id ?? 0UL,
                CaseAction.Unban,
                "Tempban expired.",
                Context: Context(job)),
            ct);

        log.LogInformation(
            "Case {CaseId}: tempban lifted for {TargetId} in {GuildId} (job {JobId})",
            record.CaseId, userId, guildId, job.JobId);
    }

    private static IReadOnlyDictionary<string, string> Context(Job job)
    {
        Dictionary<string, string> context = new(StringComparer.Ordinal) { ["source"] = TempBanJob.Kind };

        if (job.Payload[TempBanJob.CaseIdField]?.ToString() is { Length: > 0 } caseId)
        {
            context["tempban_case"] = caseId;
        }

        return context;
    }

    /// <summary>
    /// Payload ids are stored as strings (a ulong snowflake does not survive a JSON number
    /// round-trip through <c>double</c> intact).
    /// </summary>
    private static ulong? Snowflake(Job job, string field)
        => ulong.TryParse(
            job.Payload[field]?.ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value) && value != 0
            ? value
            : null;
}

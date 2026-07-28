using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Utility;

/// <summary>
/// <c>/ticket</c> (docs/07-commands.md#server-management): owns the row, the caps and the wording.
/// The private thread, who gets added to it and the transcript attachment are the caller's — those
/// are Discord's to do.
/// </summary>
/// <remarks>
/// A concrete class rather than an interface (unlike <see cref="ICapsuleService"/>): it takes
/// nothing internal, so there is no accessibility reason to add one — same as <c>ShipMeter</c>.
/// </remarks>
public sealed class TicketService(ITicketRepository tickets, ILogger<TicketService> log)
{
    /// <summary>
    /// Open tickets one person may hold per guild. Low on purpose: a ticket creates a thread and
    /// pings staff, so this is the abuse surface of the feature.
    /// </summary>
    public const int MaxOpenPerUser = 3;

    /// <summary>Matches the thread-name budget once the id prefix is accounted for.</summary>
    public const int MaxTopicLength = 80;

    /// <summary>
    /// Checks the cap and the topic before the caller creates a thread. Returns null when it is
    /// fine, or the sentence to answer with.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="RecordAsync"/> because the thread has to exist before the row can
    /// (the row stores its id), and creating a thread only to refuse the ticket would leave litter.
    /// </remarks>
    public async Task<string?> CheckAsync(
        ulong guildId, ulong openerId, string topic, CancellationToken cancellationToken = default)
    {
        string trimmed = topic?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "Tell me what it's about, even roughly.";
        }

        if (trimmed.Length > MaxTopicLength)
        {
            return $"Keep it under {MaxTopicLength} characters — details go in the thread.";
        }

        int open = await tickets
            .CountOpenByOpenerAsync((long)guildId, (long)openerId, cancellationToken)
            .ConfigureAwait(false);

        return open >= MaxOpenPerUser
            ? $"You already have {open} tickets open. Let one close first."
            : null;
    }

    /// <summary>Records a ticket for a thread that already exists.</summary>
    public async Task<TicketResult> RecordAsync(
        ulong guildId,
        ulong openerId,
        ulong threadId,
        CancellationToken cancellationToken = default)
    {
        long ticketId = await tickets
            .AddAsync(
                new Ticket
                {
                    GuildId = (long)guildId,
                    OpenerId = (long)openerId,
                    ThreadId = (long)threadId,
                    Status = TicketStatus.Open,
                },
                cancellationToken)
            .ConfigureAwait(false);

        log.LogInformation(
            "Ticket {TicketId} opened by {UserId} in thread {ThreadId}", ticketId, openerId, threadId);

        return TicketResult.Ok("Opened — the thread is yours and the mods can see it.", ticketId);
    }

    /// <summary>The open ticket for a thread, or null when the thread is not one (or is done).</summary>
    public async Task<Ticket?> GetOpenAsync(ulong threadId, CancellationToken cancellationToken = default)
        => await tickets.GetByThreadAsync((long)threadId, cancellationToken).ConfigureAwait(false)
            is { Status: TicketStatus.Open } ticket
            ? ticket
            : null;

    /// <summary>
    /// Closes the ticket, recording who closed it and where the transcript went. False when it was
    /// already closed — the caller uses that to avoid posting a second transcript.
    /// </summary>
    public async Task<bool> CloseAsync(
        long ticketId,
        ulong closedBy,
        string? transcriptRef,
        CancellationToken cancellationToken = default)
    {
        bool closed = await tickets
            .CloseAsync(ticketId, (long)closedBy, transcriptRef, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (closed)
        {
            log.LogInformation("Ticket {TicketId} closed by {UserId}", ticketId, closedBy);
        }

        return closed;
    }

    /// <summary>
    /// Thread name for a ticket. The id goes first so a moderator scanning the thread list can
    /// match it to the mod-log post without opening anything.
    /// </summary>
    public static string ThreadName(string topic)
    {
        string trimmed = topic?.Trim() ?? "ticket";

        // Discord truncates at 100; leave room and do it ourselves so the cut is not mid-word soup.
        return trimmed.Length <= MaxTopicLength ? trimmed : string.Concat(trimmed.AsSpan(0, MaxTopicLength - 1), "…");
    }
}

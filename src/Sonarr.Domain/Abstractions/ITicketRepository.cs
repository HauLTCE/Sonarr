using Sonarr.Domain.Entities.Social;

namespace Sonarr.Domain.Abstractions;

/// <summary><c>social.ticket</c> access — <c>/ticket</c> and its close button (docs/04).</summary>
public interface ITicketRepository
{
    /// <summary>Stores the ticket and returns its id.</summary>
    Task<long> AddAsync(Ticket ticket, CancellationToken ct = default);

    /// <summary>The ticket for a thread, or null when the thread is not one.</summary>
    Task<Ticket?> GetByThreadAsync(long threadId, CancellationToken ct = default);

    /// <summary>
    /// Closes the ticket and records who did it and where the transcript went. False when it was
    /// already closed, so a second click cannot post a second transcript.
    /// </summary>
    Task<bool> CloseAsync(
        long ticketId, long closedBy, string? transcriptRef, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Open tickets one person has in a guild — the per-user cap.</summary>
    Task<int> CountOpenByOpenerAsync(long guildId, long openerId, CancellationToken ct = default);
}

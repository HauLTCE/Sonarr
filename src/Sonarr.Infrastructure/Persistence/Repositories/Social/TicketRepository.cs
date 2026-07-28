using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Repositories.Social;

/// <inheritdoc cref="ITicketRepository"/>
public sealed class TicketRepository(SonarrDbContext db) : ITicketRepository
{
    public async Task<long> AddAsync(Ticket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ticket.TicketId;
    }

    public async Task<Ticket?> GetByThreadAsync(long threadId, CancellationToken ct = default)
        => await db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ThreadId == threadId, ct)
            .ConfigureAwait(false);

    public async Task<bool> CloseAsync(
        long ticketId, long closedBy, string? transcriptRef, DateTimeOffset at, CancellationToken ct = default)
        // The status test lives in the WHERE clause: two clicks close once and post one transcript.
        => await db.Tickets
            .Where(t => t.TicketId == ticketId && t.Status == TicketStatus.Open)
            .ExecuteUpdateAsync(
                t => t
                    .SetProperty(x => x.Status, TicketStatus.Closed)
                    .SetProperty(x => x.ClosedBy, closedBy)
                    .SetProperty(x => x.ClosedAt, at)
                    .SetProperty(x => x.TranscriptRef, transcriptRef),
                ct)
            .ConfigureAwait(false) > 0;

    public async Task<int> CountOpenByOpenerAsync(
        long guildId, long openerId, CancellationToken ct = default)
        => await db.Tickets
            .AsNoTracking()
            .CountAsync(
                t => t.GuildId == guildId && t.OpenerId == openerId && t.Status == TicketStatus.Open, ct)
            .ConfigureAwait(false);
}

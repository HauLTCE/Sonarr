using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Repositories.Social;

/// <inheritdoc cref="IEventRepository"/>
public sealed class EventRepository(SonarrDbContext db) : IEventRepository
{
    public async Task<long> AddAsync(SocialEvent social, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(social);

        db.Events.Add(social);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return social.EventId;
    }

    public async Task<SocialEvent?> GetAsync(long guildId, long eventId, CancellationToken ct = default)
        => await db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId && e.GuildId == guildId, ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<SocialEvent>> ListUpcomingAsync(
        long guildId, int limit, CancellationToken ct = default)
        => limit <= 0
            ? []
            : await db.Events
                .AsNoTracking()
                .Where(e => e.GuildId == guildId
                    && e.Status == SocialEventStatus.Scheduled
                    && e.StartsAt > DateTimeOffset.UtcNow)
                .OrderBy(e => e.StartsAt)
                .Take(limit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

    public Task LinkDiscordEventAsync(long eventId, long discordEventId, CancellationToken ct = default)
        => db.Events
            .Where(e => e.EventId == eventId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.DiscordEventId, discordEventId), ct);

    public async Task<bool> CancelAsync(long guildId, long eventId, CancellationToken ct = default)
        // The status test is in the WHERE clause, so two clicks produce one cancellation.
        => await db.Events
            .Where(e => e.EventId == eventId
                && e.GuildId == guildId
                && e.Status == SocialEventStatus.Scheduled)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.Status, SocialEventStatus.Cancelled), ct)
            .ConfigureAwait(false) > 0;

    public async Task<SocialEvent?> RespondAsync(
        long guildId, long eventId, long userId, string response, CancellationToken ct = default)
    {
        // Read first: an RSVP to a cancelled or foreign event must not create a row that
        // cascades off an event the caller cannot see.
        SocialEvent? social = await db.Events
            .FirstOrDefaultAsync(
                e => e.EventId == eventId
                    && e.GuildId == guildId
                    && e.Status == SocialEventStatus.Scheduled,
                ct)
            .ConfigureAwait(false);
        if (social is null)
        {
            return null;
        }

        EventRsvp? existing = await db.EventRsvps
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.EventRsvps.Add(new EventRsvp { EventId = eventId, UserId = userId, Response = response });
        }
        else
        {
            existing.Response = response;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return social;
    }

    public async Task<IReadOnlyDictionary<long, (int Going, int Maybe)>> CountResponsesAsync(
        IReadOnlyCollection<long> eventIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (eventIds.Count == 0)
        {
            return new Dictionary<long, (int, int)>();
        }

        var rows = await db.EventRsvps
            .AsNoTracking()
            .Where(r => eventIds.Contains(r.EventId) && r.Response != RsvpResponse.No)
            .GroupBy(r => new { r.EventId, r.Response })
            .Select(g => new { g.Key.EventId, g.Key.Response, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.EventId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Going: g.Where(r => r.Response == RsvpResponse.Going).Sum(r => r.Count),
                    Maybe: g.Where(r => r.Response == RsvpResponse.Maybe).Sum(r => r.Count)));
    }

    public async Task<int> CountUpcomingByCreatorAsync(
        long guildId, long creatorId, CancellationToken ct = default)
        => await db.Events
            .AsNoTracking()
            .CountAsync(
                e => e.GuildId == guildId
                    && e.CreatorId == creatorId
                    && e.Status == SocialEventStatus.Scheduled
                    && e.StartsAt > DateTimeOffset.UtcNow,
                ct)
            .ConfigureAwait(false);
}

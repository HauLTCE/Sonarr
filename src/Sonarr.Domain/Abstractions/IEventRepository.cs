using Sonarr.Domain.Entities.Social;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>social.event</c> + <c>social.event_rsvp</c> access — <c>/event create|list|cancel</c> and the
/// RSVP buttons (docs/04).
/// </summary>
public interface IEventRepository
{
    /// <summary>Stores the event and returns its id.</summary>
    Task<long> AddAsync(SocialEvent social, CancellationToken ct = default);

    /// <summary>One event by id, scoped to its guild so a guessed id from elsewhere misses.</summary>
    Task<SocialEvent?> GetAsync(long guildId, long eventId, CancellationToken ct = default);

    /// <summary>Scheduled events that have not started yet, soonest first.</summary>
    Task<IReadOnlyList<SocialEvent>> ListUpcomingAsync(long guildId, int limit, CancellationToken ct = default);

    /// <summary>Records the Discord scheduled-event id once the mirror exists.</summary>
    Task LinkDiscordEventAsync(long eventId, long discordEventId, CancellationToken ct = default);

    /// <summary>
    /// Marks the event cancelled. False when it is missing, in another guild, or already
    /// cancelled — so a double click is a no-op rather than a second announcement.
    /// </summary>
    Task<bool> CancelAsync(long guildId, long eventId, CancellationToken ct = default);

    /// <summary>Upserts one person's response. Returns the event, or null when it cannot be joined.</summary>
    Task<SocialEvent?> RespondAsync(
        long guildId, long eventId, long userId, string response, CancellationToken ct = default);

    /// <summary>Response counts per event id, for <c>/event list</c> without an N+1.</summary>
    Task<IReadOnlyDictionary<long, (int Going, int Maybe)>> CountResponsesAsync(
        IReadOnlyCollection<long> eventIds, CancellationToken ct = default);

    /// <summary>Undelivered events one person has created in a guild — the per-user cap.</summary>
    Task<int> CountUpcomingByCreatorAsync(long guildId, long creatorId, CancellationToken ct = default);
}

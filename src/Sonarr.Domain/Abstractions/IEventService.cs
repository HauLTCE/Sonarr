using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>/event create|list|cancel</c> and the RSVP buttons (docs/07-commands.md#community). Owns the
/// parsing, the caps and the wording; the caller owns everything Discord — the native scheduled
/// event and the opt-in role.
/// </summary>
/// <remarks>
/// An interface because the implementation takes <c>ZoneResolver</c>, which is internal to
/// Sonarr.Application (same reason as <see cref="ICapsuleService"/>).
/// </remarks>
public interface IEventService
{
    /// <summary>
    /// Parses <paramref name="when"/> in the creator's timezone and writes the event row. The
    /// native Discord event is the caller's follow-up, reported back with
    /// <see cref="LinkDiscordEventAsync"/>.
    /// </summary>
    Task<EventResult> CreateAsync(
        ulong guildId,
        ulong creatorId,
        string name,
        string when,
        string? description = null,
        ulong? pingRoleId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Records the mirrored Discord scheduled-event id. Best effort — a miss costs a link, not the event.</summary>
    Task LinkDiscordEventAsync(long eventId, ulong discordEventId, CancellationToken cancellationToken = default);

    /// <summary>Upcoming events in the guild, soonest first, with their response counts.</summary>
    Task<IReadOnlyList<EventView>> ListAsync(
        ulong guildId, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an event. Only the creator or a member with Manage Events may — the caller passes
    /// <paramref name="isStaff"/>, because permissions are Discord's to answer.
    /// </summary>
    Task<EventResult> CancelAsync(
        ulong guildId,
        ulong eventId,
        ulong requestedBy,
        bool isStaff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an RSVP. The outcome says whether the caller should now hold the event's ping role,
    /// which is the only part of "opt-in role pings" the service cannot do itself.
    /// </summary>
    Task<RsvpOutcome> RespondAsync(
        ulong guildId,
        ulong eventId,
        ulong userId,
        string response,
        CancellationToken cancellationToken = default);
}

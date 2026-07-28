namespace Sonarr.Domain.Utility;

/// <summary>
/// Result of <c>/event create</c>. Carries the stored id and the parsed start so the caller can
/// mirror it as a native Discord scheduled event without parsing the phrase a second time.
/// </summary>
public sealed record EventResult(bool Success, string Message, long EventId = 0, DateTimeOffset StartsAt = default)
{
    public static EventResult Ok(string message, long eventId, DateTimeOffset startsAt)
        => new(true, message, eventId, startsAt);

    public static EventResult Rejected(string? reason)
        => new(false, reason ?? "That didn't work.");
}

/// <summary>One row of <c>/event list</c>: what a member needs to decide whether to show up.</summary>
public sealed record EventView(
    long EventId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    long CreatorId,
    long? PingRoleId,
    int Going,
    int Maybe);

/// <summary>
/// Result of an RSVP button. <paramref name="PingRoleId"/> is the role the caller should now hold
/// (or lose) — the role change itself is Discord's job, not the service's.
/// </summary>
public sealed record RsvpOutcome(bool Success, string Message, long? PingRoleId = null, bool WantsRole = false);

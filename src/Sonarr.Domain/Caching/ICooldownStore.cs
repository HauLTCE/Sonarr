namespace Sonarr.Domain.Caching;

/// <summary>
/// Transient rate limiting and cooldowns (Redis area <c>rl</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Availability contract: every method here FAILS CLOSED. If the cache is unreachable the
/// implementation denies the action (returns <c>false</c> / reports "seen before"), because
/// these calls guard a security boundary (web DM-token endpoint) and abuse surfaces.
/// The only exception is the anti-spam duplicate check, which fails OPEN — see its remarks.
/// </remarks>
public interface ICooldownStore
{
    /// <summary>
    /// XP-per-message cooldown (<c>rl:xp:{guild}:{user}</c>, 60 s).
    /// Returns <c>true</c> exactly once per window: the caller may award XP.
    /// </summary>
    Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Global command flood guard (<c>rl:cmd:{user}</c>, 10 s).
    /// Returns <c>true</c> when the user is allowed to run a command now.
    /// </summary>
    Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Web DM-token endpoint limit (<c>rl:login:{identifier}</c>, max 3 per 15 min).
    /// Call once per identifier (caller checks both the IP and the target username).
    /// Returns <c>false</c> when the window is exhausted.
    /// </summary>
    Task<bool> TryConsumeLoginAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anti-spam recent-hash set (<c>rl:spam:{guild}:{user}</c>, 5 min): records
    /// <paramref name="messageHash"/> and returns how many times it has been seen in the window
    /// (1 = first time).
    /// </summary>
    /// <remarks>
    /// Fails OPEN (returns 1) when the cache is unreachable: a moderation action is the
    /// consequence of a positive result here, so an outage must not punish users.
    /// </remarks>
    Task<int> RecordMessageHashAsync(
        ulong guildId,
        ulong userId,
        string messageHash,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the recent-hash state for a user (e.g. after a moderation action).</summary>
    Task ClearMessageHashesAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
}

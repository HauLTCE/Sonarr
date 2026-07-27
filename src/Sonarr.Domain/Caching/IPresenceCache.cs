namespace Sonarr.Domain.Caching;

/// <summary>
/// Open voice session used for voice-XP accrual (<c>presence:voice:{guild}:{user}</c>).
/// Anti-AFK: time only counts while others are present and the user is unmuted.
/// </summary>
public sealed record VoicePresence(DateTimeOffset JoinedAt, ulong ChannelId, bool Alone, bool Muted);

/// <summary>
/// Presence transient state (Redis area <c>presence</c>, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Availability contract: FAILS OPEN. A lost voice session means that stretch of voice time
/// isn't credited; it is not user data loss.
/// </remarks>
public interface IPresenceCache
{
    /// <summary>Opens (or overwrites) the voice session on join.</summary>
    Task StartVoiceAsync(ulong guildId, ulong userId, VoicePresence presence, CancellationToken cancellationToken = default);

    /// <summary>Updates the accrual-relevant flags on a voice-state change, keeping <c>JoinedAt</c>.</summary>
    Task UpdateVoiceFlagsAsync(
        ulong guildId,
        ulong userId,
        bool alone,
        bool muted,
        CancellationToken cancellationToken = default);

    Task<VoicePresence?> GetVoiceAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>Closes the session on leave and returns what was there, for the accrual calculation.</summary>
    Task<VoicePresence?> EndVoiceAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>Latest online-count sample (<c>presence:online_sample:{guild}</c>, 10 min) before the batch write.</summary>
    Task SetOnlineSampleAsync(ulong guildId, int onlineCount, CancellationToken cancellationToken = default);

    Task<int?> GetOnlineSampleAsync(ulong guildId, CancellationToken cancellationToken = default);
}

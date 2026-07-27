namespace Sonarr.Domain.Levels;

/// <summary>
/// Owns the <c>presence:voice:{guild}:{user}</c> lifecycle for voice XP (docs/05-caching.md).
/// Separate from the music module's voice watcher on purpose: two independent subscribers to the
/// same gateway event, so neither area's needs bend the other's file.
/// </summary>
/// <remarks>
/// Fails open, like the presence cache underneath it: a lost session means that stretch of voice
/// time is not credited, which is not data loss. The accrual timer only credits users with an
/// open session, so "Redis down" reads as "no voice XP", never as unlimited voice XP.
/// </remarks>
public interface IVoiceSessionTracker
{
    /// <summary>Opens (or repoints, on a channel move) the session.</summary>
    Task BeginAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        int humanCount,
        bool muted,
        CancellationToken cancellationToken = default);

    /// <summary>Records a mute/occupancy change without disturbing the session anchor.</summary>
    Task UpdateAsync(
        ulong guildId,
        ulong userId,
        int humanCount,
        bool muted,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the session. <c>null</c> when there was nothing open.</summary>
    Task<VoiceSession?> EndAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    /// <summary>The open session, if any — the accrual timer's gate.</summary>
    Task<VoiceSession?> GetAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);
}

/// <summary>An open voice session, as the levels module sees it.</summary>
/// <param name="Alone">Fewer than <see cref="XpRules.VoiceMinimumHumans"/> humans in the channel.</param>
public sealed record VoiceSession(ulong ChannelId, DateTimeOffset JoinedAt, bool Alone, bool Muted);

using Sonarr.Domain.Caching;
using Sonarr.Domain.Levels;

namespace Sonarr.Application.Levels;

/// <inheritdoc cref="IVoiceSessionTracker"/>
/// <remarks>
/// A thin translation onto <see cref="IPresenceCache"/>: the levels module thinks in
/// "how many humans are in there", the cache stores the derived <c>alone</c> flag that
/// docs/05-caching.md specifies for the key.
/// </remarks>
public sealed class VoiceSessionTracker(IPresenceCache presence) : IVoiceSessionTracker
{
    public Task BeginAsync(
        ulong guildId,
        ulong userId,
        ulong channelId,
        int humanCount,
        bool muted,
        CancellationToken cancellationToken = default)
        => presence.StartVoiceAsync(
            guildId,
            userId,
            new VoicePresence(DateTimeOffset.UtcNow, channelId, IsAlone(humanCount), muted),
            cancellationToken);

    public Task UpdateAsync(
        ulong guildId,
        ulong userId,
        int humanCount,
        bool muted,
        CancellationToken cancellationToken = default)
        => presence.UpdateVoiceFlagsAsync(guildId, userId, IsAlone(humanCount), muted, cancellationToken);

    public async Task<VoiceSession?> EndAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => Map(await presence.EndVoiceAsync(guildId, userId, cancellationToken));

    public async Task<VoiceSession?> GetAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => Map(await presence.GetVoiceAsync(guildId, userId, cancellationToken));

    private static bool IsAlone(int humanCount) => humanCount < XpRules.VoiceMinimumHumans;

    private static VoiceSession? Map(VoicePresence? presence)
        => presence is null
            ? null
            : new VoiceSession(presence.ChannelId, presence.JoinedAt, presence.Alone, presence.Muted);
}

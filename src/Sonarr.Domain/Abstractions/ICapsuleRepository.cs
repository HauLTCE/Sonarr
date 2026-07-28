using Sonarr.Domain.Entities.Social;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>social.capsule</c> access — <c>/capsule write</c> (docs/04). The message lives here; when it
/// opens is a paired <c>core.job</c> row, so a restart cannot lose a delivery.
/// </summary>
public interface ICapsuleRepository
{
    /// <summary>Stores the capsule and returns its id, which the job payload then points at.</summary>
    Task<long> AddAsync(Capsule capsule, CancellationToken ct = default);

    /// <summary>One capsule by id, or null when it is gone.</summary>
    Task<Capsule?> GetAsync(long capsuleId, CancellationToken ct = default);

    /// <summary>
    /// Stamps <c>delivered_at</c>. False when the row was already stamped — the handler uses that
    /// as its idempotency check, so a job retried after a successful post cannot double-deliver.
    /// </summary>
    Task<bool> MarkDeliveredAsync(long capsuleId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Undelivered capsules one person has in flight in a guild — the per-user cap.</summary>
    Task<int> CountPendingAsync(long guildId, long authorId, CancellationToken ct = default);
}

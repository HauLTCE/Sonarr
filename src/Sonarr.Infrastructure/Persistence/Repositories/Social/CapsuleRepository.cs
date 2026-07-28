using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Repositories.Social;

/// <inheritdoc cref="ICapsuleRepository"/>
public sealed class CapsuleRepository(SonarrDbContext db) : ICapsuleRepository
{
    public async Task<long> AddAsync(Capsule capsule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capsule);

        db.Capsules.Add(capsule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return capsule.CapsuleId;
    }

    public async Task<Capsule?> GetAsync(long capsuleId, CancellationToken ct = default)
        => await db.Capsules
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CapsuleId == capsuleId, ct)
            .ConfigureAwait(false);

    /// <remarks>
    /// The <c>delivered_at is null</c> clause is the idempotency check: one UPDATE decides whether
    /// this process is the one delivering, so a job retried after a successful post updates zero
    /// rows and the handler stops instead of posting twice.
    /// </remarks>
    public async Task<bool> MarkDeliveredAsync(
        long capsuleId, DateTimeOffset at, CancellationToken ct = default)
        => await db.Capsules
            .Where(c => c.CapsuleId == capsuleId && c.DeliveredAt == null)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.DeliveredAt, at), ct)
            .ConfigureAwait(false) > 0;

    public async Task<int> CountPendingAsync(
        long guildId, long authorId, CancellationToken ct = default)
        => await db.Capsules
            .AsNoTracking()
            .CountAsync(c => c.GuildId == guildId && c.AuthorId == authorId && c.DeliveredAt == null, ct)
            .ConfigureAwait(false);
}

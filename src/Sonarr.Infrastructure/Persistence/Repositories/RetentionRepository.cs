using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRetentionRepository"/>
/// <remarks>
/// Four bulk deletes rather than one statement: they hit unrelated tables, and keeping them apart
/// means one failing table does not roll back the other three's work.
/// <para>Each is a single <c>DELETE … WHERE</c> over an indexed column (<c>expires_at</c>,
/// <c>hour_bucket</c>, <c>day</c>) — the sweep must not read rows into memory on a J2900.</para>
/// </remarks>
public sealed class RetentionRepository(SonarrDbContext db) : IRetentionRepository
{
    public async Task<RetentionSweep> SweepAsync(
        DateTimeOffset now, TimeSpan statsMaxAge, CancellationToken ct = default)
    {
        DateTimeOffset cutoff = now - statsMaxAge;

        // A stats.command_usage row is keyed by a DateOnly day, not an instant.
        DateOnly dayCutoff = DateOnly.FromDateTime(cutoff.UtcDateTime);

        return new RetentionSweep(
            // Expired login tokens are dead credentials — the sooner they are gone the better.
            await db.LoginTokens.Where(t => t.ExpiresAt <= now).ExecuteDeleteAsync(ct)
                .ConfigureAwait(false),
            await db.WebSessions.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(ct)
                .ConfigureAwait(false),
            await db.ActivitySamples.Where(a => a.HourBucket < cutoff).ExecuteDeleteAsync(ct)
                .ConfigureAwait(false),
            await db.CommandUsage.Where(c => c.Day < dayCutoff).ExecuteDeleteAsync(ct)
                .ConfigureAwait(false));
    }
}

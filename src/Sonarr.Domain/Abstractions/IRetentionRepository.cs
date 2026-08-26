namespace Sonarr.Domain.Abstractions;

/// <summary>
/// How many rows each nightly sweep deleted (docs/08 <c>RetentionPruner</c>).
/// </summary>
public sealed record RetentionSweep(int ActivitySamples, int CommandUsage)
{
    public int Total => ActivitySamples + CommandUsage;
}

/// <summary>
/// Deletes rows that have outlived their retention window (docs/06). Everything here is
/// time-bounded and derivable — nothing another table needs, which is why a sweep can be a plain
/// delete with no soft-delete dance.
/// </summary>
public interface IRetentionRepository
{
    /// <summary>
    /// Sweeps stats rows older than the cutoff. Returns per-table counts.
    /// </summary>
    /// <param name="now">Cutoff clock, so a test does not have to wait for midnight.</param>
    /// <param name="statsMaxAge">Age past which a stats row goes (docs/08: 400 days).</param>
    Task<RetentionSweep> SweepAsync(
        DateTimeOffset now, TimeSpan statsMaxAge, CancellationToken ct = default);
}

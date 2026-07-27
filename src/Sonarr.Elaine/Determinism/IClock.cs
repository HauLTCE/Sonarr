namespace Sonarr.Elaine.Determinism;

/// <summary>
/// The only way anything in the engine can learn about wall-clock time.
/// </summary>
/// <remarks>
/// The engine itself reasons in <em>logical turns</em>; wall-clock time is a set of
/// <em>signals</em> the adapter feeds in (absence tiers, daypart pools, grudge decay,
/// mood-of-the-day seed, seasonal overlay activation — see docs/10-elaine-engine.md).
/// Nothing in <c>Sonarr.Elaine</c> may call <c>DateTimeOffset.UtcNow</c>; it takes an
/// <see cref="IClock"/> through its constructor or it does not know the time.
/// A real implementation lives in the host, not here, so the engine assembly stays pure.
/// </remarks>
public interface IClock
{
    /// <summary>Current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

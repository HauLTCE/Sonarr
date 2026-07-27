namespace Sonarr.Elaine.Determinism;

/// <summary>A clock frozen at one instant. Test fixtures and replay use this.</summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

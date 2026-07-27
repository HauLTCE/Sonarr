using Sonarr.Elaine.Determinism;

namespace Sonarr.Bot.Discord.Chat;

/// <summary>
/// The real clock. Lives in the host because <c>Sonarr.Elaine</c> is not allowed to read time
/// (docs/10 determinism contract) — it ships only <c>FixedClock</c> for tests and replay.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

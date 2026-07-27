using System.Collections.Concurrent;

namespace Sonarr.Bot.Observability;

/// <summary>
/// Counters the panel renders. Deliberately not Prometheus — an 8GB box does not get a
/// metrics stack (docs/03-stack.md#observability--ops).
/// </summary>
// ponytail: in-process counters, reset on restart. If we ever want history, push these
// into stats.* on a timer rather than adding a scrape target.
public sealed class SonarrMetrics
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public void Increment(string name, long by = 1)
        => _counters.AddOrUpdate(name, by, (_, current) => current + by);

    public IReadOnlyDictionary<string, long> Snapshot()
        => _counters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}

public static class MetricNames
{
    public const string CommandsExecuted = "commands_executed";
    public const string CommandsFailed = "commands_failed";
    public const string ChatRepliesSent = "chat_replies_sent";
    public const string GatewayReconnects = "gateway_reconnects";
}

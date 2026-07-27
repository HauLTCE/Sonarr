namespace Sonarr.Domain.Abstractions;

/// <summary>One dependency's verdict: Postgres, Redis, Lavalink or Discord permissions.</summary>
/// <param name="Name">Short label used in the log line and in <c>/status</c>.</param>
/// <param name="Healthy">Green or red. No degraded state — a half-working dependency is red.</param>
/// <param name="Detail">One friendly clause: latency, row count, or what is missing. Never a secret.</param>
public sealed record SelfTestCheck(string Name, bool Healthy, string Detail);

/// <summary>The aggregate of one SelfTest run (docs/08-background-services.md).</summary>
public sealed record SelfTestReport(DateTimeOffset RanAt, IReadOnlyList<SelfTestCheck> Checks)
{
    public bool Healthy => Checks.All(c => c.Healthy);

    /// <summary>Names of the failing checks, in probe order — this is the change-detection key.</summary>
    public IReadOnlyList<string> Failing => [.. Checks.Where(c => !c.Healthy).Select(c => c.Name)];

    /// <summary>One line, ready for a log or a Discord reply.</summary>
    public string Summary => Healthy
        ? "all green: " + string.Join(", ", Checks.Select(c => c.Name))
        : "red: " + string.Join("; ", Checks.Where(c => !c.Healthy).Select(c => $"{c.Name} — {c.Detail}"));
}

/// <summary>
/// One dependency check. Implementations live in the host that owns the connection
/// (the bot process), so the aggregator stays free of transport dependencies.
/// </summary>
public interface ISelfTestProbe
{
    string Name { get; }

    /// <summary>
    /// Runs the probe. Throwing is allowed — the aggregator turns an exception into a red check —
    /// but a probe must never take longer than a few seconds.
    /// </summary>
    Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Boot + hourly health verification. Logs a green/red line only when the state changes,
/// and keeps the last report around for <c>/status</c> to render.
/// </summary>
public interface ISelfTest
{
    /// <summary>The most recent report, or <c>null</c> before the first run completes.</summary>
    SelfTestReport? Last { get; }

    Task<SelfTestReport> RunAsync(CancellationToken cancellationToken = default);
}

using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Health;

/// <summary>
/// Runs every registered <see cref="ISelfTestProbe"/> and aggregates the verdicts
/// (docs/08-background-services.md — SelfTest, boot + hourly).
/// </summary>
/// <remarks>
/// Logging is change-triggered on purpose: hourly "all green" lines train people to ignore
/// the log, so a line is only written when the set of failing checks differs from last run.
/// The aggregator holds no transport dependency — the probes do (docs/02-architecture.md).
/// </remarks>
public sealed class SelfTest(IEnumerable<ISelfTestProbe> probes, ILogger<SelfTest> log) : ISelfTest
{
    private readonly ISelfTestProbe[] _probes = [.. probes];

    /// <summary>Failing-check names from the previous run; <c>null</c> until the first run.</summary>
    private IReadOnlyList<string>? _previousFailing;

    public SelfTestReport? Last { get; private set; }

    public async Task<SelfTestReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<SelfTestCheck>(_probes.Length);
        foreach (var probe in _probes)
        {
            checks.Add(await RunProbeAsync(probe, cancellationToken).ConfigureAwait(false));
        }

        var report = new SelfTestReport(DateTimeOffset.UtcNow, checks);
        Last = report;
        ReportIfChanged(report);
        return report;
    }

    private static async Task<SelfTestCheck> RunProbeAsync(ISelfTestProbe probe, CancellationToken ct)
    {
        try
        {
            return await probe.RunAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A probe that throws is a red check, not a dead SelfTest. Message only:
            // connection strings and passwords can appear in provider exception text.
            return new SelfTestCheck(probe.Name, false, ex.Message);
        }
    }

    private void ReportIfChanged(SelfTestReport report)
    {
        var failing = report.Failing;
        if (_previousFailing is not null && failing.SequenceEqual(_previousFailing, StringComparer.Ordinal))
        {
            log.LogDebug("Self-test unchanged ({Summary})", report.Summary);
            return;
        }

        _previousFailing = failing;

        if (report.Healthy)
        {
            log.LogInformation("Self-test {State}: {Summary}", "GREEN", report.Summary);
        }
        else
        {
            log.LogError("Self-test {State}: {Summary}", "RED", report.Summary);
        }
    }
}

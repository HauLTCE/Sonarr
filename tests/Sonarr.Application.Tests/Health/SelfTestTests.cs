using Microsoft.Extensions.Logging;
using Sonarr.Application.Health;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Tests.Health;

/// <summary>
/// docs/08-background-services.md: SelfTest aggregates the probes and posts one green/red
/// line <i>on change</i> — an hourly all-green line is noise people learn to ignore.
/// </summary>
public sealed class SelfTestTests
{
    [Fact]
    public async Task RunAsync_is_green_when_every_probe_is_green()
    {
        var log = new RecordingLogger();
        var selfTest = new SelfTest([Green("Postgres"), Green("Redis")], log);

        var report = await selfTest.RunAsync();

        Assert.True(report.Healthy);
        Assert.Empty(report.Failing);
        Assert.Contains("all green", report.Summary);
    }

    [Fact]
    public async Task RunAsync_is_red_when_one_probe_is_red()
    {
        var log = new RecordingLogger();
        var selfTest = new SelfTest([Green("Postgres"), Red("Lavalink", "HTTP 503")], log);

        var report = await selfTest.RunAsync();

        Assert.False(report.Healthy);
        Assert.Equal(["Lavalink"], report.Failing);
        Assert.Contains("Lavalink — HTTP 503", report.Summary);
    }

    [Fact]
    public async Task RunAsync_turns_a_throwing_probe_into_a_red_check()
    {
        var log = new RecordingLogger();
        var selfTest = new SelfTest([new ThrowingProbe("Redis", "no route to host")], log);

        var report = await selfTest.RunAsync();

        Assert.False(report.Healthy);
        Assert.Equal(["Redis"], report.Failing);
        Assert.Contains("no route to host", report.Summary);
    }

    [Fact]
    public async Task RunAsync_stores_the_last_report_for_status_to_read()
    {
        var log = new RecordingLogger();
        var selfTest = new SelfTest([Green("Redis")], log);

        Assert.Null(selfTest.Last);
        var report = await selfTest.RunAsync();

        Assert.Same(report, selfTest.Last);
    }

    [Fact]
    public async Task RunAsync_reports_once_while_the_state_is_unchanged()
    {
        var log = new RecordingLogger();
        var selfTest = new SelfTest([Green("Redis")], log);

        await selfTest.RunAsync();
        await selfTest.RunAsync();
        await selfTest.RunAsync();

        Assert.Equal(1, log.Count(LogLevel.Information));
        Assert.Equal(0, log.Count(LogLevel.Error));
    }

    [Fact]
    public async Task RunAsync_reports_again_when_a_check_flips_red()
    {
        var log = new RecordingLogger();
        var probe = new SwitchableProbe("Lavalink");
        var selfTest = new SelfTest([probe], log);

        await selfTest.RunAsync();
        probe.Healthy = false;
        await selfTest.RunAsync();

        Assert.Equal(1, log.Count(LogLevel.Information));
        Assert.Equal(1, log.Count(LogLevel.Error));
    }

    [Fact]
    public async Task RunAsync_reports_recovery_when_a_check_goes_green_again()
    {
        var log = new RecordingLogger();
        var probe = new SwitchableProbe("Lavalink") { Healthy = false };
        var selfTest = new SelfTest([probe], log);

        await selfTest.RunAsync();
        await selfTest.RunAsync();
        probe.Healthy = true;
        await selfTest.RunAsync();

        Assert.Equal(1, log.Count(LogLevel.Error));
        Assert.Equal(1, log.Count(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_reports_again_when_a_different_check_fails()
    {
        var log = new RecordingLogger();
        var postgres = new SwitchableProbe("Postgres") { Healthy = false };
        var redis = new SwitchableProbe("Redis");
        var selfTest = new SelfTest([postgres, redis], log);

        await selfTest.RunAsync();
        postgres.Healthy = true;
        redis.Healthy = false;
        await selfTest.RunAsync();

        // Still red both times, but a different dependency — that is a state change worth a line.
        Assert.Equal(2, log.Count(LogLevel.Error));
    }

    private static ISelfTestProbe Green(string name) => new StubProbe(name, true, "fine");

    private static ISelfTestProbe Red(string name, string detail) => new StubProbe(name, false, detail);

    private sealed class StubProbe(string name, bool healthy, string detail) : ISelfTestProbe
    {
        public string Name => name;

        public Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SelfTestCheck(name, healthy, detail));
    }

    private sealed class SwitchableProbe(string name) : ISelfTestProbe
    {
        public string Name => name;

        public bool Healthy { get; set; } = true;

        public Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SelfTestCheck(name, Healthy, Healthy ? "fine" : "down"));
    }

    private sealed class ThrowingProbe(string name, string message) : ISelfTestProbe
    {
        public string Name => name;

        public Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(message);
    }

    /// <summary>Hand-rolled recorder — Moq is not a dependency of this solution.</summary>
    private sealed class RecordingLogger : ILogger<SelfTest>
    {
        private readonly List<LogLevel> _levels = [];

        public int Count(LogLevel level) => _levels.Count(l => l == level);

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

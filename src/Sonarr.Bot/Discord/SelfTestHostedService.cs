using System.Diagnostics;
using System.Net.Http.Headers;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Sonarr.Bot.Discord;

/// <summary>
/// Runs the self-test on boot (once the gateway is up) and then hourly
/// (docs/08-background-services.md). The green/red line is emitted by
/// <see cref="ISelfTest"/> only when the state changes.
/// </summary>
public sealed class SelfTestHostedService(
    ISelfTest selfTest,
    DiscordSocketClient client,
    ILogger<SelfTestHostedService> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReadyWait = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Boot run waits for Ready, otherwise the Discord-permissions probe would report
        // red for the few seconds the gateway takes to connect.
        await WaitForReadyAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await selfTest.RunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let the loop die: the next tick is a fresh chance.
                log.LogError(ex, "Self-test run failed outright");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task WaitForReadyAsync(CancellationToken stoppingToken)
    {
        if (client.ConnectionState == global::Discord.ConnectionState.Connected)
        {
            return;
        }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnReady()
        {
            ready.TrySetResult();
            return Task.CompletedTask;
        }

        client.Ready += OnReady;
        try
        {
            // ponytail: bounded wait, then probe anyway — a gateway that never connects
            // should be reported red rather than silently skipping every self-test.
            using var timeout = new CancellationTokenSource(ReadyWait);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
            await ready.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            log.LogWarning("Gateway not ready after {Seconds}s; running the self-test anyway", ReadyWait.TotalSeconds);
        }
        finally
        {
            client.Ready -= OnReady;
        }
    }
}

/// <summary>Postgres: can we connect and actually read a row (docs/04-database.md).</summary>
public sealed class PostgresProbe(IServiceScopeFactory scopes) : ISelfTestProbe
{
    public string Name => "Postgres";

    public async Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();

        var sw = Stopwatch.StartNew();
        // CanConnect alone passes against a database with no schema; counting a real table
        // proves migrations ran too.
        var guilds = await db.Guilds.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
        return new SelfTestCheck(Name, true, $"{guilds} guild row(s), {sw.ElapsedMilliseconds} ms");
    }
}

/// <summary>Redis: a plain PING against the shared multiplexer (docs/05-caching.md).</summary>
public sealed class RedisProbe(IConnectionMultiplexer multiplexer) : ISelfTestProbe
{
    public string Name => "Redis";

    public async Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
    {
        var latency = await multiplexer.GetDatabase().PingAsync().ConfigureAwait(false);
        return new SelfTestCheck(Name, true, $"ping {latency.TotalMilliseconds:F0} ms");
    }
}

/// <summary>
/// Lavalink: authenticated GET on the REST API. Out-of-process and not ours to restart,
/// so "is it there and does it accept our password" is exactly what we need to know.
/// </summary>
public sealed class LavalinkProbe(SonarrOptions options) : ISelfTestProbe, IDisposable
{
    // ponytail: one long-lived HttpClient in the probe instead of IHttpClientFactory —
    // a single fixed endpoint hit once an hour has no DNS-rotation problem to solve.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public string Name => "Lavalink";

    public async Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
    {
        var uri = new Uri(new Uri(options.LavalinkUri), "/v4/info");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // Lavalink wants the raw password as the Authorization value, no scheme. Added without
        // validation on purpose: the typed header would throw FormatException on a password
        // containing a space, and that exception message echoes the password into the log
        // (docs/06-data-and-privacy.md — secrets never reach a sink).
        request.Headers.TryAddWithoutValidation("Authorization", options.LavalinkPassword);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return new SelfTestCheck(Name, true, $"{uri.Host}:{uri.Port} responding");
        }

        var detail = (int)response.StatusCode == 401
            ? "rejected our credentials (check LAVALINK_PASSWORD)"
            : $"HTTP {(int)response.StatusCode}";
        return new SelfTestCheck(Name, false, detail);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Discord: the permissions each feature needs, per guild, reusing the same table
/// <c>/checkperms</c> renders (<see cref="FeaturePermissions"/>).
/// </summary>
public sealed class DiscordPermissionsProbe(DiscordSocketClient client) : ISelfTestProbe
{
    public string Name => "Discord";

    public Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
    {
        if (client.ConnectionState != global::Discord.ConnectionState.Connected)
        {
            return Task.FromResult(new SelfTestCheck(Name, false, $"gateway {client.ConnectionState}"));
        }

        var broken = new List<string>();
        foreach (var guild in client.Guilds)
        {
            var me = guild.CurrentUser;
            if (me is null)
            {
                continue;
            }

            var features = FeaturePermissions.All
                .Where(r => r.Permissions.Any(p => !me.GuildPermissions.Has(p)))
                .Select(r => r.Feature)
                .ToArray();

            if (features.Length > 0)
            {
                broken.Add($"{guild.Name}: {string.Join(", ", features)}");
            }
        }

        // Missing permissions are red: the feature will silently do nothing otherwise,
        // which is the exact failure this check exists to prevent.
        return Task.FromResult(broken.Count == 0
            ? new SelfTestCheck(Name, true, $"{client.Guilds.Count} guild(s), all perms present ({client.Latency} ms)")
            : new SelfTestCheck(Name, false, "missing perms — " + string.Join(" | ", broken)));
    }
}

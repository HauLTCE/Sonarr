using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Jobs;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// The nightly retention sweep (docs/08, 04:00): the <c>chat.episode</c> cap, dead login tokens
/// and web sessions, and stats past their window.
/// </summary>
/// <remarks>
/// <para>A plain daily timer rather than a <c>core.job</c> row, unlike everything the
/// <see cref="JobScheduler"/> runs. Durability is what a job row buys, and a missed sweep costs
/// nothing — the next one deletes the same rows plus a day's worth, because every cutoff is
/// computed from "now" rather than from when the last run happened.</para>
/// <para>04:00 container-local (UTC in production), which is the whole point of naming an hour: the
/// deletes take locks on tables the chat path reads, and nobody is talking to her then.</para>
/// <para>Redis needs no sweep — every key it writes carries a TTL, so an orphan expires on its own.
/// docs/08's "expired Redis orphans" is handled by construction, not by this service.</para>
/// </remarks>
public sealed class RetentionPruner(IServiceScopeFactory scopes, ILogger<RetentionPruner> log)
    : BackgroundService
{
    /// <summary>docs/08: 04:00.</summary>
    public static readonly TimeOnly RunAt = new(4, 0);

    /// <summary>
    /// Episodes kept per person (docs/04 default). Everything older goes unless a fact points at
    /// it — a memory she will never recall is disk, and pgvector search cost grows with the table.
    /// </summary>
    public const int KeepEpisodesPerUser = 200;

    /// <summary>docs/08: stats older than this go.</summary>
    public static readonly TimeSpan StatsMaxAge = TimeSpan.FromDays(400);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("RetentionPruner sweeping daily at {RunAt}", RunAt);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UntilNextRun(DateTimeOffset.Now), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await SweepAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>How long until the next <see cref="RunAt"/>. See <see cref="DailySchedule"/>.</summary>
    public static TimeSpan UntilNextRun(DateTimeOffset now) => DailySchedule.UntilNextRun(now, RunAt);

    /// <summary>
    /// One sweep. Public so a test can drive it without waiting for 04:00, same as
    /// <see cref="JobScheduler.PollOnceAsync"/>.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();

        // Two try blocks, not one: the episode cap is the half that touches her memory, and a
        // failing stats delete must not skip it (or the reverse).
        try
        {
            int episodes = await scope.ServiceProvider.GetRequiredService<IEpisodeRepository>()
                .PruneAsync(KeepEpisodesPerUser, ct).ConfigureAwait(false);

            log.LogInformation("Retention: pruned {Episodes} episode(s).", episodes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Episode retention failed; retrying tomorrow.");
        }

        try
        {
            RetentionSweep swept = await scope.ServiceProvider
                .GetRequiredService<IRetentionRepository>()
                .SweepAsync(DateTimeOffset.UtcNow, StatsMaxAge, ct).ConfigureAwait(false);

            log.LogInformation(
                "Retention: swept {Total} row(s) — {Tokens} token(s), {Sessions} session(s), "
                + "{Samples} sample(s), {Usage} usage row(s).",
                swept.Total, swept.LoginTokens, swept.WebSessions, swept.ActivitySamples,
                swept.CommandUsage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Retention sweep failed; retrying tomorrow.");
        }
    }
}

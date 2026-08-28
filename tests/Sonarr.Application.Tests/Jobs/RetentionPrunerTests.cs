using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Bot.Discord.Jobs;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Application.Tests.Jobs;

/// <summary>
/// The episode half of the sweep. Only <c>PruneAsync</c> is exercised — the cap itself is SQL, and
/// there is no database in this suite.
/// </summary>
internal sealed class PrunerEpisodeRepository : IEpisodeRepository
{
    public List<int> Pruned { get; } = [];

    public Exception? Throws { get; set; }

    public int Returns { get; set; }

    public Task<int> PruneAsync(int keepPerUser, CancellationToken ct = default)
    {
        Pruned.Add(keepPerUser);
        return Throws is null ? Task.FromResult(Returns) : Task.FromException<int>(Throws);
    }

    // The pruner touches nothing else on this interface.
    public Task<IReadOnlyList<EpisodeMatch>> SearchAsync(
        long guildId, long userId, float[] query, int limit, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Episode>> GetUnembeddedAsync(int limit, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Episode>> GetRecentAsync(
        long guildId, long userId, DateTimeOffset since, int limit, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Episode>> GetRecentAsync(
        long guildId, DateTimeOffset since, int limit, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task SetEmbeddingsAsync(
        IReadOnlyList<(long Id, float[] Vector)> embeddings, CancellationToken ct = default)
        => throw new NotSupportedException();
}

internal sealed class PrunerRetentionRepository : IRetentionRepository
{
    public List<(DateTimeOffset Now, TimeSpan MaxAge)> Sweeps { get; } = [];

    public Exception? Throws { get; set; }

    public RetentionSweep Returns { get; set; } = new(0, 0);

    public Task<RetentionSweep> SweepAsync(
        DateTimeOffset now, TimeSpan statsMaxAge, CancellationToken ct = default)
    {
        Sweeps.Add((now, statsMaxAge));
        return Throws is null
            ? Task.FromResult(Returns)
            : Task.FromException<RetentionSweep>(Throws);
    }
}

public class RetentionPrunerTests
{
    private static (RetentionPruner Pruner, PrunerEpisodeRepository Episodes, PrunerRetentionRepository Rows) Build()
    {
        PrunerEpisodeRepository episodes = new();
        PrunerRetentionRepository rows = new();

        ServiceCollection services = new();
        services.AddSingleton<IEpisodeRepository>(episodes);
        services.AddSingleton<IRetentionRepository>(rows);

        RetentionPruner pruner = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RetentionPruner>.Instance);

        return (pruner, episodes, rows);
    }

    [Fact]
    public async Task A_sweep_caps_episodes_and_clears_dead_rows()
    {
        var (pruner, episodes, rows) = Build();

        await pruner.SweepAsync(CancellationToken.None);

        Assert.Equal([RetentionPruner.KeepEpisodesPerUser], episodes.Pruned);
        Assert.Equal(RetentionPruner.StatsMaxAge, Assert.Single(rows.Sweeps).MaxAge);
    }

    /// <summary>
    /// The two halves are independent on purpose: a broken stats delete must not cost her the
    /// episode cap, which is the half that keeps the vector table searchable.
    /// </summary>
    [Fact]
    public async Task A_failing_half_does_not_skip_the_other()
    {
        var (pruner, episodes, rows) = Build();
        episodes.Throws = new InvalidOperationException("no");

        await pruner.SweepAsync(CancellationToken.None);

        Assert.Single(rows.Sweeps);

        var (second, alsoEpisodes, alsoRows) = Build();
        alsoRows.Throws = new InvalidOperationException("no");

        await second.SweepAsync(CancellationToken.None);

        Assert.Single(alsoEpisodes.Pruned);
    }

    [Fact]
    public void The_next_run_is_the_next_four_am()
    {
        DateTimeOffset lateNight = new(2026, 7, 28, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(150), RetentionPruner.UntilNextRun(lateNight));
    }

    /// <summary>Otherwise a sweep finishing inside the same minute would sweep again immediately.</summary>
    [Fact]
    public void Four_am_itself_waits_a_whole_day()
    {
        DateTimeOffset onTheDot = new(2026, 7, 28, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromDays(1), RetentionPruner.UntilNextRun(onTheDot));
    }

    [Fact]
    public void The_wait_is_never_negative()
    {
        DateTimeOffset now = new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

        for (int minutes = 0; minutes < 24 * 60; minutes += 7)
        {
            Assert.InRange(
                RetentionPruner.UntilNextRun(now.AddMinutes(minutes)),
                TimeSpan.Zero,
                TimeSpan.FromDays(1));
        }
    }

    [Fact]
    public void The_total_is_every_table()
        => Assert.Equal(3, new RetentionSweep(1, 2).Total);
}

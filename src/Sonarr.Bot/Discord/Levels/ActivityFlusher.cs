using System.Collections.Concurrent;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// Per-member message counts and last-active stamps, batched in memory between flushes
/// (docs/08-background-services.md — "ActivityFlusher: 60 s"). One row per member per window
/// instead of one UPDATE per message, which is the difference between the J2900 keeping up and not.
/// </summary>
/// <remarks>
/// Deliberately lossy by design: a restart drops at most the current window (a handful of message
/// counts). Postgres stays the authority for everything durable, and nothing here feeds XP — XP is
/// written synchronously by <see cref="Sonarr.Domain.Abstractions.ILevelService"/>.
/// </remarks>
public sealed class ActivityBuffer
{
    private readonly ConcurrentDictionary<(long GuildId, long UserId), Pending> pending = new();

    /// <summary>Counts one message. Lock-free and allocation-free on the hot path after the first hit.</summary>
    public void Record(ulong guildId, ulong userId)
    {
        var key = ((long)guildId, (long)userId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        pending.AddOrUpdate(key, _ => new Pending(1, now), (_, existing) => existing.Add(now));
    }

    /// <summary>
    /// Hands the current window to the caller and clears it. The caller owns the deltas from here:
    /// if the write fails they are gone, which is the accepted trade for never double-counting.
    /// </summary>
    public IReadOnlyCollection<MemberActivityDelta> Drain()
    {
        if (pending.IsEmpty)
        {
            return [];
        }

        List<MemberActivityDelta> drained = new(pending.Count);
        foreach (var key in pending.Keys)
        {
            if (pending.TryRemove(key, out Pending value))
            {
                drained.Add(new MemberActivityDelta(key.GuildId, key.UserId, value.Count, value.LastActiveAt));
            }
        }

        return drained;
    }

    private readonly record struct Pending(long Count, DateTimeOffset LastActiveAt)
    {
        public Pending Add(DateTimeOffset at)
            => new(Count + 1, at > LastActiveAt ? at : LastActiveAt);
    }
}

/// <summary>
/// Flushes <see cref="ActivityBuffer"/> into <c>core.member</c> every 60 s.
/// </summary>
public sealed class ActivityFlusher(
    ActivityBuffer buffer,
    IServiceScopeFactory scopes,
    ILogger<ActivityFlusher> log) : BackgroundService
{
    /// <summary>docs/08: 60 s window.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("ActivityFlusher flushing every {Seconds}s", FlushInterval.TotalSeconds);

        using PeriodicTimer timer = new(FlushInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await FlushAsync(stoppingToken).ConfigureAwait(false);
        }

        // Last window on a clean shutdown. CancellationToken.None: the token that stopped the loop
        // is already cancelled, and this write is short.
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>One flush pass. Internal so a test can drive it without the timer.</summary>
    internal async Task FlushAsync(CancellationToken ct)
    {
        try
        {
            IReadOnlyCollection<MemberActivityDelta> deltas = buffer.Drain();
            if (deltas.Count == 0)
            {
                return;
            }

            using IServiceScope scope = scopes.CreateScope();
            var members = scope.ServiceProvider.GetRequiredService<IMemberRepository>();

            await members.ApplyActivityAsync(deltas, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ponytail: a failed flush loses that window's message counts (XP is unaffected).
            // Upgrade path: a Redis hash keyed from RedisKeys (needs a new entry + CacheTtl there)
            // so the counters survive both a failed write and a restart.
            log.LogError(ex, "Activity flush failed; that window's message counts are lost");
        }
    }
}

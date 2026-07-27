using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;

// The web SDK's implicit global usings put Microsoft.AspNetCore.Http.IResult in scope, which
// collides with Discord's. Alias rather than fully qualify at every handler signature.
using IInteractionResult = Discord.Interactions.IResult;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// Counts slash command executions into <c>stats.command_usage</c> (docs/08-background-services.md).
/// Command names only — never arguments, never message content (docs/06-data-and-privacy.md).
/// </summary>
/// <remarks>
/// <para>
/// Counts are buffered in memory and flushed every <see cref="FlushInterval"/>, so a busy minute
/// costs one upsert per (guild, command) instead of one per invocation. That matters on a Pentium
/// J2900, and it keeps the interaction path free of a database round trip.
/// </para>
/// <para>
/// The buffer is drained on shutdown too, so a clean stop loses nothing.
/// </para>
/// </remarks>
public sealed class CommandUsageCounter(
    InteractionService interactions,
    IServiceScopeFactory scopes,
    ILogger<CommandUsageCounter> log) : BackgroundService
{
    /// <summary>Long enough to batch a burst, short enough that a crash loses very little.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    // ponytail: an in-memory buffer means an unclean kill (SIGKILL, power loss) drops up to a
    // minute of counts. Ceiling: private analytics only, nothing user-visible depends on it.
    // Upgrade path: a Redis counter hash flushed by the same loop, if the numbers ever matter.
    private readonly ConcurrentDictionary<(long GuildId, string Command, DateOnly Day), long> _pending = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        interactions.SlashCommandExecuted += OnExecutedAsync;
        interactions.ContextCommandExecuted += OnContextExecutedAsync;
        log.LogInformation("CommandUsageCounter flushing every {Seconds}s", FlushInterval.TotalSeconds);

        try
        {
            using PeriodicTimer timer = new(FlushInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: fall through to the final flush below.
        }
        finally
        {
            interactions.SlashCommandExecuted -= OnExecutedAsync;
            interactions.ContextCommandExecuted -= OnContextExecutedAsync;
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task OnExecutedAsync(SlashCommandInfo command, IInteractionContext context, IInteractionResult result)
    {
        Record(context, command.Module.SlashGroupName is { Length: > 0 } group
            ? $"{group} {command.Name}"
            : command.Name);
        return Task.CompletedTask;
    }

    private Task OnContextExecutedAsync(ContextCommandInfo command, IInteractionContext context, IInteractionResult result)
    {
        Record(context, command.Name);
        return Task.CompletedTask;
    }

    private void Record(IInteractionContext context, string command)
    {
        // DMs have no guild to attribute the count to, and the schema keys on guild_id.
        if (context.Guild is null || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        _pending.AddOrUpdate(((long)context.Guild.Id, command, day), 1, static (_, count) => count + 1);
    }

    /// <summary>Drains the buffer into stats. Internal so a test can flush without the timer.</summary>
    internal async Task FlushAsync(CancellationToken ct)
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var stats = scope.ServiceProvider.GetRequiredService<IStatsRepository>();

            foreach ((long GuildId, string Command, DateOnly Day) key in _pending.Keys)
            {
                if (!_pending.TryRemove(key, out var delta) || delta == 0)
                {
                    continue;
                }

                try
                {
                    await stats.IncrementCommandAsync(key.GuildId, key.Command, key.Day, delta, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Put it back so the next flush retries rather than silently losing the count.
                    _pending.AddOrUpdate(key, delta, (_, current) => current + delta);
                    log.LogWarning(ex, "Command usage flush failed for {Command}", key.Command);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Command usage flush failed");
        }
    }
}

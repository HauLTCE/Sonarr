using Sonarr.Application.Chat;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Discord.Chat;

/// <summary>
/// Warms the semantic index at boot, then re-warms after a persona hot-reload.
/// </summary>
/// <remarks>
/// Off the boot path on purpose: embedding is CPU work on a J2900 and the lexical tier answers
/// perfectly well while it runs, so a slow first warm costs nothing but a few minutes of
/// lexical-only matching. Every failure inside <see cref="SemanticIntentIndex.WarmAsync"/> is
/// already swallowed there, so this loop only has to survive the ones that escape it.
/// </remarks>
public sealed class SemanticWarmup(
    SemanticIntentIndex index,
    IServiceScopeFactory scopes,
    ILogger<SemanticWarmup> log) : BackgroundService
{
    /// <summary>How often we look for a persona edit that added or changed examples.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ponytail: polling is enough — WarmAsync hashes the authored examples and returns
        // immediately when nothing changed, so a 30s tick costs microseconds. Swap for a reload
        // event on PersonaHolder if anything else ever needs to react to an edit.
        while (!stoppingToken.IsCancellationRequested)
        {
            await WarmAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            await index.WarmAsync(
                scope.ServiceProvider.GetRequiredService<IIntentEmbeddingRepository>(),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Semantic warm-up failed; retrying in {Interval}.", PollInterval);
        }
    }
}

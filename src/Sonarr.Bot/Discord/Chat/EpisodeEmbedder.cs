using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Infrastructure.Persistence.Repositories.Chat;

namespace Sonarr.Bot.Discord.Chat;

/// <summary>
/// Fills in <c>chat.episode.embedding</c> for rows that do not have one yet: the legacy episodes
/// the migrator imported (docs/11 cutover step 4) and every episode written since, which the turn
/// path stores unembedded on purpose.
/// </summary>
/// <remarks>
/// Off the turn path deliberately. Embedding her own line would add tens of milliseconds to a
/// reply on the J2900 to produce something only a later turn reads, so the write leaves the vector
/// null and this service catches up within a few minutes — an episode is simply not recallable
/// until then (<see cref="IEpisodeRepository.SearchAsync"/> skips unembedded rows).
/// <para>Idempotent by construction: the query is "rows with no embedding", so a crash mid-batch
/// costs at most one batch of repeated work.</para>
/// </remarks>
public sealed class EpisodeEmbedder(
    ITextEmbedder embedder,
    IServiceScopeFactory scopes,
    ILogger<EpisodeEmbedder> log) : BackgroundService
{
    /// <summary>Wait between batches once the queue is empty — docs/11 budgets "minutes".</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);

    /// <summary>Wait between full batches, so a large legacy import drains without hogging the CPU.</summary>
    public static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(10);

    /// <summary>Rows per batch. Smaller than <see cref="EpisodeRepository.BatchSize"/>: the padded
    /// hidden-state tensor is rows × tokens × 384 floats, and 200 of those is tens of MB on a box
    /// with 8 GB.</summary>
    public const int BatchSize = 32;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Ready())
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int done = await BatchAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(done == BatchSize ? BusyInterval : IdleInterval, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One embed, up front, to find out whether there is a model at all. Worth its own probe
    /// because the loaded session caches its own load failure — without a model file every batch
    /// would throw forever, and logging that every five minutes for the life of the process is
    /// worse than not running.
    /// </summary>
    private bool Ready()
    {
        try
        {
            _ = embedder.Embed("ready");
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex, "No embedding model; episode embeddings stay empty and callbacks stay off.");
            return false;
        }
    }

    /// <summary>
    /// Embeds one batch. Returns how many rows were written. Public so a test can drive a batch
    /// without waiting on the timer, same as <c>JobScheduler.PollOnceAsync</c>.
    /// </summary>
    public async Task<int> BatchAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var episodes = scope.ServiceProvider.GetRequiredService<IEpisodeRepository>();

            IReadOnlyList<Episode> pending = await episodes
                .GetUnembeddedAsync(BatchSize, ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return 0;
            }

            // A blank quote can never be recalled, but it would come back in every batch forever.
            // Storing the zero vector the embedder returns for it takes it out of the queue.
            IReadOnlyList<float[]> vectors = embedder.EmbedBatch([.. pending.Select(e => e.Quote)]);

            await episodes.SetEmbeddingsAsync(
                [.. pending.Select((e, i) => (e.Id, vectors[i]))], ct).ConfigureAwait(false);

            log.LogInformation("Embedded {Count} episode(s).", pending.Count);
            return pending.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            // Postgres down, or the pgvector write rejected: retry on the next tick. Nothing is
            // lost — the rows are still unembedded, so the next batch picks up the same ones.
            log.LogWarning(ex, "Episode embedding batch failed; retrying in {Interval}.", IdleInterval);
            return 0;
        }
    }
}

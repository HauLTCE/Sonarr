using Sonarr.Application.Chat;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;
using Sonarr.Elaine.Determinism;
using Sonarr.Infrastructure.Embeddings;
using Sonarr.Infrastructure.Persistence.Repositories.Chat;

namespace Sonarr.Bot.Discord.Chat;

/// <summary>
/// One call that wires the chat slice: the person repository, the pipeline, the real clock and
/// the gateway handler.
/// </summary>
public static class ChatServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c>, <c>AddSonarrRedis</c>, <c>AddSonarrConfig</c> (the
    /// kill switch) and <c>AddSonarrPersona</c> (the pipeline resolves the live
    /// <c>PersonaHolder</c>), and before <c>AddSonarrDiscord</c>.
    /// </summary>
    public static IServiceCollection AddSonarrChat(this IServiceCollection services, SonarrOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IIntentEmbeddingRepository, IntentEmbeddingRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();

        // Server-event memory: written by PresenceSampler, read when she brings a record up.
        services.AddScoped<IGuildStateRepository, GuildStateRepository>();
        services.AddScoped<IChatPipeline, ChatPipeline>();
        services.AddScoped<ChatEditWatcher>();

        // Read-only view for /relationship and /memories — no turn, no writes.
        services.AddScoped<ChatIntrospection>();

        // /opinion: topic centroid of the last few messages against the stance registry.
        services.AddScoped<ChatOpinions>();

        // Stateless, and the engine may not read the clock itself.
        services.AddSingleton<IClock, SystemClock>();

        // Semantic tier (docs/10): one loaded model and one set of example vectors per process.
        // The model file is loaded on first use, not here — absent files stay non-fatal.
        services.AddSingleton(new OnnxEmbedderOptions { ModelPath = options.ModelPath });
        services.AddSingleton<ITextEmbedder, OnnxTextEmbedder>();
        services.AddSingleton<SemanticIntentIndex>();
        services.AddHostedService<SemanticWarmup>();

        // Fills TurnInput.Callback from episodic memory when something relevant exists.
        services.AddScoped<CallbackRetriever>();

        // Nothing else ever writes chat.episode.embedding: the turn path stores episodes
        // unembedded and this drains the queue in the background (docs/11 cutover step 4).
        services.AddHostedService<EpisodeEmbedder>();

        services.AddSingleton<ChatOnMessage>();
        services.AddHostedService(sp => sp.GetRequiredService<ChatOnMessage>());

        return services;
    }
}

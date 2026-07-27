using Sonarr.Application.Chat;
using Sonarr.Domain.Abstractions;
using Sonarr.Elaine.Determinism;
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
    public static IServiceCollection AddSonarrChat(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IChatPipeline, ChatPipeline>();
        services.AddScoped<ChatEditWatcher>();

        // Read-only view for /relationship and /memories — no turn, no writes.
        services.AddScoped<ChatIntrospection>();

        // Stateless, and the engine may not read the clock itself.
        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<ChatOnMessage>();
        services.AddHostedService(sp => sp.GetRequiredService<ChatOnMessage>());

        return services;
    }
}

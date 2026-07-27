using Sonarr.Bot.Configuration;
using Sonarr.Elaine.Persona;

namespace Sonarr.Bot.Persona;

/// <summary>Persona loading and hot-reload wiring (docs/10-elaine-engine.md).</summary>
public static class PersonaServiceCollectionExtensions
{
    /// <summary>
    /// Loads the persona eagerly, at boot, on the calling thread. An invalid persona throws here
    /// rather than at the first mention — that is the fail-fast half of the docs/10 contract, and
    /// the reason this is not a hosted service.
    /// </summary>
    /// <exception cref="InvalidOperationException">The persona does not validate.</exception>
    public static IServiceCollection AddSonarrPersona(this IServiceCollection services, SonarrOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        DirectoryPersonaSource source = new(options.PersonaPath);
        PersonaHolder holder = PersonaHolder.TryCreate(source, out PersonaValidationResult result)
            ?? throw new InvalidOperationException(
                $"Persona at '{options.PersonaPath}' is invalid, refusing to start.\n{result.Report()}");

        services.AddSingleton(source);
        services.AddSingleton(holder);
        services.AddHostedService<PersonaWatcher>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Health;

/// <summary>DI wiring for the self-test aggregator. The host calls <see cref="AddSonarrHealth"/> once.</summary>
public static class HealthServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISelfTest"/> as a singleton — it holds the last report and the
    /// change-detection state, so there must be exactly one. The probes it aggregates are
    /// registered by the host, which owns the connections (docs/08-background-services.md).
    /// </summary>
    public static IServiceCollection AddSonarrHealth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISelfTest, SelfTest>();
        return services;
    }
}

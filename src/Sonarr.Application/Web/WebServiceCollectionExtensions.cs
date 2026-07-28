using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Web;

/// <summary>Panel API application services (docs/09-web-panels.md).</summary>
public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c> and <c>AddSonarrRedis</c>. The
    /// <see cref="Domain.Web.AdminAllowList"/> comes from options and is registered by the host.
    /// </summary>
    public static IServiceCollection AddSonarrWebAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IWebAuthService, WebAuthService>();
        return services;
    }
}

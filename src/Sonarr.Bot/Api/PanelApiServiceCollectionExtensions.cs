using Sonarr.Application.Web;
using Sonarr.Bot.Configuration;
using Sonarr.Bot.Discord.Web;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Web;
using Sonarr.Infrastructure.Persistence.Repositories.Web;

namespace Sonarr.Bot.Api;

/// <summary>One call that wires the panel API: CORS, the auth service, its repository and the DM sender.</summary>
public static class PanelApiServiceCollectionExtensions
{
    public const string CorsPolicy = "sonarr-panel";

    /// <summary>
    /// Add after <c>AddSonarrPersistence</c>, <c>AddSonarrRedis</c> and <c>AddSonarrConfig</c>.
    /// </summary>
    public static IServiceCollection AddSonarrPanelApi(this IServiceCollection services, SonarrOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Locked to the panel origin (docs/09). Credentials are on, because the session is a cookie
        // — which is also why the origin list cannot be a wildcard: browsers refuse the combination.
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(options.PanelBaseUrl.TrimEnd('/'))
            .AllowCredentials()
            .WithHeaders("Content-Type", PanelCookies.CsrfHeader)
            .WithMethods("GET", "POST", "PUT", "DELETE")));

        services.AddScoped<IWebAuthRepository, WebAuthRepository>();
        services.AddScoped<IUserDataRepository, UserDataRepository>();
        services.AddSonarrWebAuth();

        // Singleton: the allow-list is env-sourced and immutable for the process lifetime.
        services.AddSingleton(new AdminAllowList(options.AdminUserIds));

        // The middle tier. Singleton because the gateway client is one; resolved per request rather
        // than cached, because a role taken away on Discord must take effect on the next request.
        services.AddSingleton<IGuildAuthority, GatewayGuildAuthority>();

        services.AddSingleton<LoginTokenSender>();
        services.AddHostedService<StatusPagePusher>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Caching;
using StackExchange.Redis;

namespace Sonarr.Infrastructure.Caching;

/// <summary>DI wiring for the Redis caches. The host calls <see cref="AddSonarrRedis"/> once.</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single <see cref="IConnectionMultiplexer"/> plus every cache implementation.
    /// </summary>
    /// <remarks>
    /// The multiplexer is a singleton and is created lazily with <c>AbortOnConnectFail = false</c>,
    /// so the bot starts even when Redis is not up yet; StackExchange.Redis reconnects on its own
    /// and the caches degrade per their documented fail-open/fail-closed contracts meanwhile.
    /// </remarks>
    public static IServiceCollection AddSonarrRedis(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ClientName = "sonarr-bot";

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));

        services.AddSingleton<ISessionCache, RedisSessionCache>();
        services.AddSingleton<ICooldownStore, RedisCooldownStore>();
        services.AddSingleton<IMusicSessionCache, RedisMusicSessionCache>();
        services.AddSingleton<IPresenceCache, RedisPresenceCache>();
        services.AddSingleton<IConfigCache, RedisConfigCache>();

        return services;
    }
}

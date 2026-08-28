using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// Redis, examined by a ping the way the plain health check did. Same remedy as Postgres — a
/// stopped container started back up — because a cache that is merely down is the common
/// case, and the caches fail open anyway: what is really lost while Redis is down is the
/// write-through that makes a CLI or slash-command change apply in the running bot right away.
/// </summary>
internal sealed class RedisCheck : IDoctorCheck
{
    public string Name => "redis";

    public async Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        if (CliHost.Configuration["REDIS_CONNECTION"] is not { Length: > 0 })
        {
            return Diagnosis.Blocked("REDIS_CONNECTION is not set — see the env row");
        }

        try
        {
            using IServiceScope scope = CliHost.Scope();
            IConnectionMultiplexer redis =
                scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            TimeSpan roundTrip = await redis.GetDatabase().PingAsync();
            return Diagnosis.Ok($"pinged in {roundTrip.TotalMilliseconds.ToString("0.#")} ms");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Diagnosis.Broken(
                Doctor.OneLine(ex.Message),
                "start Redis (on the server: the compose stack in /root/sonarr-net), or check "
                + "the host and port in REDIS_CONNECTION");
        }
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
        => await DockerRemedy.TryStartAsync("redis", async () =>
        {
            using IServiceScope scope = CliHost.Scope();
            IConnectionMultiplexer redis =
                scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            await redis.GetDatabase().PingAsync();
            return true;
        }, ct);
}

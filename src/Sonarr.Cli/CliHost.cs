using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sonarr.Application.Config;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Caching;
using Sonarr.Infrastructure.Configuration;
using Sonarr.Infrastructure.Persistence;
using Sonarr.Infrastructure.Persistence.Repositories;
using Sonarr.Infrastructure.Persistence.Repositories.Chat;
using Sonarr.Infrastructure.Persistence.Repositories.Stats;

namespace Sonarr.Cli;

/// <summary>
/// The CLI's service provider: the same <c>.env</c>, the same repositories and the same Redis
/// invalidation the bot uses, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Second process, not a client of the first.</b> There is no IPC into the running bot, so this
/// reads Postgres and Redis directly. That is why <c>sonarr stats</c> reports what is <em>stored</em>
/// (stats.command_usage, stats.activity_sample, core.member.first_seen_at) rather than the
/// in-process counters <c>SonarrMetrics</c> holds — those reset on restart and belong to the bot's
/// address space. It also means the CLI works with the bot stopped, which is when you most want to
/// look at things.
/// </para>
/// <para>
/// <b>No gateway.</b> No Discord.Net, no token, no connection. Writes therefore go through the
/// Application services (so Redis is invalidated and the running bot sees the change within its
/// cache TTL) but cannot do the one thing that needs the gateway: check that a snowflake is the
/// <em>kind</em> of thing its config key asks for. See <see cref="OfflineGuildDirectory"/>.
/// </para>
/// </remarks>
public static class CliHost
{
    private static ServiceProvider? _provider;
    private static IConfiguration? _configuration;

    /// <summary>
    /// The .env plus real environment variables, resolved the same way the bot resolves them.
    /// </summary>
    /// <remarks>
    /// <c>SONARR_ENV</c> names the file outright; without it the search walks upwards from the
    /// working directory (<see cref="DotEnv.FindUpwards"/>), which is what makes the CLI work from
    /// anywhere inside the deployment tree. The explicit variable is what makes it work from
    /// <em>outside</em> it: the installed <c>/usr/local/bin/sonarr</c> exports it, so a person
    /// standing in their home directory gets the same answers as one standing in /opt/sonarr.
    /// </remarks>
    public static IConfiguration Configuration => _configuration ??= BuildConfiguration();

    private static IConfiguration BuildConfiguration()
    {
        ConfigurationBuilder builder = new();
        if (EnvFile() is { } envFile)
        {
            builder.AddDotEnvFile(envFile);
        }

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    /// <summary>
    /// The .env this process should read, or null when there is none to find.
    /// </summary>
    /// <remarks>
    /// A <c>SONARR_ENV</c> that points at nothing is an error rather than a silent fall back to the
    /// upward search: somebody set it deliberately, and quietly reading a different file than the
    /// one they named is how you spend an afternoon wondering why a write went to the wrong
    /// database. <see cref="DotEnv.AddDotEnvFile"/> ignores a missing path, so the check has to be
    /// here.
    /// </remarks>
    private static string? EnvFile()
    {
        string? named = Environment.GetEnvironmentVariable("SONARR_ENV");
        if (string.IsNullOrWhiteSpace(named))
        {
            return DotEnv.FindUpwards(Directory.GetCurrentDirectory());
        }

        return File.Exists(named)
            ? named
            : throw new CliError($"SONARR_ENV points at '{named}', which is not a file.", 2);
    }

    /// <summary>A scope to resolve services from. One per command; the command owns its lifetime.</summary>
    public static IServiceScope Scope() => Provider().CreateScope();

    private static ServiceProvider Provider()
    {
        if (_provider is not null)
        {
            return _provider;
        }

        string pg = Required("PG_CONNECTION");
        ServiceCollection services = new();

        // Warnings and above: a CLI that prints EF's connection chatter above its own output is a
        // CLI whose output you have to scroll to find.
        //
        // Three categories are silenced entirely, and each for the same reason — the message they
        // emit is a diagnostics dump written for a bug report, not a sentence:
        //
        //   EF's Database.Connection and Query both log the full Npgsql stack for one refused
        //   connection, so a stopped Postgres prints it twice. Program catches that exception and
        //   says so in one line; left on, that line lands 4 KB below the problem.
        //
        //   Sonarr.Infrastructure.Caching logs StackExchange.Redis's ~1 KB connection-state string
        //   ("IOCP: (Busy=0,Free=1000…") on every degraded read. The caches fail open by contract,
        //   so for a read that dump is describing something that already recovered. What a stopped
        //   Redis actually costs a CLI write is said by SetCommand instead, in a sentence.
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.None)
            .AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.None)
            .AddFilter("Sonarr.Infrastructure.Caching", LogLevel.None)
            .AddSimpleConsole(console => console.SingleLine = true));

        services.AddSonarrPersistence(pg);

        // Redis is what makes a write visible to the running bot: FeatureGate.SetAsync drops
        // cfg:flags, and the bot's next read misses and goes to Postgres. Without it the bot
        // would serve a stale flag for up to the 1-minute TTL.
        services.AddSonarrRedis(Required("REDIS_CONNECTION"));

        services.AddSonarrConfig();
        services.AddSingleton<IGuildDirectory, OfflineGuildDirectory>();

        // The repositories AddSonarrPersistence does not register, because they belong to bot
        // slices that also wire Discord (chat, stats). Same scoped lifetime.
        services.AddScoped<IStatsRepository, StatsRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IEpisodeRepository, EpisodeRepository>();

        _provider = services.BuildServiceProvider();
        return _provider;
    }

    /// <summary>
    /// A key the CLI cannot work without. The error names the key and never its value — the same
    /// rule <c>SonarrOptionsSetup.Validate</c> follows, for the same reason.
    /// </summary>
    private static string Required(string key)
        => Configuration[key] is { Length: > 0 } value
            ? value.Trim()
            : throw new CliError(
                $"{key} is not set. Run this from inside the deployment directory, or point "
                + "SONARR_ENV at the .env.", 2);

    public static void Dispose()
    {
        _provider?.Dispose();
        _provider = null;
    }
}

/// <summary>
/// The <see cref="IGuildDirectory"/> a process with no gateway can honestly provide: nothing.
/// </summary>
/// <remarks>
/// Every method returns null, which the consumers already treat as "not cached, fall back to the
/// id" — <c>GuildConfigService.Missing</c> documents a null guild as a deliberate pass, because from
/// the gateway cache's side "not cached" and "does not exist" are indistinguishable and rejecting on
/// absence would fail every write during the seconds after a reconnect.
/// <para>
/// The consequence is real and is stated in <c>sonarr help set</c>: this process cannot catch
/// <c>sonarr set dj_role &lt;a channel id&gt;</c>. It is not a fake that makes a guard vacuous — the
/// guard has always had this hole for un-cached guilds, and the CLI is simply always in that state.
/// Feature flags, which are what goal 5 actually asked to set from here, need no directory at all.
/// </para>
/// </remarks>
internal sealed class OfflineGuildDirectory : IGuildDirectory
{
    public ValueTask<GuildDirectory?> GetAsync(ulong guildId, CancellationToken ct = default)
        => ValueTask.FromResult<GuildDirectory?>(null);

    public string? DisplayName(ulong guildId, ulong userId) => null;

    public string? GuildName(ulong guildId) => null;
}

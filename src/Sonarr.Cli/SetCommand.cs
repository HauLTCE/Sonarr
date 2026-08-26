using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using StackExchange.Redis;

namespace Sonarr.Cli;

/// <summary>
/// <c>sonarr set KEY VALUE</c> — one entry point over both config systems, dispatching on which
/// catalog the key is in.
/// </summary>
/// <remarks>
/// <para>
/// The two stores are not interchangeable and the split is structural rather than stylistic:
/// <c>core.feature_flag</c> has no foreign key to <c>core.guild</c>, so <c>guild_id = 0</c> is a
/// legal row and a toggle can be global. <c>core.guild_config</c> has one, so a global config key
/// cannot exist — which is why <c>sonarr set sleep false</c> (goal 5's "make it so it is a global
/// configuration") had to be a feature flag.
/// </para>
/// <para>
/// Both paths go through the Application service rather than the repository, so the Redis
/// invalidation happens and the running bot picks the change up on its next read.
/// </para>
/// </remarks>
internal static class SetCommand
{
    /// <summary>
    /// Who a CLI write is attributed to in the <c>changed_by</c> / <c>updated_by</c> column.
    /// </summary>
    /// <remarks>
    /// Zero, not a real user id: the columns hold a Discord snowflake and nobody on Discord made
    /// this change. Anyone with shell access on the box is already past every authority the bot
    /// has, so inventing an id would only make the audit row lie about where the write came from.
    /// Snowflakes are never zero, so 0 reads unambiguously as "from the machine".
    /// </remarks>
    private const ulong ShellActor = 0;

    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        if (cli.Positional.Count < 2)
        {
            throw CliError.Usage("set wants a key and a value: `sonarr set sleep false`.");
        }

        string key = cli.Positional[0];
        // Joined rather than [1] alone so a value with spaces survives being quoted by the shell
        // and then split back apart — `sonarr set timezone "Asia/Ho_Chi_Minh"` has one token, but
        // a channel-weights list typed without quotes has several.
        string value = string.Join(' ', cli.Positional.Skip(1));
        ulong guildId = cli.Guild();

        using IServiceScope scope = CliHost.Scope();

        string feature = FeatureNames.Resolve(key);
        if (FeatureNames.IsKnown(feature))
        {
            return await SetFeatureAsync(scope, feature, value, guildId);
        }

        if (ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition))
        {
            return await SetConfigAsync(scope, definition, value, guildId);
        }

        throw CliError.Usage(
            $"'{key}' is neither a toggle nor a config key. `sonarr get` lists both.");
    }

    private static async Task<int> SetFeatureAsync(
        IServiceScope scope, string feature, string value, ulong guildId)
    {
        if (!TryParseBoolean(value, out bool state))
        {
            throw CliError.Usage($"a toggle is on or off — '{value}' is neither. Try true or false.");
        }

        ConfigWriteResult result = await scope.ServiceProvider
            .GetRequiredService<IFeatureGate>()
            .SetAsync(feature, guildId, state, ShellActor);

        return Report(scope, result, guildId == 0
            ? "globally — every server without its own row follows this"
            : $"for server {guildId}");
    }

    private static async Task<int> SetConfigAsync(
        IServiceScope scope, ConfigKeyDefinition definition, string value, ulong guildId)
    {
        if (guildId == 0)
        {
            // Not a validation nicety: core.guild_config has a foreign key to core.guild, so a
            // guild_id 0 row would be rejected by Postgres. Saying so here beats a constraint
            // violation.
            throw CliError.Usage(
                $"`{definition.Key}` is a per-server setting — pass --guild ID. "
                + "Only toggles (sleep, midday) can be set globally.");
        }

        ConfigWriteResult result = await scope.ServiceProvider
            .GetRequiredService<IGuildConfigService>()
            .SetAsync(guildId, definition.Key, value, ShellActor);

        return Report(scope, result, $"for server {guildId}");
    }

    /// <summary>
    /// Prints the service's own sentence — it is already written for a person — and turns the
    /// result into an exit code. A rejection is a 2: the write did not happen because the command
    /// was wrong.
    /// </summary>
    private static int Report(IServiceScope scope, ConfigWriteResult result, string scopeText)
    {
        string message = Plain(result.Message);
        if (!result.Success)
        {
            Console.Error.WriteLine($"sonarr: {message}");
            return 2;
        }

        Console.WriteLine($"{message} ({scopeText})");
        Stale(scope);
        return 0;
    }

    /// <summary>
    /// Says so when the row landed but the cache drop did not.
    /// </summary>
    /// <remarks>
    /// The caches fail open by contract (<c>RedisCacheBase</c>: an outage degrades features rather
    /// than stopping the bot), so <c>InvalidateFlagsAsync</c> swallowing a dead Redis is correct —
    /// but for a write it means the running bot keeps serving the old value until the key expires on
    /// its own, one minute for flags and ten for guild config. Postgres has the new value and the
    /// command really did succeed, so this is a note after a 0, not a failure: what a person needs
    /// to know is that the change is not live yet and roughly when it will be.
    /// <para>
    /// Checked here rather than plumbed through <see cref="ConfigWriteResult"/> because the services
    /// are shared with Discord, where the same outage is already visible in the bot's own logs and a
    /// second sentence in a slash-command reply would be noise.
    /// </para>
    /// </remarks>
    private static void Stale(IServiceScope scope)
    {
        IConnectionMultiplexer redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        if (redis.IsConnected)
        {
            return;
        }

        Console.Error.WriteLine(
            "sonarr: written, but Redis is unreachable — the cache was not dropped, so a running "
            + "bot serves the old value until the key expires (a minute for toggles, ten for config).");
    }

    /// <summary>
    /// Strips the Discord markup out of a shared message. The services write for a chat client, and
    /// a terminal shows `backticks` and **asterisks** literally.
    /// </summary>
    private static string Plain(string message)
        => message.Replace("**", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);

    /// <remarks>
    /// The same spellings <c>ConfigKeys.TryParseBoolean</c> accepts, because a person who learned
    /// <c>on</c>/<c>off</c> from <c>/config</c> should not discover the CLI is stricter. It is
    /// duplicated rather than shared because that one is private to the config catalog and a
    /// feature toggle is not a config key — the alternative is exposing a boolean parser from the
    /// domain for one caller.
    /// </remarks>
    private static bool TryParseBoolean(string raw, out bool value)
    {
        value = false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "true" or "on" or "yes" or "enabled" or "1":
                value = true;
                return true;
            case "false" or "off" or "no" or "disabled" or "0":
                return true;
            default:
                return false;
        }
    }
}

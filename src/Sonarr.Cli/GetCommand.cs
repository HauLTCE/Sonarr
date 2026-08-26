using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;

namespace Sonarr.Cli;

/// <summary>
/// <c>sonarr get</c> — the read half of <see cref="SetCommand"/>, over both catalogs.
/// </summary>
/// <remarks>
/// Every toggle is listed with where its state came from, because "off" from this server's row and
/// "off" from the built-in default behave identically until somebody changes the global row, and
/// then they do not. That distinction is <see cref="FeatureStateSource"/>, and it is the reason
/// <c>/feature list</c> shows it too.
/// </remarks>
internal static class GetCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        ulong guildId = cli.Guild();
        using IServiceScope scope = CliHost.Scope();
        IFeatureGate features = scope.ServiceProvider.GetRequiredService<IFeatureGate>();
        IGuildConfigService config = scope.ServiceProvider.GetRequiredService<IGuildConfigService>();

        return cli.Positional.Count > 0
            ? await OneAsync(features, config, cli.Positional[0], guildId)
            : await AllAsync(features, config, guildId);
    }

    private static async Task<int> OneAsync(
        IFeatureGate features, IGuildConfigService config, string key, ulong guildId)
    {
        string feature = FeatureNames.Resolve(key);
        if (FeatureNames.IsKnown(feature))
        {
            IReadOnlyList<FeatureState> all = await features.GetAllAsync(guildId);
            FeatureState state = all.First(f => f.Feature == feature);
            Console.WriteLine($"{state.Enabled.ToString().ToLowerInvariant()}  ({Source(state.Source, guildId)})");
            return 0;
        }

        if (!ConfigKeys.TryGet(key, out ConfigKeyDefinition? definition))
        {
            throw CliError.Usage($"'{key}' is neither a toggle nor a config key.");
        }

        if (guildId == 0)
        {
            throw CliError.Usage($"`{definition.Key}` is per-server — pass --guild ID.");
        }

        ConfigValue? value = await config.GetAsync(guildId, definition.Key);
        if (value is null)
        {
            // Exit 1 rather than printing "unset": this is the shape a script tests, and an empty
            // stdout with a non-zero code is what `if sonarr get log_channel` expects.
            Console.Error.WriteLine($"sonarr: {definition.Key} is not set.");
            return 1;
        }

        Console.WriteLine(value.Raw);
        return 0;
    }

    private static async Task<int> AllAsync(
        IFeatureGate features, IGuildConfigService config, ulong guildId)
    {
        Console.WriteLine(guildId == 0
            ? "Global view — toggles only, and every state shown is the global row or the default."
            : $"Server {guildId}.");

        Output.Heading("Toggles");
        IReadOnlyList<FeatureState> states = await features.GetAllAsync(guildId);
        Output.Rows(states.Select(s => (
            s.Feature,
            $"{(s.Enabled ? "on" : "off"),-3}  ({Source(s.Source, guildId)})")));

        if (guildId == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Config keys are per-server — pass --guild ID to see them.");
            return 0;
        }

        Output.Heading("Config");
        IReadOnlyDictionary<string, ConfigValue> values = await config.GetAllAsync(guildId);
        if (values.Count == 0)
        {
            Console.WriteLine("  (nothing set — every key is at its default)");
            return 0;
        }

        Output.Rows(values
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value.Raw)));

        // The keys with no row are the interesting other half: a person checking why welcomes are
        // silent wants to see that welcome_channel is absent, not to notice it missing from a list.
        List<string> unset = [.. ConfigKeys.All
            .Select(d => d.Key)
            .Where(k => !values.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)];

        if (unset.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  unset: {string.Join(", ", unset)}");
        }

        return 0;
    }

    /// <summary>
    /// Where a state came from, in the words of whoever is asking.
    /// </summary>
    /// <remarks>
    /// <see cref="FeatureStateSource.Guild"/> renders as "global row" when the guild <em>is</em> zero.
    /// The gate resolves the guild row first and guild 0's own row is a guild row by that rule, so a
    /// global view would otherwise report <c>sleep_mode</c> as set "on this server" — naming a server
    /// that does not exist, for the one setting whose whole point is that it belongs to no server.
    /// </remarks>
    private static string Source(FeatureStateSource source, ulong guildId) => source switch
    {
        FeatureStateSource.Guild when guildId == 0 => "global row",
        FeatureStateSource.Guild => "this server",
        FeatureStateSource.Global => "global row",
        _ => "default",
    };
}

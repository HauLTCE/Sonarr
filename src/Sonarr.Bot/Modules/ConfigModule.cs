using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/config</c> — the one config surface (docs/07-commands.md#server-management). Thin
/// translator: parse, call <see cref="IGuildConfigService"/>, format. All validation lives in
/// the service, which is the enforcement point — this module is not what makes a value legal.
/// </summary>
/// <remarks>
/// Two permission attributes, on purpose. <see cref="DefaultMemberPermissionsAttribute"/> is only a
/// <em>default</em> — a server admin can override it in Server Settings → Integrations, up to and
/// including granting the group to @everyone — so it hides the commands but does not enforce
/// anything. <see cref="RequireUserPermissionAttribute"/> is the enforcement: it re-checks the
/// caller's live gateway permissions on every invocation, the same belt-and-braces rule the
/// moderation modules follow (docs/checklist.md — Moderation).
/// </remarks>
[Group("config", "Server settings: channels, roles, timezone, import/export.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class ConfigModule(IGuildConfigService config)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("set", "Set one server setting.")]
    public async Task SetAsync(
        [Summary("key", "Which setting to change")]
        [Autocomplete(typeof(ConfigKeyAutocompleteHandler))] string key,
        [Summary("value", "Channel, role, timezone, on/off or a number — depends on the key")] string value)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        ConfigWriteResult result = await config.SetAsync(guildId, key, value, Context.User.Id);
        await RespondAsync(result.Message, ephemeral: true);
    }

    [SlashCommand("clear", "Put one setting back to its default.")]
    public async Task ClearAsync(
        [Summary("key", "Which setting to clear")]
        [Autocomplete(typeof(ConfigKeyAutocompleteHandler))] string key)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        ConfigWriteResult result = await config.ClearAsync(guildId, key, Context.User.Id);
        await RespondAsync(result.Message, ephemeral: true);
    }

    [SlashCommand("list", "Show every setting and what it's set to.")]
    public async Task ListAsync()
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IReadOnlyDictionary<string, ConfigValue> current = await config.GetAllAsync(guildId);

        var report = new StringBuilder("**Server settings**\n");
        foreach (ConfigKeyDefinition definition in ConfigKeys.All)
        {
            var value = current.TryGetValue(definition.Key, out ConfigValue? set)
                ? Describe(definition, set)
                : "_not set_";
            report.AppendLine($"`{definition.Key}` — {value}  ·  {definition.Description}");
        }

        await RespondAsync(report.ToString(), ephemeral: true);
    }

    [SlashCommand("export", "Dump this server's settings as JSON.")]
    public async Task ExportAsync()
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        var json = await config.ExportAsync(guildId);
        await RespondAsync($"```json\n{json}\n```", ephemeral: true);
    }

    [SlashCommand("import", "Apply a JSON block of settings. Rejected whole if anything is wrong.")]
    public async Task ImportAsync(
        [Summary("json", "JSON object of key/value pairs, as produced by /config export")] string json)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        // ponytail: JSON pasted as a string option (Discord caps it around 6k chars, and the
        // whole catalog is ~10 keys). Upgrade path: accept an IAttachment and download it if a
        // config ever outgrows that.
        ConfigImportResult result = await config.ImportAsync(guildId, json, Context.User.Id);

        if (result.Applied)
        {
            await RespondAsync($"Imported {result.KeyCount} setting(s).", ephemeral: true);
            return;
        }

        var reasons = string.Join("\n", result.Rejections.Select(r => $"• {r}"));
        await RespondAsync(
            $"Nothing was changed — fix these and try again:\n{reasons}",
            ephemeral: true);
    }

    private static string Describe(ConfigKeyDefinition definition, ConfigValue value)
        => definition.Kind switch
        {
            ConfigValueKind.ChannelId when value.AsSnowflake is { } channel => MentionUtils.MentionChannel(channel),
            ConfigValueKind.RoleId when value.AsSnowflake is { } role => MentionUtils.MentionRole(role),
            _ => $"`{value.Raw}`",
        };

    /// <summary><c>null</c> in a DM, after telling the user why.</summary>
    private async Task<ulong?> RequireGuildAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild.Id;
        }

        await RespondAsync("Config lives per server — run this in one.", ephemeral: true);
        return null;
    }
}

/// <summary>Autocomplete over the closed config-key catalog (docs/07-commands.md#design-rules).</summary>
public sealed class ConfigKeyAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = ConfigKeys.All
            .Where(d => d.Key.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(d => new AutocompleteResult(d.Key, d.Key));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}

using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Application.Moderation;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/config moderation</c> — the anti-spam policy surface. Same service, same validation and
/// same cache invalidation as <see cref="ConfigModule"/>; this only narrows the key list to the
/// moderation ones so an admin is not scrolling past music settings.
/// </summary>
// ponytail: docs/07 writes this as `/config moderation …`, but Discord.Net registers one top-level
// command per [Group] and ConfigModule already owns "config", so a second module claiming it would
// collide at registration. Upgrade path: move these three into ConfigModule as a nested
// [Group("moderation")] sub-module once that file's owner is done with it.
[Group("config-moderation", "Anti-spam policy: thresholds, what a trip does.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
// DefaultMemberPermissions is a default a server admin can override; this is the enforcement.
[RequireUserPermission(GuildPermission.ManageGuild)]
[RequireContext(ContextType.Guild)]
public sealed class ConfigModerationModule(IGuildConfigService config, IModerationService moderation)
    : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("show", "Show the anti-spam policy in effect.")]
    public async Task ShowAsync()
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        ModerationPolicy policy = await moderation.GetPolicyAsync(guild.Id);
        IReadOnlyDictionary<string, ConfigValue> stored = await config.GetAllAsync(guild.Id);

        StringBuilder text = new("**Anti-spam policy**\n");
        text.Append("Enabled: ").Append(policy.AntiSpamEnabled ? "yes" : "no").Append('\n');
        text.Append("Identical-flood at: ")
            .Append(policy.IdenticalFloodThreshold.ToString(CultureInfo.InvariantCulture))
            .Append(" repeats\n");
        text.Append("Mass-mention at: ")
            .Append(policy.MassMentionThreshold.ToString(CultureInfo.InvariantCulture))
            .Append(" mentions\n");
        text.Append("Invite links: ").Append(policy.InviteLinksBlocked ? "blocked" : "allowed").Append('\n');
        text.Append("On a trip: ").Append(policy.Action.ToString().ToLowerInvariant());
        if (policy.Action == SpamAction.Timeout)
        {
            text.Append(" for ").Append(DurationText.Describe(policy.Timeout));
        }

        text.Append('\n');
        text.Append("Delete the message: ").Append(policy.DeleteOffendingMessage ? "yes" : "no").Append('\n');

        var unset = ModerationConfigKeys.All.Count(k => !stored.ContainsKey(k));
        if (unset > 0)
        {
            text.Append('\n').Append(unset.ToString(CultureInfo.InvariantCulture))
                .Append(" of ").Append(ModerationConfigKeys.All.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" setting(s) are on their default. `/config-moderation set` to change one.");
        }

        await RespondPersonalAsync(text.ToString());
    }

    [SlashCommand("set", "Change one anti-spam setting.")]
    public async Task SetAsync(
        [Summary("key", "Which setting")]
        [Autocomplete(typeof(ModerationConfigKeyAutocompleteHandler))] string key,
        [Summary("value", "on/off, a number, or note|warn|timeout|kick|ban")] string value)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        if (!ModerationConfigKeys.IsKnown(key))
        {
            await RespondInvalidAsync(
                $"`{key}` isn't a moderation setting. Pick one from the list: " +
                string.Join(", ", ModerationConfigKeys.All.Select(k => $"`{k}`")));
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id, key.Trim().ToLowerInvariant(), value, Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("clear", "Put one anti-spam setting back to its default.")]
    public async Task ClearAsync(
        [Summary("key", "Which setting")]
        [Autocomplete(typeof(ModerationConfigKeyAutocompleteHandler))] string key)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        if (!ModerationConfigKeys.IsKnown(key))
        {
            await RespondInvalidAsync($"`{key}` isn't a moderation setting.");
            return;
        }

        ConfigWriteResult result = await config.ClearAsync(
            guild.Id, key.Trim().ToLowerInvariant(), Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }
}

/// <summary>Autocomplete over the moderation subset of the config catalog.</summary>
public sealed class ModerationConfigKeyAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = ModerationConfigKeys.All
            .Where(k => k.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(k => new AutocompleteResult(
                ModerationConfigKeys.Descriptions.TryGetValue(k, out var description)
                    ? Label(k, description)
                    : k,
                k));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }

    /// <summary>Discord caps a choice name at 100 characters.</summary>
    private static string Label(string key, string description)
    {
        var label = $"{key} — {description}";
        return label.Length <= 100 ? label : label[..100];
    }
}

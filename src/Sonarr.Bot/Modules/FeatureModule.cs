using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/feature</c> — the kill switches (docs/checklist.md — "Kill switches &amp; health").
/// Turning a module off makes it answer "this feature is currently off"; it never disappears.
/// </summary>
/// <remarks>
/// Two scopes, two permission models. <c>module</c> and <c>list</c> are per-guild and gated on
/// Manage Server, which is what the class attributes below express. <c>global</c> writes the
/// <c>guild_id = 0</c> row every guild inherits, and Discord has no permission that means "may
/// configure this bot everywhere" — so it is gated on <see cref="SonarrOptions.AdminUserIds"/>
/// instead, checked in the body because a guild permission attribute cannot say it.
/// </remarks>
[Group("feature", "Turn my modules on or off for this server.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
// DefaultMemberPermissions is a default a server admin can override; this is the enforcement.
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class FeatureModule(IFeatureGate gate, SonarrOptions options)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("module", "Turn one module on or off here.")]
    public async Task ModuleAsync(
        [Summary("module", "Which module")]
        [Autocomplete(typeof(FeatureNameAutocompleteHandler))] string module,
        [Summary("state", "On or off")] bool state)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        ConfigWriteResult result = await gate.SetAsync(module, guildId, state, Context.User.Id);
        await RespondAsync(result.Message, ephemeral: true);
    }

    /// <summary>
    /// <c>/feature global</c> — the restored <c>SLEEP_MODE_ENABLED</c> / <c>MIDDAY_BREAK_ENABLED</c>
    /// surface, and the only one in Discord that writes a global row.
    /// </summary>
    /// <remarks>
    /// Runs in a DM as happily as in a guild: the row it writes belongs to no guild, and
    /// <c>core.feature_flag</c> has no foreign key to <c>core.guild</c> precisely so
    /// <c>guild_id = 0</c> can exist. A per-guild override still wins over whatever this sets —
    /// see <c>FeatureGate.Resolve</c>.
    /// </remarks>
    [SlashCommand("global", "Set a toggle for every server at once (bot admins only).")]
    public async Task GlobalAsync(
        [Summary("feature", "Which toggle")]
        [Autocomplete(typeof(FeatureNameAutocompleteHandler))] string feature,
        [Summary("state", "On or off")] bool state)
    {
        if (!options.AdminUserIds.Contains(Context.User.Id))
        {
            // Deliberately says who can rather than just refusing: on a one-community bot the
            // person hitting this is usually an admin who ran it in the wrong scope.
            await RespondAsync(
                "Global toggles are limited to the bot's own admins (`ADMIN_USER_IDS`). "
                + "`/feature module` sets it for this server.",
                ephemeral: true);
            return;
        }

        ConfigWriteResult result = await gate.SetAsync(feature, GlobalScope, state, Context.User.Id);
        await RespondAsync(
            result.Success ? result.Message + " — everywhere, unless a server overrides it." : result.Message,
            ephemeral: true);
    }

    /// <summary>The <c>guild_id</c> a global flag row carries. Not a guild; no guild has id 0.</summary>
    private const ulong GlobalScope = 0;

    [SlashCommand("list", "Show which modules are on here.")]
    public async Task ListAsync()
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IReadOnlyList<FeatureState> states = await gate.GetAllAsync(guildId);

        var report = new StringBuilder("**Modules**\n");
        foreach (FeatureState state in states)
        {
            var origin = state.Source switch
            {
                FeatureStateSource.Guild => "set here",
                FeatureStateSource.Global => "global setting",
                _ => "default",
            };

            // A restriction reads backwards from a module: "on" takes something away, so say what
            // it does rather than showing a green tick for "she goes quiet at 22:00".
            var mark = FeatureNames.Restrictions.Contains(state.Feature)
                ? state.Enabled ? "🌙" : "▫️"
                : state.Enabled ? "✅" : "🚫";

            report.AppendLine($"{mark} **{state.Feature}** — {origin}");
        }

        await RespondAsync(report.ToString(), ephemeral: true);
    }

    private async Task<ulong?> RequireGuildAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild.Id;
        }

        await RespondAsync("Kill switches are per server — run this in one.", ephemeral: true);
        return null;
    }
}

/// <summary>Autocomplete over the module catalog.</summary>
public sealed class FeatureNameAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = FeatureNames.All
            .Where(f => f.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(f => new AutocompleteResult(f, f));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}

/// <summary>
/// Controller-layer kill-switch check. Put it on a module or command and a disabled feature
/// answers with a sentence instead of failing silently — the InteractionHandler shows an
/// <c>UnmetPrecondition</c> reason verbatim.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireFeatureAttribute(string feature) : PreconditionAttribute
{
    public string Feature { get; } = feature;

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        // DMs have no guild row to consult; the global flags still apply.
        var guildId = context.Guild?.Id ?? 0;
        var gate = services.GetRequiredService<IFeatureGate>();

        return await gate.IsEnabledAsync(Feature, guildId)
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError($"**{Feature}** is currently off. An admin can turn it back on with `/feature module`.");
    }
}

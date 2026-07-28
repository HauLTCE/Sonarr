using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/feature</c> — the kill switches (docs/checklist.md — "Kill switches &amp; health").
/// Turning a module off makes it answer "this feature is currently off"; it never disappears.
/// </summary>
[Group("feature", "Turn my modules on or off for this server.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
// DefaultMemberPermissions is a default a server admin can override; this is the enforcement.
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class FeatureModule(IFeatureGate gate)
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
            report.AppendLine($"{(state.Enabled ? "✅" : "🚫")} **{state.Feature}** — {origin}");
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

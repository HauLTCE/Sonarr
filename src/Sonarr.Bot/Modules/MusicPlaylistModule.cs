using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/playlist save|load|list|delete</c> and <c>/grab</c> (docs/07-commands.md#music).
/// Playlists are per guild and owner-scoped; deleting someone else's needs a DJ.
/// </summary>
[RequireFeature(FeatureNames.Music)]
[RequireContext(ContextType.Guild)]
[Group("playlist", "Save and reload queues.")]
public sealed class MusicPlaylistModule(IMusicService music, IGuildConfigService config)
    : MusicModuleBase(music, config)
{
    [SlashCommand("save", "Save the current queue under a name.")]
    public async Task SaveAsync([Summary("name", "What to call it")] string name)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.Length(name, MusicRules.MaxPlaylistNameLength, "playlist name") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        await RespondResultAsync(await Music.SavePlaylistAsync(context, name));
    }

    [SlashCommand("load", "Queue up a saved playlist.")]
    public async Task LoadAsync(
        [Summary("name", "Which playlist")]
        [Autocomplete(typeof(PlaylistAutocompleteHandler))] string name)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.Length(name, MusicRules.MaxPlaylistNameLength, "playlist name") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        // Loading resolves every track through Lavalink, which outlasts the 3 s window.
        await DeferAsync();
        MusicResult result = await Music.LoadPlaylistAsync(context, name);
        await FollowupAsync(result.Message);
    }

    [SlashCommand("list", "The playlists saved here.")]
    public async Task ListAsync()
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        IReadOnlyList<PlaylistSummary> saved = await Music.ListPlaylistsAsync(context.GuildId);
        if (saved.Count == 0)
        {
            await RespondPersonalAsync("No playlists saved here yet — `/playlist save` makes one.");
            return;
        }

        var body = new StringBuilder();
        foreach (PlaylistSummary playlist in saved)
        {
            body.Append("**").Append(playlist.Name).Append("** — ")
                .Append(playlist.TrackCount)
                .Append(playlist.TrackCount == 1 ? " track" : " tracks")
                .Append(" · <@").Append(playlist.OwnerId).AppendLine(">");
        }

        await RespondAsync(embed: new EmbedBuilder()
            .WithTitle("Saved playlists")
            .WithColor(new Color(0x5865F2))
            .WithDescription(body.ToString())
            .Build());
    }

    [SlashCommand("delete", "Delete one of your playlists.")]
    public async Task DeleteAsync(
        [Summary("name", "Which playlist")]
        [Autocomplete(typeof(PlaylistAutocompleteHandler))] string name)
    {
        if (await ContextAsync() is not { } context)
        {
            return;
        }

        if (InputGuards.Length(name, MusicRules.MaxPlaylistNameLength, "playlist name") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        await RespondResultAsync(await Music.DeletePlaylistAsync(context, name));
    }
}

/// <summary>Names for <c>/playlist load</c> and <c>/playlist delete</c>, from this guild only.</summary>
public sealed class PlaylistAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        // Resolved per call, not injected: IMusicService is scoped and the handler is not.
        var music = services.GetRequiredService<IMusicService>();
        IReadOnlyList<PlaylistSummary> saved = await music.ListPlaylistsAsync(context.Guild.Id);

        IEnumerable<AutocompleteResult> results = saved
            .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(p => new AutocompleteResult($"{p.Name} ({p.TrackCount})", p.Name));

        return AutocompletionResult.FromSuccess(results);
    }
}

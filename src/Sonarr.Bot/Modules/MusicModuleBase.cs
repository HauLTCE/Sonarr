using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// Shared plumbing for the music modules: the guild check, the DJ check, and the
/// <see cref="MusicContext"/> that carries who/where into <see cref="IMusicService"/>.
/// Everything else is the service's job (docs/02-architecture.md).
/// </summary>
public abstract class MusicModuleBase(IMusicService music, IGuildConfigService config)
    : SonarrModuleBase<SocketInteractionContext>
{
    protected IMusicService Music { get; } = music;

    /// <summary>
    /// Who is asking, where they are, and whether they are a DJ. <c>null</c> in a DM, after
    /// telling the user why.
    /// </summary>
    protected async Task<MusicContext?> ContextAsync()
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Music happens in a server — run this in one.");
            return null;
        }

        return new MusicContext(
            Context.Guild.Id,
            Context.User.Id,
            (Context.User as IVoiceState)?.VoiceChannel?.Id,
            Context.Channel.Id,
            await IsDjAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// DJ = holds the configured <c>dj_role</c>, or can manage the server. With no role configured
    /// everyone is a DJ, which is what a small server wants and what the old bot did.
    /// </summary>
    private async Task<bool> IsDjAsync()
    {
        if (Context.User is not SocketGuildUser member)
        {
            return false;
        }

        if (member.GuildPermissions.ManageGuild)
        {
            return true;
        }

        ConfigValue? value = await config.GetAsync(Context.Guild.Id, ConfigKeys.DjRole).ConfigureAwait(false);
        return value?.AsSnowflake is not { } roleId || member.Roles.Any(r => r.Id == roleId);
    }

    /// <summary>One-line answer for the common "did it work" service result.</summary>
    protected Task RespondResultAsync(MusicResult result)
        => result.Success ? RespondPublicAsync(result.Message) : RespondInvalidAsync(result.Message);

    protected static string Duration(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    /// <summary>A track as one clickable line. Streams have no useful duration.</summary>
    protected static string Line(TrackInfo track)
        => track.IsStream
            ? $"[{track.Title}]({track.Uri}) — live"
            : $"[{track.Title}]({track.Uri}) `{Duration(track.Duration)}`";
}

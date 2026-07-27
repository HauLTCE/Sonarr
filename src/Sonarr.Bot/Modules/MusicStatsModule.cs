using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Music;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/musicstats</c>, <c>/mytracks</c>, <c>/toptracks</c> (docs/07-commands.md#music). Read-only:
/// no voice channel needed, no DJ check.
/// </summary>
[RequireFeature(FeatureNames.Music)]
[RequireContext(ContextType.Guild)]
public sealed class MusicStatsModule(IMusicService music, IGuildConfigService config)
    : MusicModuleBase(music, config)
{
    [SlashCommand("musicstats", "What this server has been listening to.")]
    public async Task StatsAsync()
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Music stats are per server — run this in one.");
            return;
        }

        MusicStats stats = await Music.GetStatsAsync(Context.Guild.Id);

        if (stats.TotalPlays == 0)
        {
            await RespondPersonalAsync("Nothing played here yet.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Music stats")
            .WithColor(new Color(0x5865F2))
            .AddField("Tracks played", stats.TotalPlays, inline: true)
            .AddField("Listening time", Duration(stats.ListeningTime), inline: true);

        if (stats.MostPlayed.Count > 0)
        {
            var body = new StringBuilder();
            foreach (TrackPlayCount track in stats.MostPlayed)
            {
                body.Append("**").Append(track.Plays).Append("×** [")
                    .Append(track.Title).Append("](").Append(track.Uri).AppendLine(")");
            }

            embed.AddField("Most played", body.ToString());
        }

        if (stats.TopRequesters.Count > 0)
        {
            var body = new StringBuilder();
            foreach (RequesterPlayCount requester in stats.TopRequesters)
            {
                body.Append("<@").Append(requester.UserId).Append("> — ")
                    .Append(requester.Plays).AppendLine(requester.Plays == 1 ? " track" : " tracks");
            }

            embed.AddField("Top requesters", body.ToString());
        }

        await RespondAsync(embed: embed.Build());
    }

    [SlashCommand("mytracks", "The tracks you've been queueing.")]
    public async Task MyTracksAsync()
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Music stats are per server — run this in one.");
            return;
        }

        IReadOnlyList<TrackPlayCount> tracks = await Music.GetMyTracksAsync(Context.Guild.Id, Context.User.Id);
        if (tracks.Count == 0)
        {
            await RespondPersonalAsync("You haven't queued anything here yet.");
            return;
        }

        var body = new StringBuilder();
        foreach (TrackPlayCount track in tracks)
        {
            body.Append("**").Append(track.Plays).Append("×** [")
                .Append(track.Title).Append("](").Append(track.Uri).AppendLine(")");
        }

        await RespondAsync(embed: new EmbedBuilder()
            .WithTitle("Your tracks")
            .WithColor(new Color(0x5865F2))
            .WithDescription(body.ToString())
            .Build());
    }

    [SlashCommand("toptracks", "Crowd favourites, from the thumbs on /nowplaying.")]
    public async Task TopTracksAsync()
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Music stats are per server — run this in one.");
            return;
        }

        IReadOnlyList<RatedTrack> tracks = await Music.GetTopTracksAsync(Context.Guild.Id);
        if (tracks.Count == 0)
        {
            await RespondPersonalAsync("Nobody has rated anything here yet — the thumbs on `/nowplaying` do that.");
            return;
        }

        var body = new StringBuilder();
        foreach (RatedTrack track in tracks)
        {
            body.Append("[").Append(track.Title.Length == 0 ? track.Uri : track.Title)
                .Append("](").Append(track.Uri).Append(") — ")
                .Append(track.Likes).Append(" up · ").Append(track.Dislikes).AppendLine(" down");
        }

        await RespondAsync(embed: new EmbedBuilder()
            .WithTitle("Top rated")
            .WithColor(new Color(0x5865F2))
            .WithDescription(body.ToString())
            .Build());
    }
}

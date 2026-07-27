using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/announce</c> (docs/07-commands.md#server-management): post now, or schedule it as a
/// <c>core.job</c> row — one-shot or recurring — so a restart never swallows an announcement.
/// </summary>
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
public sealed class AnnounceModule(IAnnounceService announcements) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Matches <c>AnnounceService.MaxMessageLength</c>; the service is still the authority.</summary>
    private const int MaxMessageLength = 1800;

    [SlashCommand("announce", "Post an announcement now, or schedule one.")]
    public async Task AnnounceAsync(
        [Summary("channel", "Where it goes")]
        [ChannelTypes(ChannelType.Text, ChannelType.News)] ITextChannel channel,
        [Summary("message", "What to say")] string message,
        [Summary("schedule", "Leave empty to post now. \"in 2h\", \"tomorrow 9am\", \"every monday 09:00\".")]
        string? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Announcements are per server — run this in one.");
            return;
        }

        if (InputGuards.BelongsToGuild(channel.GuildId, Context.Guild.Id, "channel") is { } wrongGuild)
        {
            await RespondInvalidAsync(wrongGuild);
            return;
        }

        if (InputGuards.Length(message, MaxMessageLength, "announcement") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        // Sonarr's own permissions, not the caller's: staff can pick a channel the bot cannot post in.
        if (Context.Guild.CurrentUser is { } self
            && !self.GetPermissions(channel).SendMessages)
        {
            await RespondInvalidAsync($"I can't post in {MentionUtils.MentionChannel(channel.Id)} — check my permissions there.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(schedule))
        {
            ScheduleResult result = await announcements.ScheduleAsync(
                Context.Guild.Id, channel.Id, Context.User.Id, schedule, message);

            if (result.Success)
            {
                await RespondPersonalAsync(result.Message);
            }
            else
            {
                await RespondInvalidAsync(result.Message);
            }

            return;
        }

        // Immediate: no job row is worth writing for something that happens right now.
        await channel.SendMessageAsync(message.Trim(), allowedMentions: AllowedMentions.All);
        await RespondPersonalAsync($"Posted in {MentionUtils.MentionChannel(channel.Id)}.");
    }
}

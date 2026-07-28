using System.Globalization;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/birthday</c> and <c>/anniversary</c> (docs/07-commands.md#community). Both read the same
/// <c>core.member</c> row; the announcements they feed are posted by <c>DailyTick</c>.
/// </summary>
/// <remarks>
/// <para>Birthdays are opt-in by command only (docs/06, checklist "Timezone + birthday are opt-in
/// via command only") — nothing infers a date, and <c>/birthday clear</c> is on the same command so
/// taking it back is as easy as giving it.</para>
/// <para>The confirmation is ephemeral: a date of birth is the one thing here that is worth
/// something outside Discord, and the point of the feature is that the <em>greeting</em> is public,
/// not the disclosure. <c>/anniversary</c> is public — a join date is already visible in the member
/// list.</para>
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Social)]
public sealed class MilestoneModule(IMilestoneService milestones) : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("anniversary", "How long you've been in this server.")]
    public async Task AnniversaryAsync(
        [Summary("user", "Whose. Yours if you leave it out.")] IUser? user = null)
    {
        ArgumentNullException.ThrowIfNull(milestones);

        IUser target = user ?? Context.User;
        AnniversaryCard card = await milestones.GetAnniversaryAsync(Context.Guild.Id, target.Id);

        if (card.FirstSeenAt is null)
        {
            await RespondPersonalAsync(target.Id == Context.User.Id
                ? "I have no record of you arriving. Say something and I'll start counting."
                : "I have no record of them. They're new to me.");
            return;
        }

        var joined = card.FirstSeenAt.Value.ToUnixTimeSeconds();
        var years = card.Years switch
        {
            0 => "Less than a year in.",
            1 => "One year in.",
            _ => $"{card.Years} years in.",
        };

        var next = card.DaysUntilNext == 0
            ? "That's today, as it happens."
            : $"Next one <t:{Unix(card.NextOn!.Value)}:R>.";

        // RespondAsync rather than RespondPublicAsync: a display name is member-authored text, so
        // the reply needs AllowedMentions.None, which the convention helper does not take.
        await RespondAsync(
            $"{Format.Sanitize(Display(target))} joined <t:{joined}:D> — <t:{joined}:R>. {years} {next}",
            allowedMentions: AllowedMentions.None);
    }

    [Group("birthday", "Tell me your birthday, or take it back.")]
    public sealed class BirthdayGroup(IMilestoneService milestones)
        : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("set", "Month and day. The year is optional and only decides whether I say your age.")]
        public async Task SetAsync(
            [Summary("month", "1-12")] [MinValue(1)] [MaxValue(12)] int month,
            [Summary("day", "1-31")] [MinValue(1)] [MaxValue(31)] int day,
            [Summary("year", "Optional. Leave it out and I'll never mention your age.")]
            [MinValue(1900)] [MaxValue(2100)] int? year = null)
        {
            ArgumentNullException.ThrowIfNull(milestones);

            BirthdayResult result = await milestones.SetBirthdayAsync(
                Context.Guild.Id, Context.User.Id, month, day, year);

            if (result.Success)
            {
                // Ephemeral on purpose: the greeting is the public part, not the disclosure.
                await RespondPersonalAsync(result.Message);
                return;
            }

            await RespondInvalidAsync(result.Message);
        }

        [SlashCommand("clear", "Forget my birthday.")]
        public async Task ClearAsync()
        {
            ArgumentNullException.ThrowIfNull(milestones);

            BirthdayResult result = await milestones.ClearBirthdayAsync(Context.Guild.Id, Context.User.Id);
            await RespondPersonalAsync(result.Message);
        }
    }

    private static long Unix(DateOnly date)
        => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

    private static string Display(IUser user)
        => (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;
}

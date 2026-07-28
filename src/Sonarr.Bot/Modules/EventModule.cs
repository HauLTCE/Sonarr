using System.Globalization;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/event create|list|cancel</c> plus the RSVP buttons (docs/07-commands.md#community). The
/// service owns the row, the parsing and the wording; this owns everything only Discord can do —
/// the native scheduled event and the opt-in ping role.
/// </summary>
/// <remarks>
/// <c>[DefaultMemberPermissions(ManageEvents)]</c> guards the group, which is what the command
/// table asks for. <c>list</c> and the buttons sit outside it: RSVPing is for everyone, and a
/// member who cannot create an event can still read the schedule.
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Events)]
public sealed class EventModule(IEventService events) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Component id prefix — <c>event:rsvp:{eventId}:{response}</c>.</summary>
    public const string RsvpPrefix = "event:rsvp";

    /// <summary>Mirrors <c>EventService.MaxNameLength</c>; the service is still the authority.</summary>
    private const int MaxNameLength = 100;

    /// <summary>Mirrors <c>EventService.MaxDescriptionLength</c>.</summary>
    private const int MaxDescriptionLength = 1000;

    /// <summary>One screen's worth. The list is a glance, not an archive.</summary>
    private const int ListLimit = 10;

    [SlashCommand("events", "What's coming up in this server.")]
    public async Task ListAsync()
    {
        IReadOnlyList<EventView> upcoming = await events.ListAsync(Context.Guild.Id, ListLimit);

        if (upcoming.Count == 0)
        {
            await RespondPersonalAsync("Nothing scheduled. `/event create` if you have an idea.");
            return;
        }

        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle("Upcoming")
            .WithColor(new Color(0x4E8DE8));

        foreach (EventView view in upcoming)
        {
            string when = $"<t:{view.StartsAt.ToUnixTimeSeconds()}:F> · <t:{view.StartsAt.ToUnixTimeSeconds()}:R>";
            string rsvps = $"{view.Going} going · {view.Maybe} maybe";
            string blurb = string.IsNullOrWhiteSpace(view.Description)
                ? string.Empty
                : $"\n{Format.Sanitize(Trim(view.Description!, 200))}";

            embed.AddField(
                $"#{view.EventId} · {Format.Sanitize(view.Name)}",
                $"{when}\n{rsvps}{blurb}");
        }

        // Public: a schedule nobody can see is not a schedule. Nothing here mentions anyone.
        await RespondAsync(embed: embed.Build(), allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction($"{RsvpPrefix}:*:*", ignoreGroupNames: true)]
    public async Task RespondToEventAsync(string eventId, string response)
    {
        if (!ulong.TryParse(eventId, CultureInfo.InvariantCulture, out ulong id))
        {
            await RespondInvalidAsync("That button is from an older message — try `/events`.");
            return;
        }

        RsvpOutcome outcome = await events.RespondAsync(Context.Guild.Id, id, Context.User.Id, response);
        if (!outcome.Success)
        {
            await RespondInvalidAsync(outcome.Message);
            return;
        }

        string extra = await ApplyPingRoleAsync(outcome);

        // Ephemeral: an RSVP is between the member and the event, and thirty clicks should not be
        // thirty channel messages.
        await RespondPersonalAsync(outcome.Message + extra);
    }

    /// <summary>
    /// Adds or removes the event's ping role to match the RSVP. Returns a sentence to append when
    /// it did not work out, so the member is not told they will be pinged when they will not be.
    /// </summary>
    private async Task<string> ApplyPingRoleAsync(RsvpOutcome outcome)
    {
        if (outcome.PingRoleId is not { } roleId
            || Context.User is not IGuildUser member
            || Context.Guild.GetRole((ulong)roleId) is not { } role)
        {
            return string.Empty;
        }

        // Sonarr's own position matters, not the caller's: a role above the bot cannot be granted.
        if (Context.Guild.CurrentUser is { } self
            && (!self.GuildPermissions.ManageRoles || self.Hierarchy <= role.Position))
        {
            return $"\n(I can't manage {role.Name} — ask staff to move it below my role.)";
        }

        try
        {
            if (outcome.WantsRole)
            {
                await member.AddRoleAsync(role);
                return $"\nYou'll get pinged with {role.Name}.";
            }

            await member.RemoveRoleAsync(role);
            return string.Empty;
        }
        catch (global::Discord.Net.HttpException)
        {
            // Permissions can change between the check and the call. The RSVP itself is already
            // stored, so this is a note rather than a failure.
            return $"\n(Couldn't update {role.Name} — the RSVP still counts.)";
        }
    }

    private static string Trim(string text, int max)
        => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");

    /// <summary>
    /// The staff half of the feature. Separate class so the group can carry
    /// <c>ManageEvents</c> while <c>/events</c> and the RSVP buttons stay open to everyone.
    /// </summary>
    [RequireContext(ContextType.Guild)]
    [RequireFeature(FeatureNames.Events)]
    [DefaultMemberPermissions(GuildPermission.ManageEvents)]
    [Group("event", "Schedule something.")]
    public sealed class Manage(IEventService events) : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("create", "Put something on the calendar.")]
        public async Task CreateAsync(
            [Summary("name", "What it is")] string name,
            [Summary("when", "\"friday 8pm\", \"in 3 days\", \"1 jan 2027 20:00\"")] string when,
            [Summary("description", "Details, optional")] string? description = null,
            [Summary("ping_role", "Role given to anyone who RSVPs going, so they get reminded")]
            IRole? pingRole = null)
        {
            if (InputGuards.Length(name, MaxNameLength, "event name") is { } badName)
            {
                await RespondInvalidAsync(badName);
                return;
            }

            if (description is not null
                && InputGuards.Length(description, MaxDescriptionLength, "description") is { } badBlurb)
            {
                await RespondInvalidAsync(badBlurb);
                return;
            }

            if (pingRole is not null)
            {
                if (InputGuards.BelongsToGuild(pingRole.Guild.Id, Context.Guild.Id, "role") is { } wrongGuild)
                {
                    await RespondInvalidAsync(wrongGuild);
                    return;
                }

                // @everyone and managed roles (bot/booster/integration roles) cannot be handed out.
                if (pingRole.Id == Context.Guild.EveryoneRole.Id || pingRole.IsManaged)
                {
                    await RespondInvalidAsync("Pick a normal role — that one can't be assigned.");
                    return;
                }
            }

            EventResult result = await events.CreateAsync(
                Context.Guild.Id, Context.User.Id, name, when, description, pingRole?.Id);

            if (!result.Success)
            {
                await RespondInvalidAsync(result.Message);
                return;
            }

            await RespondAsync(
                result.Message,
                components: RsvpButtons(result.EventId),
                allowedMentions: AllowedMentions.None);

            await MirrorToDiscordAsync(result, name, description);
        }

        [SlashCommand("cancel", "Call one off.")]
        public async Task CancelAsync([Summary("id", "The number in `/events`")] long id)
        {
            if (InputGuards.InRange(id, 1, long.MaxValue, "event id") is { } problem)
            {
                await RespondInvalidAsync(problem);
                return;
            }

            EventResult result = await events.CancelAsync(
                Context.Guild.Id,
                (ulong)id,
                Context.User.Id,
                // Staff is Discord's answer, not the service's. The group's DefaultMemberPermissions
                // is a hint Discord can be told to ignore, so the real check happens here.
                isStaff: Context.User is IGuildUser { GuildPermissions.ManageEvents: true });

            if (!result.Success)
            {
                await RespondInvalidAsync(result.Message);
                return;
            }

            // Public: people who RSVPd need to hear this.
            await RespondAsync(result.Message, allowedMentions: AllowedMentions.None);
        }

        /// <summary>
        /// Mirrors the event onto Discord's own calendar, best effort. A failure here costs the
        /// native reminder, not the event — the row and the RSVP buttons already exist.
        /// </summary>
        private async Task MirrorToDiscordAsync(EventResult result, string name, string? description)
        {
            try
            {
                IGuildScheduledEvent native = await Context.Guild.CreateEventAsync(
                    name.Trim(),
                    result.StartsAt,
                    GuildScheduledEventType.External,
                    description: string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                    // External events need an end time and a location, both required by Discord.
                    endTime: result.StartsAt.AddHours(2),
                    location: Context.Channel.Name);

                await events.LinkDiscordEventAsync(result.EventId, native.Id);
            }
            catch (global::Discord.Net.HttpException ex)
            {
                await FollowupAsync(
                    $"(Couldn't add it to the server's event list — {ex.Reason ?? "Discord said no"}. "
                    + "The RSVP post above still works.)",
                    ephemeral: true);
            }
        }

        private static MessageComponent RsvpButtons(long eventId)
            => new ComponentBuilder()
                .WithButton("Going", $"{RsvpPrefix}:{eventId}:{RsvpResponse.Going}", ButtonStyle.Success)
                .WithButton("Maybe", $"{RsvpPrefix}:{eventId}:{RsvpResponse.Maybe}", ButtonStyle.Secondary)
                .WithButton("Can't", $"{RsvpPrefix}:{eventId}:{RsvpResponse.No}", ButtonStyle.Secondary)
                .Build();
    }
}

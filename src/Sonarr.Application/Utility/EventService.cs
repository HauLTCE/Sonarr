using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Utility;

/// <inheritdoc cref="IEventService"/>
internal sealed class EventService(
    IEventRepository events,
    ZoneResolver zones,
    ILogger<EventService> log) : IEventService
{
    /// <summary>Matches the <c>name</c> column, and Discord's own scheduled-event title cap.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Matches the <c>description</c> column.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>Upcoming events one person may hold per guild — enough for a season, not a spam wall.</summary>
    public const int MaxUpcomingPerCreator = 10;

    /// <summary>
    /// Discord refuses a scheduled event in the past, and a lead time under this is better served
    /// by just saying so in the channel.
    /// </summary>
    public static readonly TimeSpan MinLeadTime = TimeSpan.FromMinutes(10);

    /// <summary>Discord's own ceiling for a scheduled event is five years out; a year is plenty here.</summary>
    public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(365);

    public async Task<EventResult> CreateAsync(
        ulong guildId,
        ulong creatorId,
        string name,
        string when,
        string? description = null,
        ulong? pingRoleId = null,
        CancellationToken cancellationToken = default)
    {
        string title = name?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return EventResult.Rejected("An event needs a name.");
        }

        if (title.Length > MaxNameLength)
        {
            return EventResult.Rejected($"Keep the name under {MaxNameLength} characters.");
        }

        string? blurb = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (blurb?.Length > MaxDescriptionLength)
        {
            return EventResult.Rejected($"Keep the description under {MaxDescriptionLength} characters.");
        }

        int mine = await events
            .CountUpcomingByCreatorAsync((long)guildId, (long)creatorId, cancellationToken)
            .ConfigureAwait(false);
        if (mine >= MaxUpcomingPerCreator)
        {
            return EventResult.Rejected(
                $"You already have {mine} events coming up. Run one or cancel one first.");
        }

        TimeZoneInfo zone = await zones.ForUserAsync(guildId, creatorId, cancellationToken).ConfigureAwait(false);
        if (!WhenParser.TryParse(when, DateTimeOffset.UtcNow, zone, out WhenResult? parsed, out string? error))
        {
            return EventResult.Rejected(error);
        }

        // A recurring event would need a series, its own cancel semantics and a native event per
        // occurrence. Not asked for, so it is refused rather than half-supported.
        if (parsed!.Recurrence is not null)
        {
            return EventResult.Rejected("One event at a time — a repeating series isn't a thing here yet.");
        }

        TimeSpan lead = parsed.RunAt - DateTimeOffset.UtcNow;
        if (lead < MinLeadTime)
        {
            return EventResult.Rejected("That's too soon to schedule — just say it in the channel.");
        }

        if (lead > MaxLeadTime)
        {
            return EventResult.Rejected("A year out is the limit.");
        }

        SocialEvent social = new()
        {
            GuildId = (long)guildId,
            CreatorId = (long)creatorId,
            Name = title,
            Description = blurb,
            StartsAt = parsed.RunAt,
            PingRoleId = (long?)pingRoleId,
            Status = SocialEventStatus.Scheduled,
        };

        long eventId = await events.AddAsync(social, cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Event {EventId} scheduled for {StartsAt} in guild {GuildId}", eventId, parsed.RunAt, guildId);

        return EventResult.Ok(
            $"**{title}** is on for <t:{parsed.RunAt.ToUnixTimeSeconds()}:F> — `#{eventId}`.",
            eventId,
            parsed.RunAt);
    }

    public Task LinkDiscordEventAsync(
        long eventId, ulong discordEventId, CancellationToken cancellationToken = default)
        => events.LinkDiscordEventAsync(eventId, (long)discordEventId, cancellationToken);

    public async Task<IReadOnlyList<EventView>> ListAsync(
        ulong guildId, int limit = 10, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SocialEvent> upcoming = await events
            .ListUpcomingAsync((long)guildId, limit, cancellationToken)
            .ConfigureAwait(false);
        if (upcoming.Count == 0)
        {
            return [];
        }

        // One count query for the page rather than one per row.
        IReadOnlyDictionary<long, (int Going, int Maybe)> counts = await events
            .CountResponsesAsync([.. upcoming.Select(e => e.EventId)], cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. upcoming.Select(e =>
            {
                (int going, int maybe) = counts.TryGetValue(e.EventId, out (int, int) c) ? c : (0, 0);
                return new EventView(
                    e.EventId, e.Name, e.Description, e.StartsAt, e.CreatorId, e.PingRoleId, going, maybe);
            }),
        ];
    }

    public async Task<EventResult> CancelAsync(
        ulong guildId,
        ulong eventId,
        ulong requestedBy,
        bool isStaff,
        CancellationToken cancellationToken = default)
    {
        if (await events.GetAsync((long)guildId, (long)eventId, cancellationToken).ConfigureAwait(false)
            is not { } social)
        {
            return EventResult.Rejected($"No event `#{eventId}` here.");
        }

        // Ownership is checked against the row rather than trusted from the caller; staff is the
        // one thing only Discord can answer, so it arrives as a flag.
        if (!isStaff && social.CreatorId != (long)requestedBy)
        {
            return EventResult.Rejected("That's not your event.");
        }

        if (social.Status != SocialEventStatus.Scheduled)
        {
            return EventResult.Rejected($"`#{eventId}` is already {social.Status}.");
        }

        if (!await events.CancelAsync((long)guildId, (long)eventId, cancellationToken).ConfigureAwait(false))
        {
            // Lost a race with another cancel. Nothing left to do, and no second announcement.
            return EventResult.Rejected($"`#{eventId}` is already cancelled.");
        }

        log.LogInformation("Event {EventId} cancelled by {UserId} in guild {GuildId}", eventId, requestedBy, guildId);

        return EventResult.Ok($"**{social.Name}** is off.", (long)eventId, social.StartsAt);
    }

    public async Task<RsvpOutcome> RespondAsync(
        ulong guildId,
        ulong eventId,
        ulong userId,
        string response,
        CancellationToken cancellationToken = default)
    {
        if (!IsKnownResponse(response))
        {
            return new RsvpOutcome(false, "Pick going, maybe or no.");
        }

        SocialEvent? social = await events
            .RespondAsync((long)guildId, (long)eventId, (long)userId, response, cancellationToken)
            .ConfigureAwait(false);
        if (social is null)
        {
            return new RsvpOutcome(false, "That event is gone or cancelled.");
        }

        bool going = response == RsvpResponse.Going;
        string message = response switch
        {
            RsvpResponse.Going => $"You're in for **{social.Name}**.",
            RsvpResponse.Maybe => $"Noted as a maybe for **{social.Name}**.",
            _ => $"You're out for **{social.Name}**.",
        };

        // The role is the opt-in half of "opt-in role pings": going gets it, anything else drops it,
        // so nobody is pinged for something they backed out of.
        return new RsvpOutcome(true, message, social.PingRoleId, going);
    }

    /// <summary>Whitelist rather than a parse: the value goes straight into a 16-char column.</summary>
    public static bool IsKnownResponse(string? response)
        => response is RsvpResponse.Going or RsvpResponse.Maybe or RsvpResponse.No;
}

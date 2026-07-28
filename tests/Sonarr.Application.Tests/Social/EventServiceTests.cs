using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Utility;
using Sonarr.Bot.Modules;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Social;

/// <summary>
/// <c>/event create|list|cancel</c> and the RSVP path at the service boundary. The service owns the
/// row, the caps and the wording; the native Discord event and the ping role stay in the module,
/// which is why the outcome only reports which role the caller should now hold.
/// </summary>
public sealed class EventServiceTests
{
    private const ulong Guild = 800UL;
    private const ulong Creator = 801UL;
    private const ulong Member = 802UL;
    private const ulong Role = 803UL;

    /// <summary>Mirrors <c>EventService.MaxNameLength</c> — internal, so restated here.</summary>
    private const int MaxNameLength = 100;

    /// <summary>Mirrors <c>EventService.MaxUpcomingPerCreator</c>.</summary>
    private const int MaxUpcomingPerCreator = 10;

    [Fact]
    public async Task Create_stores_the_event_with_its_parsed_start()
    {
        (IEventService service, FakeEventRepository repo) = Build();

        EventResult result = await service.CreateAsync(
            Guild, Creator, "Movie night", "in 3 days", "bring snacks", Role);

        Assert.True(result.Success, result.Message);
        SocialEvent stored = Assert.Single(repo.Rows);
        Assert.Equal("Movie night", stored.Name);
        Assert.Equal("bring snacks", stored.Description);
        Assert.Equal((long)Role, stored.PingRoleId);
        Assert.Equal(SocialEventStatus.Scheduled, stored.Status);
        Assert.Equal(stored.StartsAt, result.StartsAt);

        // The caller needs the start back to mirror it onto Discord's calendar without reparsing.
        Assert.True(result.StartsAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Create_works_without_a_description_or_a_ping_role()
    {
        (IEventService service, FakeEventRepository repo) = Build();

        Assert.True((await service.CreateAsync(Guild, Creator, "Raid", "tomorrow 20:00")).Success);
        Assert.Null(repo.Rows[0].Description);
        Assert.Null(repo.Rows[0].PingRoleId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_event_needs_a_name(string name)
    {
        (IEventService service, FakeEventRepository repo) = Build();

        Assert.False((await service.CreateAsync(Guild, Creator, name, "in 3 days")).Success);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task An_oversized_name_or_description_is_refused()
    {
        (IEventService service, FakeEventRepository repo) = Build();

        Assert.False((await service.CreateAsync(
            Guild, Creator, new string('x', MaxNameLength + 1), "in 3 days")).Success);
        Assert.False((await service.CreateAsync(
            Guild, Creator, "fine", "in 3 days", new string('x', 1001))).Success);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task A_repeating_phrase_is_refused_rather_than_becoming_a_series()
    {
        (IEventService service, FakeEventRepository repo) = Build();

        EventResult result = await service.CreateAsync(Guild, Creator, "Standup", "every monday 9am");

        Assert.False(result.Success);
        Assert.Empty(repo.Rows);
    }

    [Theory]
    [InlineData("in 2 minutes")]
    [InlineData("in 5 years")]
    public async Task Starts_that_are_too_soon_or_too_far_out_are_refused(string when)
    {
        (IEventService service, FakeEventRepository repo) = Build();

        Assert.False((await service.CreateAsync(Guild, Creator, "Thing", when)).Success);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task An_unreadable_when_is_refused_without_writing_a_row()
    {
        (IEventService service, FakeEventRepository repo) = Build();

        Assert.False((await service.CreateAsync(Guild, Creator, "Thing", "at some point")).Success);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task One_person_can_only_have_so_many_upcoming_events()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        for (var i = 0; i < MaxUpcomingPerCreator; i++)
        {
            Assert.True((await service.CreateAsync(Guild, Creator, $"Thing {i}", "in 3 days")).Success);
        }

        Assert.False((await service.CreateAsync(Guild, Creator, "One more", "in 3 days")).Success);

        // Somebody else is unaffected, and so is another guild: the cap is per creator per guild.
        Assert.True((await service.CreateAsync(Guild, Member, "Mine", "in 3 days")).Success);
        Assert.True((await service.CreateAsync(Guild + 1, Creator, "Elsewhere", "in 3 days")).Success);
        Assert.Equal(MaxUpcomingPerCreator + 2, repo.Rows.Count);
    }

    [Fact]
    public async Task List_shows_upcoming_events_soonest_first_with_their_counts()
    {
        (IEventService service, _) = Build();
        EventResult later = await service.CreateAsync(Guild, Creator, "Later", "in 5 days");
        EventResult sooner = await service.CreateAsync(Guild, Creator, "Sooner", "in 2 days");

        await service.RespondAsync(Guild, (ulong)sooner.EventId, Member, RsvpResponse.Going);
        await service.RespondAsync(Guild, (ulong)sooner.EventId, Creator, RsvpResponse.Maybe);

        IReadOnlyList<EventView> upcoming = await service.ListAsync(Guild);

        Assert.Equal(["Sooner", "Later"], upcoming.Select(e => e.Name));
        Assert.Equal((1, 1), (upcoming[0].Going, upcoming[0].Maybe));
        Assert.Equal((0, 0), (upcoming[1].Going, upcoming[1].Maybe));
        Assert.Equal(later.EventId, upcoming[1].EventId);
    }

    [Fact]
    public async Task List_leaves_out_another_guilds_events_and_cancelled_ones()
    {
        (IEventService service, _) = Build();
        EventResult mine = await service.CreateAsync(Guild, Creator, "Mine", "in 2 days");
        await service.CreateAsync(Guild + 1, Creator, "Theirs", "in 2 days");
        EventResult doomed = await service.CreateAsync(Guild, Creator, "Doomed", "in 2 days");

        await service.CancelAsync(Guild, (ulong)doomed.EventId, Creator, isStaff: false);

        IReadOnlyList<EventView> upcoming = await service.ListAsync(Guild);

        Assert.Equal([mine.EventId], upcoming.Select(e => e.EventId));
    }

    [Fact]
    public async Task An_empty_calendar_lists_nothing_rather_than_throwing()
    {
        (IEventService service, _) = Build();

        Assert.Empty(await service.ListAsync(Guild));
    }

    [Fact]
    public async Task The_creator_can_cancel_their_own_event()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Movie night", "in 3 days");

        EventResult cancelled = await service.CancelAsync(Guild, (ulong)created.EventId, Creator, isStaff: false);

        Assert.True(cancelled.Success, cancelled.Message);
        Assert.Contains("Movie night", cancelled.Message, StringComparison.Ordinal);
        Assert.Equal(SocialEventStatus.Cancelled, repo.Rows[0].Status);
    }

    [Fact]
    public async Task Staff_can_cancel_somebody_elses_event_and_a_stranger_cannot()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Movie night", "in 3 days");

        Assert.False((await service.CancelAsync(Guild, (ulong)created.EventId, Member, isStaff: false)).Success);
        Assert.Equal(SocialEventStatus.Scheduled, repo.Rows[0].Status);

        Assert.True((await service.CancelAsync(Guild, (ulong)created.EventId, Member, isStaff: true)).Success);
    }

    [Fact]
    public async Task Cancelling_twice_only_announces_once()
    {
        (IEventService service, _) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Movie night", "in 3 days");

        Assert.True((await service.CancelAsync(Guild, (ulong)created.EventId, Creator, isStaff: false)).Success);
        Assert.False((await service.CancelAsync(Guild, (ulong)created.EventId, Creator, isStaff: false)).Success);
    }

    [Fact]
    public async Task Cancelling_across_guilds_or_a_missing_id_finds_nothing()
    {
        (IEventService service, _) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Movie night", "in 3 days");

        Assert.False((await service.CancelAsync(Guild + 1, (ulong)created.EventId, Creator, isStaff: true)).Success);
        Assert.False((await service.CancelAsync(Guild, 4242, Creator, isStaff: true)).Success);
    }

    [Fact]
    public async Task Going_asks_for_the_ping_role_and_backing_out_gives_it_back()
    {
        (IEventService service, _) = Build();
        EventResult created = await service.CreateAsync(
            Guild, Creator, "Raid", "in 3 days", pingRoleId: Role);

        RsvpOutcome going = await service.RespondAsync(
            Guild, (ulong)created.EventId, Member, RsvpResponse.Going);
        Assert.True(going.Success, going.Message);
        Assert.True(going.WantsRole);
        Assert.Equal((long)Role, going.PingRoleId);

        // Anything other than going drops the role, so nobody is pinged for something they left.
        foreach (string response in new[] { RsvpResponse.Maybe, RsvpResponse.No })
        {
            RsvpOutcome outcome = await service.RespondAsync(
                Guild, (ulong)created.EventId, Member, response);
            Assert.True(outcome.Success);
            Assert.False(outcome.WantsRole);
            Assert.Equal((long)Role, outcome.PingRoleId);
        }
    }

    [Fact]
    public async Task An_event_without_a_ping_role_asks_for_nothing()
    {
        (IEventService service, _) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Chill", "in 3 days");

        RsvpOutcome outcome = await service.RespondAsync(
            Guild, (ulong)created.EventId, Member, RsvpResponse.Going);

        Assert.True(outcome.Success);
        Assert.Null(outcome.PingRoleId);
    }

    [Fact]
    public async Task Changing_your_mind_updates_the_one_row_rather_than_adding_another()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Raid", "in 3 days");

        await service.RespondAsync(Guild, (ulong)created.EventId, Member, RsvpResponse.Going);
        await service.RespondAsync(Guild, (ulong)created.EventId, Member, RsvpResponse.No);

        Assert.Equal(RsvpResponse.No, Assert.Single(repo.Rsvps).Response);

        // A "no" is stored, but it is not a headcount.
        IReadOnlyList<EventView> upcoming = await service.ListAsync(Guild);
        Assert.Equal((0, 0), (upcoming[0].Going, upcoming[0].Maybe));
    }

    [Fact]
    public async Task An_unknown_response_never_reaches_the_column()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Raid", "in 3 days");

        RsvpOutcome outcome = await service.RespondAsync(Guild, (ulong)created.EventId, Member, "GOING");

        Assert.False(outcome.Success);
        Assert.Empty(repo.Rsvps);
    }

    [Fact]
    public async Task Rsvping_to_a_cancelled_missing_or_foreign_event_stores_nothing()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Raid", "in 3 days");
        await service.CancelAsync(Guild, (ulong)created.EventId, Creator, isStaff: false);

        Assert.False((await service.RespondAsync(Guild, (ulong)created.EventId, Member, RsvpResponse.Going)).Success);
        Assert.False((await service.RespondAsync(Guild, 4242, Member, RsvpResponse.Going)).Success);
        Assert.False((await service.RespondAsync(Guild + 1, (ulong)created.EventId, Member, RsvpResponse.Going)).Success);
        Assert.Empty(repo.Rsvps);
    }

    [Fact]
    public async Task Linking_the_native_event_records_its_id()
    {
        (IEventService service, FakeEventRepository repo) = Build();
        EventResult created = await service.CreateAsync(Guild, Creator, "Raid", "in 3 days");

        await service.LinkDiscordEventAsync(created.EventId, 999UL);

        Assert.Equal(999L, repo.Rows[0].DiscordEventId);
    }

    [Fact]
    public async Task The_module_splits_staff_commands_from_the_open_ones()
    {
        using DiscordRestClient rest = new();
        using InteractionService interactions = new(rest);

        (IEventService service, _) = Build();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(service)
            .BuildServiceProvider();

        ModuleInfo module = await interactions.AddModuleAsync(typeof(EventModule), services);

        // /events and the RSVP buttons are open to everyone; create/cancel sit in the guarded group.
        Assert.Contains(module.SlashCommands, c => c.Name == "events");
        Assert.Contains(module.ComponentCommands, c => c.Name.StartsWith(EventModule.RsvpPrefix, StringComparison.Ordinal));

        ModuleInfo manage = Assert.Single(module.SubModules);
        Assert.Equal("event", manage.SlashGroupName);
        Assert.Equal(["cancel", "create"], manage.SlashCommands.Select(c => c.Name).Order());
    }

    private static (IEventService Service, FakeEventRepository Events) Build()
    {
        FakeEventRepository events = new();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IEventRepository>(events);
        services.AddSingleton<IMemberRepository>(new Utility.FakeTimezoneMemberRepository());
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSonarrUtilityServices();

        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IEventService>(), events);
    }
}

/// <summary>In-memory <c>social.event</c> + <c>social.event_rsvp</c>, mirroring the WHERE clauses.</summary>
internal sealed class FakeEventRepository : IEventRepository
{
    private long _next;

    public List<SocialEvent> Rows { get; } = [];

    public List<EventRsvp> Rsvps { get; } = [];

    public Task<long> AddAsync(SocialEvent social, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(social);
        social.EventId = ++_next;
        Rows.Add(social);
        return Task.FromResult(social.EventId);
    }

    public Task<SocialEvent?> GetAsync(long guildId, long eventId, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(e => e.EventId == eventId && e.GuildId == guildId));

    public Task<IReadOnlyList<SocialEvent>> ListUpcomingAsync(
        long guildId, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SocialEvent>>(
        [
            .. Rows
                .Where(e => e.GuildId == guildId
                    && e.Status == SocialEventStatus.Scheduled
                    && e.StartsAt > DateTimeOffset.UtcNow)
                .OrderBy(e => e.StartsAt)
                .Take(Math.Max(limit, 0)),
        ]);

    public Task LinkDiscordEventAsync(long eventId, long discordEventId, CancellationToken ct = default)
    {
        if (Rows.FirstOrDefault(e => e.EventId == eventId) is { } row)
        {
            row.DiscordEventId = discordEventId;
        }

        return Task.CompletedTask;
    }

    public Task<bool> CancelAsync(long guildId, long eventId, CancellationToken ct = default)
    {
        SocialEvent? row = Rows.FirstOrDefault(
            e => e.EventId == eventId && e.GuildId == guildId && e.Status == SocialEventStatus.Scheduled);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        row.Status = SocialEventStatus.Cancelled;
        return Task.FromResult(true);
    }

    public Task<SocialEvent?> RespondAsync(
        long guildId, long eventId, long userId, string response, CancellationToken ct = default)
    {
        SocialEvent? row = Rows.FirstOrDefault(
            e => e.EventId == eventId && e.GuildId == guildId && e.Status == SocialEventStatus.Scheduled);
        if (row is null)
        {
            return Task.FromResult<SocialEvent?>(null);
        }

        if (Rsvps.FirstOrDefault(r => r.EventId == eventId && r.UserId == userId) is { } existing)
        {
            existing.Response = response;
        }
        else
        {
            Rsvps.Add(new EventRsvp { EventId = eventId, UserId = userId, Response = response });
        }

        return Task.FromResult<SocialEvent?>(row);
    }

    public Task<IReadOnlyDictionary<long, (int Going, int Maybe)>> CountResponsesAsync(
        IReadOnlyCollection<long> eventIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);

        return Task.FromResult<IReadOnlyDictionary<long, (int, int)>>(
            Rsvps
                .Where(r => eventIds.Contains(r.EventId))
                .GroupBy(r => r.EventId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        g.Count(r => r.Response == RsvpResponse.Going),
                        g.Count(r => r.Response == RsvpResponse.Maybe))));
    }

    public Task<int> CountUpcomingByCreatorAsync(long guildId, long creatorId, CancellationToken ct = default)
        => Task.FromResult(Rows.Count(
            e => e.GuildId == guildId
                && e.CreatorId == creatorId
                && e.Status == SocialEventStatus.Scheduled
                && e.StartsAt > DateTimeOffset.UtcNow));
}

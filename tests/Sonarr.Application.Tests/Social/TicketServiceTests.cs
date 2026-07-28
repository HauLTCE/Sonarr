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
/// <c>/ticket</c> at the service boundary, plus the transcript renderer. The thread, its members and
/// the attachment are the module's — what is asserted here is the row, the caps, and the fact that a
/// second close cannot post a second transcript.
/// </summary>
public sealed class TicketServiceTests
{
    private const ulong Guild = 600UL;
    private const ulong Opener = 601UL;
    private const ulong Mod = 602UL;
    private const ulong Thread = 603UL;

    [Fact]
    public async Task Recording_a_ticket_stores_the_thread_it_lives_in()
    {
        (TicketService service, FakeTicketRepository repo) = Build();

        TicketResult result = await service.RecordAsync(Guild, Opener, Thread);

        Assert.True(result.Success, result.Message);
        Ticket stored = Assert.Single(repo.Rows);
        Assert.Equal(((long)Guild, (long)Opener, (long)Thread), (stored.GuildId, stored.OpenerId, stored.ThreadId));
        Assert.Equal(TicketStatus.Open, stored.Status);
        Assert.Null(stored.TranscriptRef);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_ticket_needs_a_topic(string topic)
        => Assert.NotNull(await Build().Service.CheckAsync(Guild, Opener, topic));

    [Fact]
    public async Task An_oversized_topic_is_refused_before_a_thread_exists()
    {
        (TicketService service, FakeTicketRepository repo) = Build();

        Assert.NotNull(await service.CheckAsync(Guild, Opener, new string('x', TicketService.MaxTopicLength + 1)));
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task A_normal_topic_passes_the_check()
        => Assert.Null(await Build().Service.CheckAsync(Guild, Opener, "someone is being weird in general"));

    [Fact]
    public async Task One_person_can_only_hold_so_many_open_tickets()
    {
        (TicketService service, FakeTicketRepository repo) = Build();
        for (var i = 0; i < TicketService.MaxOpenPerUser; i++)
        {
            Assert.Null(await service.CheckAsync(Guild, Opener, "help"));
            await service.RecordAsync(Guild, Opener, Thread + (ulong)i);
        }

        Assert.NotNull(await service.CheckAsync(Guild, Opener, "help"));

        // Closing one makes room, and nobody else was affected.
        Assert.True(await service.CloseAsync(repo.Rows[0].TicketId, Mod, transcriptRef: null));
        Assert.Null(await service.CheckAsync(Guild, Opener, "help"));
        Assert.Null(await service.CheckAsync(Guild, Mod, "help"));
        Assert.Null(await service.CheckAsync(Guild + 1, Opener, "help"));
    }

    [Fact]
    public async Task A_thread_that_is_not_a_ticket_reads_as_nothing()
    {
        (TicketService service, _) = Build();

        Assert.Null(await service.GetOpenAsync(Thread));
    }

    [Fact]
    public async Task Closing_records_who_did_it_and_where_the_transcript_went()
    {
        (TicketService service, FakeTicketRepository repo) = Build();
        TicketResult opened = await service.RecordAsync(Guild, Opener, Thread);

        Assert.True(await service.CloseAsync(opened.TicketId, Mod, "https://discord.com/channels/1/2/3"));

        Ticket stored = repo.Rows[0];
        Assert.Equal(TicketStatus.Closed, stored.Status);
        Assert.Equal((long)Mod, stored.ClosedBy);
        Assert.NotNull(stored.ClosedAt);
        Assert.Equal("https://discord.com/channels/1/2/3", stored.TranscriptRef);

        // Closed tickets stop answering, which is what keeps a stale button from acting.
        Assert.Null(await service.GetOpenAsync(Thread));
    }

    [Fact]
    public async Task A_second_close_cannot_post_a_second_transcript()
    {
        (TicketService service, _) = Build();
        TicketResult opened = await service.RecordAsync(Guild, Opener, Thread);

        Assert.True(await service.CloseAsync(opened.TicketId, Mod, "first"));
        Assert.False(await service.CloseAsync(opened.TicketId, Opener, "second"));
    }

    [Fact]
    public async Task Closing_a_ticket_that_never_existed_is_just_false()
        => Assert.False(await Build().Service.CloseAsync(4242, Mod, null));

    [Fact]
    public async Task Closing_without_a_log_channel_still_closes_the_ticket()
    {
        (TicketService service, FakeTicketRepository repo) = Build();
        TicketResult opened = await service.RecordAsync(Guild, Opener, Thread);

        // No transcript ref: the mod log is unconfigured or refused the post. Trapping the thread
        // over that would be worse than losing the transcript.
        Assert.True(await service.CloseAsync(opened.TicketId, Mod, transcriptRef: null));
        Assert.Equal(TicketStatus.Closed, repo.Rows[0].Status);
        Assert.Null(repo.Rows[0].TranscriptRef);
    }

    [Theory]
    [InlineData("short one", "short one")]
    [InlineData("  padded  ", "padded")]
    public void The_thread_name_is_the_topic_tidied_up(string topic, string expected)
        => Assert.Equal(expected, TicketService.ThreadName(topic));

    [Fact]
    public void A_long_topic_is_cut_to_fit_a_thread_name()
    {
        string name = TicketService.ThreadName(new string('x', 200));

        Assert.Equal(TicketService.MaxTopicLength, name.Length);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_transcript_reads_forwards_and_says_what_it_is()
    {
        DateTimeOffset start = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
        string text = TicketTranscript.Render(
            7,
            [
                new(start, "opener", "hello"),
                new(start.AddMinutes(1), "mod", "hi"),
            ]);

        Assert.Contains("Ticket #7", text, StringComparison.Ordinal);
        Assert.Contains("2 message(s)", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("hello", StringComparison.Ordinal) < text.IndexOf("hi", StringComparison.Ordinal),
            "the transcript should read oldest-first");
    }

    [Fact]
    public void A_multiline_message_stays_one_transcript_line()
    {
        string text = TicketTranscript.Render(
            1, [new(DateTimeOffset.UtcNow, "opener", "first\nsecond\r\nthird")]);

        // Four lines of header plus exactly one message line.
        Assert.Single(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(4));
        Assert.Contains("first ⏎ second ⏎ third", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_very_long_ticket_is_truncated_and_says_so()
    {
        TranscriptLine[] lines =
        [
            .. Enumerable
                .Range(0, TicketTranscript.MaxLines + 25)
                .Select(i => new TranscriptLine(DateTimeOffset.UtcNow, "opener", $"line {i}")),
        ];

        string text = TicketTranscript.Render(1, lines);

        Assert.Contains("25 earlier message(s) not included", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"line {TicketTranscript.MaxLines + 1}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_ticket_still_renders()
        => Assert.Contains("0 message(s)", TicketTranscript.Render(1, []), StringComparison.Ordinal);

    [Fact]
    public async Task The_module_registers_the_command_and_its_close_button()
    {
        using DiscordRestClient rest = new();
        using InteractionService interactions = new(rest);

        (TicketService service, _) = Build();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(service)
            .AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService())
            .BuildServiceProvider();

        ModuleInfo module = await interactions.AddModuleAsync(typeof(TicketModule), services);

        Assert.Contains(module.SlashCommands, c => c.Name == "ticket");
        Assert.Contains(
            module.ComponentCommands,
            c => c.Name.StartsWith(TicketModule.ClosePrefix, StringComparison.Ordinal));
    }

    private static (TicketService Service, FakeTicketRepository Tickets) Build()
    {
        FakeTicketRepository tickets = new();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ITicketRepository>(tickets);
        services.AddSingleton<IMemberRepository>(new Utility.FakeTimezoneMemberRepository());
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSonarrUtilityServices();

        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<TicketService>(), tickets);
    }
}

/// <summary>In-memory <c>social.ticket</c>, mirroring the one-shot close in the WHERE clause.</summary>
internal sealed class FakeTicketRepository : ITicketRepository
{
    private long _next;

    public List<Ticket> Rows { get; } = [];

    public Task<long> AddAsync(Ticket ticket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ticket.TicketId = ++_next;
        Rows.Add(ticket);
        return Task.FromResult(ticket.TicketId);
    }

    public Task<Ticket?> GetByThreadAsync(long threadId, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(t => t.ThreadId == threadId));

    public Task<bool> CloseAsync(
        long ticketId, long closedBy, string? transcriptRef, DateTimeOffset at, CancellationToken ct = default)
    {
        Ticket? row = Rows.FirstOrDefault(t => t.TicketId == ticketId && t.Status == TicketStatus.Open);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        row.Status = TicketStatus.Closed;
        row.ClosedBy = closedBy;
        row.ClosedAt = at;
        row.TranscriptRef = transcriptRef;
        return Task.FromResult(true);
    }

    public Task<int> CountOpenByOpenerAsync(long guildId, long openerId, CancellationToken ct = default)
        => Task.FromResult(Rows.Count(
            t => t.GuildId == guildId && t.OpenerId == openerId && t.Status == TicketStatus.Open));
}

using System.Globalization;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Tests.Jobs;
using Sonarr.Bot.Modules;
using Sonarr.Application.Utility;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Jobs;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Social;

/// <summary>
/// <c>/capsule write</c> at the service boundary: two rows per capsule (the text in
/// <c>social.capsule</c>, the timing in <c>core.job</c>), never recurring, and a delivery stamp that
/// cannot be spent twice.
/// </summary>
/// <remarks>
/// Resolved through <c>AddSonarrUtilityServices</c> — <c>CapsuleService</c> is internal, and the
/// wiring is part of what is being asserted.
/// </remarks>
public sealed class CapsuleServiceTests
{
    private const ulong Guild = 900UL;
    private const ulong Channel = 901UL;
    private const ulong Author = 902UL;

    /// <summary>Mirrors <c>CapsuleService.MaxMessageLength</c> — internal, so restated here.</summary>
    private const int MaxMessageLength = 2048;

    /// <summary>Mirrors <c>CapsuleService.MaxPendingPerUser</c>.</summary>
    private const int MaxPendingPerUser = 10;

    [Fact]
    public async Task Writes_the_text_and_the_timing_as_two_rows()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, SchedulerJobRepository jobs) = Build();

        ScheduleResult result = await service.WriteAsync(
            Guild, Channel, Author, "in 30 days", "hey, future me");

        Assert.True(result.Success, result.Message);

        Capsule stored = Assert.Single(capsules.Rows);
        Assert.Equal("hey, future me", stored.Message);
        Assert.Equal(((long)Guild, (long)Channel, (long)Author), (stored.GuildId, stored.ChannelId, stored.AuthorId));
        Assert.Null(stored.DeliveredAt);

        Job job = Assert.Single(jobs.Rows);
        Assert.Equal(JobKinds.CapsuleOpen, job.Kind);
        Assert.Null(job.Recurrence);
        Assert.Equal(stored.DeliverAt, job.RunAt);

        // The payload points at the row rather than carrying the message: /privacy and forget-me
        // have to be able to reach the text, and a job payload is not a place to look.
        Assert.Equal(
            result.JobId.ToString(CultureInfo.InvariantCulture),
            job.Payload[JobKinds.CapsuleIdField]!.GetValue<string>());
        Assert.Equal(
            Author.ToString(CultureInfo.InvariantCulture),
            job.Payload[JobKinds.UserIdField]!.GetValue<string>());
        Assert.DoesNotContain("future me", job.Payload.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_confirmation_carries_the_capsule_id_not_the_job_id()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();

        ScheduleResult result = await service.WriteAsync(Guild, Channel, Author, "in 2 days", "x");

        Assert.Equal(capsules.Rows[0].CapsuleId, result.JobId);
        Assert.Contains($"#{capsules.Rows[0].CapsuleId}", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeating_phrase_is_refused_rather_than_becoming_a_subscription()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, SchedulerJobRepository jobs) = Build();

        ScheduleResult result = await service.WriteAsync(
            Guild, Channel, Author, "every monday 9am", "again and again");

        Assert.False(result.Success);
        Assert.Contains("opens once", result.Message, StringComparison.Ordinal);
        Assert.Empty(capsules.Rows);
        Assert.Empty(jobs.Rows);
    }

    [Theory]
    [InlineData("in 5 minutes")]
    [InlineData("in 59 minutes")]
    public async Task Anything_inside_an_hour_is_a_message_not_a_capsule(string when)
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, SchedulerJobRepository jobs) = Build();

        ScheduleResult result = await service.WriteAsync(Guild, Channel, Author, when, "too soon");

        Assert.False(result.Success);
        Assert.Empty(capsules.Rows);
        Assert.Empty(jobs.Rows);
    }

    [Fact]
    public async Task An_unreadable_when_is_rejected_without_writing_anything()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, SchedulerJobRepository jobs) = Build();

        ScheduleResult result = await service.WriteAsync(Guild, Channel, Author, "one day maybe", "someday");

        Assert.False(result.Success);
        Assert.Empty(capsules.Rows);
        Assert.Empty(jobs.Rows);
    }

    [Fact]
    public async Task Empty_and_oversized_messages_are_both_refused()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();

        Assert.False((await service.WriteAsync(Guild, Channel, Author, "in 2 days", "   ")).Success);
        Assert.False((await service.WriteAsync(
            Guild, Channel, Author, "in 2 days", new string('x', MaxMessageLength + 1))).Success);
        Assert.Empty(capsules.Rows);
    }

    [Fact]
    public async Task A_message_exactly_at_the_cap_still_fits_the_column()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();

        ScheduleResult result = await service.WriteAsync(
            Guild, Channel, Author, "in 2 days", new string('x', MaxMessageLength));

        Assert.True(result.Success, result.Message);
        Assert.Equal(MaxMessageLength, capsules.Rows[0].Message.Length);
    }

    [Fact]
    public async Task One_person_can_only_hold_so_many_unopened_capsules()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();

        for (var i = 0; i < MaxPendingPerUser; i++)
        {
            Assert.True((await service.WriteAsync(Guild, Channel, Author, "in 2 days", $"note {i}")).Success);
        }

        ScheduleResult tooMany = await service.WriteAsync(Guild, Channel, Author, "in 2 days", "one more");

        Assert.False(tooMany.Success);
        Assert.Equal(MaxPendingPerUser, capsules.Rows.Count);

        // Opened ones do not count against it, so the cap is a queue depth, not a lifetime quota.
        Assert.True(await service.MarkOpenedAsync(capsules.Rows[0].CapsuleId));
        Assert.True((await service.WriteAsync(Guild, Channel, Author, "in 2 days", "room again")).Success);
    }

    [Fact]
    public async Task The_cap_is_counted_per_guild()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();
        for (var i = 0; i < MaxPendingPerUser; i++)
        {
            await service.WriteAsync(Guild, Channel, Author, "in 2 days", $"note {i}");
        }

        ScheduleResult elsewhere = await service.WriteAsync(Guild + 1, Channel, Author, "in 2 days", "other server");

        Assert.True(elsewhere.Success, elsewhere.Message);
        Assert.Equal(MaxPendingPerUser + 1, capsules.Rows.Count);
    }

    [Fact]
    public async Task Read_turns_a_claimed_job_back_into_its_capsule()
    {
        (ICapsuleService service, _, SchedulerJobRepository jobs) = Build();
        await service.WriteAsync(Guild, Channel, Author, "in 2 days", "open me");

        Capsule? capsule = await service.ReadAsync(jobs.Rows[0]);

        Assert.NotNull(capsule);
        Assert.Equal("open me", capsule!.Message);
    }

    [Fact]
    public async Task Read_returns_null_for_a_row_it_cannot_use()
    {
        (ICapsuleService service, _, _) = Build();

        // Hand-edited or half-written rows: the handler completes them instead of retrying forever.
        Assert.Null(await service.ReadAsync(new Job { JobId = 1, Kind = JobKinds.CapsuleOpen }));
        Assert.Null(await service.ReadAsync(
            new Job { JobId = 2, Kind = JobKinds.CapsuleOpen, Payload = new() { ["capsule_id"] = "nope" } }));
        Assert.Null(await service.ReadAsync(
            new Job { JobId = 3, Kind = JobKinds.CapsuleOpen, Payload = new() { ["capsule_id"] = "4242" } }));
    }

    [Fact]
    public async Task An_already_opened_capsule_reads_as_nothing_to_deliver()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, SchedulerJobRepository jobs) = Build();
        await service.WriteAsync(Guild, Channel, Author, "in 2 days", "only once");

        Assert.True(await service.MarkOpenedAsync(capsules.Rows[0].CapsuleId));

        Assert.Null(await service.ReadAsync(jobs.Rows[0]));
    }

    [Fact]
    public async Task The_delivery_stamp_can_only_be_spent_once()
    {
        (ICapsuleService service, FakeCapsuleRepository capsules, _) = Build();
        await service.WriteAsync(Guild, Channel, Author, "in 2 days", "no double posts");
        long id = capsules.Rows[0].CapsuleId;

        Assert.True(await service.MarkOpenedAsync(id));

        // A job retried after a successful post finds the row stamped and stops here.
        Assert.False(await service.MarkOpenedAsync(id));
        Assert.NotNull(capsules.Rows[0].DeliveredAt);
    }

    [Fact]
    public async Task Stamping_a_capsule_that_is_gone_is_just_false()
    {
        (ICapsuleService service, _, _) = Build();

        Assert.False(await service.MarkOpenedAsync(4242));
    }

    [Fact]
    public async Task The_module_registers_write_as_a_subcommand()
    {
        using DiscordRestClient rest = new();
        using InteractionService interactions = new(rest);

        // The builder constructs the module to read its attributes, so the service has to be
        // resolvable — nothing on it is called.
        (ICapsuleService service, _, _) = Build();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(service)
            .BuildServiceProvider();

        ModuleInfo module = await interactions.AddModuleAsync(typeof(CapsuleModule), services);

        Assert.Equal("capsule", module.SlashGroupName);
        Assert.Contains(module.SlashCommands, c => c.Name == "write");
    }

    private static (ICapsuleService Service, FakeCapsuleRepository Capsules, SchedulerJobRepository Jobs) Build()
    {
        FakeCapsuleRepository capsules = new();
        SchedulerJobRepository jobs = new();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ICapsuleRepository>(capsules);
        services.AddSingleton<IJobRepository>(jobs);
        services.AddSingleton<IMemberRepository>(new Utility.FakeTimezoneMemberRepository());
        services.AddSingleton<IGuildConfigService>(new Moderation.FakeGuildConfigService());
        services.AddSonarrUtilityServices();

        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ICapsuleService>(), capsules, jobs);
    }
}

/// <summary>In-memory <c>social.capsule</c>: ids from a counter, and the same one-shot stamp.</summary>
internal sealed class FakeCapsuleRepository : ICapsuleRepository
{
    private long _next;

    public List<Capsule> Rows { get; } = [];

    public Task<long> AddAsync(Capsule capsule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        capsule.CapsuleId = ++_next;
        Rows.Add(capsule);
        return Task.FromResult(capsule.CapsuleId);
    }

    public Task<Capsule?> GetAsync(long capsuleId, CancellationToken ct = default)
        => Task.FromResult(Rows.FirstOrDefault(c => c.CapsuleId == capsuleId));

    public Task<bool> MarkDeliveredAsync(long capsuleId, DateTimeOffset at, CancellationToken ct = default)
    {
        // Mirrors the WHERE clause: only an unstamped row can be stamped.
        Capsule? row = Rows.FirstOrDefault(c => c.CapsuleId == capsuleId && c.DeliveredAt is null);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        row.DeliveredAt = at;
        return Task.FromResult(true);
    }

    public Task<int> CountPendingAsync(long guildId, long authorId, CancellationToken ct = default)
        => Task.FromResult(
            Rows.Count(c => c.GuildId == guildId && c.AuthorId == authorId && c.DeliveredAt is null));
}

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Cli;

/// <summary>
/// <c>sonarr chat -u NAME</c> — one person's side of the chat record, following live by default.
/// </summary>
/// <remarks>
/// <para>
/// This is not a channel log. What is stored is what she recorded about a person she was talking
/// to: the registers, the tier, the facts, and the episodes. A message nobody addressed her with
/// never became a row, so it cannot appear here.
/// </para>
/// <para>
/// Following is a poll, not a subscription. Postgres has LISTEN/NOTIFY and the bot does not use it,
/// so there is nothing to subscribe to; a three-second poll of the same query is cheap (indexed on
/// guild, user, happened_at) and is honest about its latency in <c>sonarr help chat</c>.
/// </para>
/// </remarks>
internal static class ChatCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        string? who = cli.Value("user", "u");
        if (string.IsNullOrWhiteSpace(who))
        {
            throw CliError.Usage("chat wants somebody: `sonarr chat -u alice`.");
        }

        ulong guildId = cli.Guild();
        if (guildId == 0)
        {
            throw CliError.Usage(
                "chat needs a server: pass --guild ID, or set SONARR_GUILD in .env. "
                + "(`sonarr guilds` lists them.)");
        }

        int days = cli.Int("days", 7);
        if (days < 1)
        {
            throw CliError.Usage("--days wants at least 1.");
        }

        int limit = cli.Int("limit", 50);
        if (limit < 1)
        {
            throw CliError.Usage("--limit wants at least 1.");
        }

        int interval = cli.Int("interval", 3);
        if (interval < 1)
        {
            throw CliError.Usage("--interval wants at least 1 second.");
        }

        using IServiceScope scope = CliHost.Scope();
        IServiceProvider services = scope.ServiceProvider;

        long userId = await ResolveAsync(services, who, (long)guildId);
        DateTimeOffset since = DateTimeOffset.UtcNow.AddDays(-days);

        IPersonRepository people = services.GetRequiredService<IPersonRepository>();
        IEpisodeRepository episodes = services.GetRequiredService<IEpisodeRepository>();

        await HeaderAsync(services, people, (long)guildId, userId, days);

        IReadOnlyList<Episode> first = await episodes.GetRecentAsync((long)guildId, userId, since, limit);
        Output.Heading("Episodes");
        if (first.Count == 0)
        {
            Console.WriteLine($"  (nothing in the last {Output.Count(days, "day", "days")})");
        }

        foreach (Episode episode in first)
        {
            Print(episode);
        }

        // --follow is the default, so the flag that matters is the one that turns it off. --follow
        // is still accepted because a person who reads the help will try typing it.
        if (cli.Has("no-follow"))
        {
            return 0;
        }

        return await FollowAsync(episodes, (long)guildId, userId, since, limit, interval, first);
    }

    /// <summary>
    /// A Discord handle or a raw user id. Digits are taken as an id — no handle is all digits, and
    /// somebody pasting a snowflake out of Discord's copy-id is the common case.
    /// </summary>
    private static async Task<long> ResolveAsync(IServiceProvider services, string who, long guildId)
    {
        string raw = who.Trim().TrimStart('@');
        if (long.TryParse(raw, CultureInfo.InvariantCulture, out long id))
        {
            return id;
        }

        long? found = await services
            .GetRequiredService<IMemberRepository>()
            .FindUserIdByUsernameAsync(raw);

        // Null is two different situations and the repository cannot tell them apart: nobody has
        // that handle, or two guilds cache the same handle for different people. Both mean "I will
        // not guess", and both are fixed the same way — pass the id.
        return found ?? throw new CliError(
            $"no member here called '{raw}'. Handles are only cached for people the bot has seen "
            + "speak — pass the user id instead if you have it.", 2);
    }

    /// <summary>
    /// Who this is, then her state, then the facts. The state is above the episodes because it is
    /// the answer to "why is she being like this", which is why anyone runs this command.
    /// </summary>
    private static async Task HeaderAsync(
        IServiceProvider services, IPersonRepository people, long guildId, long userId, int days)
    {
        Member? member = await services
            .GetRequiredService<IMemberRepository>()
            .GetAsync(guildId, userId);

        string name = member is null
            ? userId.ToString(CultureInfo.InvariantCulture)
            : $"{member.DisplayName} ({member.Username}, {userId})";

        Console.WriteLine($"{name} — last {Output.Count(days, "day", "days")}, times in {Output.Zone}");

        Person? person = await people.GetAsync(guildId, userId);
        if (person is null)
        {
            // A member row with no person row is normal: core.member is written for anyone who
            // speaks, chat.person only for someone who spoke to her.
            Console.WriteLine();
            Console.WriteLine("She has no record of talking to them — no registers, no facts.");
            return;
        }

        Output.Heading("Where she stands");
        Console.WriteLine($"  tier {person.RelationshipTier}, {person.DialogueState}, turn {person.LogicalClock}");
        if (person.AssignedNickname is { Length: > 0 } nickname)
        {
            Console.WriteLine($"  she calls them \"{nickname}\"");
        }

        Console.WriteLine();
        Output.Rows(person.Registers
            .ToDictionary()
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => (r.Key, r.Value.ToString("0.00", CultureInfo.InvariantCulture))));

        Output.Heading("Facts");
        IReadOnlyList<Fact> facts = await people.GetFactsAsync(guildId, userId);
        if (facts.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        Output.Rows(facts.Select(f => (
            f.Predicate,
            $"{f.Value}  ({f.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}, "
            + $"turn {f.LearnedAtTurn}, {Output.Local(f.LearnedAt)})")));
    }

    /// <summary>
    /// Polls for episodes newer than the newest one already printed, until Ctrl-C.
    /// </summary>
    /// <remarks>
    /// Keyed off the last <em>id</em> rather than a timestamp: two episodes inside the same turn can
    /// share <c>happened_at</c> to the microsecond, and a <c>&gt; timestamp</c> filter would drop the
    /// second one. The id is monotonic, so "anything above this" is exact.
    /// <para>
    /// Ctrl-C is caught rather than left to kill the process so the exit code is 0 — this is a tail,
    /// and stopping a tail is not a failure. Cancelling twice takes the default.
    /// </para>
    /// </remarks>
    private static async Task<int> FollowAsync(
        IEpisodeRepository episodes,
        long guildId,
        long userId,
        DateTimeOffset since,
        int limit,
        int interval,
        IReadOnlyList<Episode> printed)
    {
        using CancellationTokenSource stop = new();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = !stop.IsCancellationRequested;
            stop.Cancel();
        };

        Console.CancelKeyPress += onCancel;
        Console.WriteLine();
        Console.WriteLine($"  …following, every {interval}s. Ctrl-C to stop.");

        long lastId = printed.Count == 0 ? 0 : printed.Max(e => e.Id);

        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), stop.Token);

                IReadOnlyList<Episode> page = await episodes.GetRecentAsync(
                    guildId, userId, since, limit, stop.Token);

                foreach (Episode episode in page.Where(e => e.Id > lastId))
                {
                    Print(episode);
                    lastId = episode.Id;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C. Expected, and the only way out of the loop.
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        return 0;
    }

    /// <summary>
    /// One episode a line: time, turn, sentiment, quote. Lines rather than <see cref="Output.Rows"/>
    /// because the follow loop prints them one at a time and cannot know a column width in advance —
    /// and a tail whose columns jump when a long quote arrives is worse than one that never aligns.
    /// </summary>
    private static void Print(Episode episode) => Console.WriteLine(
        $"  {Output.Local(episode.HappenedAt)}  t{episode.Turn,-5} "
        + $"{Sentiment(episode.SentimentTag)}  {episode.Quote}");

    private static string Sentiment(string tag)
        => (string.IsNullOrWhiteSpace(tag) ? "—" : tag).PadRight(9);
}

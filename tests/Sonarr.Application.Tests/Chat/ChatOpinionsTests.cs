using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Chat;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// <c>/opinion</c>: the topic centroid picks the opinion, the registry supplies whose side you
/// took, and the messages themselves are used for nothing else.
/// </summary>
/// <remarks>
/// <see cref="FakeTextEmbedder"/> is a hashed bag-of-words, so "the room is talking about crypto"
/// is expressed here by the room literally saying crypto. That is enough to prove the wiring: the
/// real model's job is to make the word optional, not to change which pool wins.
/// </remarks>
public sealed class ChatOpinionsTests
{
    /// <summary>crypto is argued from topic_technology in stances.yaml.</summary>
    private static readonly string[] AboutCrypto = ["crypto", "crypto again", "more crypto"];

    [Fact]
    public async Task She_answers_with_the_take_for_whatever_the_room_is_discussing()
    {
        string line = await Opinions().OpinionAsync((long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.Contains(line, Lines("topic_technology"));
    }

    [Fact]
    public async Task A_room_talking_about_nothing_she_holds_gets_the_generic_take()
    {
        // Wider vectors than the default here: with 64 buckets two nonsense words share a bucket
        // with a topic label often enough that "she has no take on this" scored above the floor by
        // hash collision alone. The real embedder does not have that failure mode.
        string line = await Opinions(embedder: new FakeTextEmbedder(4096)).OpinionAsync(
            (long)Build.Guild,
            (long)Build.User,
            ["zzzz qqqq wwww xxxx", "vvvv uuuu tttt ssss", "rrrr pppp oooo nnnn"]);

        Assert.Contains(line, Lines(ChatOpinions.FallbackPool));
    }

    [Fact]
    public async Task An_empty_channel_still_gets_an_answer()
    {
        string line = await Opinions().OpinionAsync((long)Build.Guild, (long)Build.User, []);

        Assert.Contains(line, Lines(ChatOpinions.FallbackPool));
    }

    [Fact]
    public async Task A_missing_model_falls_back_rather_than_throwing()
    {
        string line = await Opinions(embedder: new FakeTextEmbedder { Fail = true })
            .OpinionAsync((long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.Contains(line, Lines(ChatOpinions.FallbackPool));
    }

    [Fact]
    public async Task The_side_you_took_is_appended_to_the_take()
    {
        FakePersonRepository people = new();
        people.SeedStance((long)Build.Guild, (long)Build.User, "crypto", agreed: true);

        string line = await Opinions(people).OpinionAsync(
            (long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.True(
            Lines(ChatOpinions.AgreedPool).Any(l => line.EndsWith(l, StringComparison.Ordinal)),
            $"expected an {ChatOpinions.AgreedPool} tail: {line}");
    }

    [Fact]
    public async Task Disagreeing_earns_the_other_tail()
    {
        FakePersonRepository people = new();
        people.SeedStance((long)Build.Guild, (long)Build.User, "crypto", agreed: false);

        string line = await Opinions(people).OpinionAsync(
            (long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.True(
            Lines(ChatOpinions.DisagreedPool).Any(l => line.EndsWith(l, StringComparison.Ordinal)),
            $"expected an {ChatOpinions.DisagreedPool} tail: {line}");
    }

    [Fact]
    public async Task A_side_taken_on_some_other_topic_is_not_dragged_in()
    {
        FakePersonRepository people = new();
        people.SeedStance((long)Build.Guild, (long)Build.User, "astrology", agreed: true);

        string line = await Opinions(people).OpinionAsync(
            (long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.Contains(line, Lines("topic_technology"));
    }

    [Fact]
    public async Task Someone_elses_side_is_never_read()
    {
        FakePersonRepository people = new();
        people.SeedStance((long)Build.Guild, userId: 999, "crypto", agreed: true);

        // docs/06: her state about another user is theirs. A standing line never leaks it.
        string line = await Opinions(people).OpinionAsync(
            (long)Build.Guild, (long)Build.User, AboutCrypto);

        Assert.Contains(line, Lines("topic_technology"));
    }

    [Fact]
    public async Task Asking_twice_gets_the_same_take()
    {
        ChatOpinions opinions = Opinions();

        Assert.Equal(
            await opinions.OpinionAsync((long)Build.Guild, (long)Build.User, AboutCrypto),
            await opinions.OpinionAsync((long)Build.Guild, (long)Build.User, AboutCrypto));
    }

    [Fact]
    public async Task A_busy_channel_costs_no_more_than_the_cap()
    {
        FakeTextEmbedder embedder = new();

        await Opinions(embedder: embedder).OpinionAsync(
            (long)Build.Guild,
            (long)Build.User,
            [.. Enumerable.Repeat("chatter", ChatOpinions.RecentMessages * 3)]);

        // The message batch is capped, and the topic labels are one per authored opinion.
        Assert.Equal(ChatOpinions.RecentMessages + Build.Graph.Stances.Count, embedder.Embedded);
    }

    [Fact]
    public void Every_authored_stance_topic_reads_as_words_a_model_can_compare()
    {
        // The topic id with underscores swapped for spaces *is* the query text, so an id nobody
        // would ever type ("pizza_v2") would quietly never match anything.
        Assert.All(Build.Graph.Stances, s => Assert.All(
            s.Topic.Replace('_', ' '),
            c => Assert.True(char.IsAsciiLetterLower(c) || c == ' ', $"{s.Topic} is not words")));
    }

    private static ChatOpinions Opinions(
        FakePersonRepository? people = null, FakeTextEmbedder? embedder = null)
        => new(
            Build.Persona(),
            embedder ?? new FakeTextEmbedder(),
            people ?? new FakePersonRepository(),
            NullLogger<ChatOpinions>.Instance);

    private static IReadOnlyList<string> Lines(string pool) => Build.Graph.Pools[pool].Lines;
}

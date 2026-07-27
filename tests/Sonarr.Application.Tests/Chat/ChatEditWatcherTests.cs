using Sonarr.Application.Chat;
using Sonarr.Domain.Configuration;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Stealth-edit detection. She only notices edits to messages she answered, inside the hour
/// <c>chat:replied</c> keeps them, and she only says something when the words actually changed.
/// </summary>
public sealed class ChatEditWatcherTests
{
    [Fact]
    public async Task An_edit_to_a_message_she_never_answered_is_ignored()
    {
        FakeSessionCache cache = new();

        ChatDecision decision = await Build.EditWatcher(cache).HandleAsync(Edit("changed my mind"));

        Assert.Equal(ChatDecision.SkipReasons.NotReplied, decision.Skipped);
        Assert.True(decision.IsSilent);
    }

    [Fact]
    public async Task A_stealth_edit_gets_called_out()
    {
        FakeSessionCache cache = new();
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));

        ChatDecision decision = await Build.EditWatcher(cache).HandleAsync(Edit("actually, something else"));

        Assert.Null(decision.Skipped);
        Assert.NotNull(decision.Text);
        Assert.True(decision.TypingDelay > TimeSpan.Zero);
    }

    [Fact]
    public async Task The_call_out_is_an_authored_line()
    {
        FakeSessionCache cache = new();
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));

        ChatDecision decision = await Build.EditWatcher(cache).HandleAsync(Edit("something else"));

        Assert.Contains(
            decision.Text,
            Build.Graph.Pools[ChatEditWatcher.ReceiptsPool].Lines);
    }

    [Fact]
    public async Task An_edit_that_changes_no_words_is_ignored()
    {
        FakeSessionCache cache = new();
        const string text = "hello";
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request(text));

        // An embed resolving or a pin fires MessageUpdated without touching the content.
        ChatDecision decision = await Build.EditWatcher(cache).HandleAsync(Edit(text));

        Assert.Equal(ChatDecision.SkipReasons.Unedited, decision.Skipped);
    }

    [Fact]
    public async Task The_same_edit_is_only_called_out_once()
    {
        FakeSessionCache cache = new();
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));
        ChatEditWatcher watcher = Build.EditWatcher(cache);

        Assert.NotNull((await watcher.HandleAsync(Edit("changed"))).Text);

        // A redelivered gateway event must not earn a second call-out.
        Assert.Equal(
            ChatDecision.SkipReasons.Unedited,
            (await watcher.HandleAsync(Edit("changed"))).Skipped);
    }

    [Fact]
    public async Task A_second_edit_earns_a_second_call_out()
    {
        FakeSessionCache cache = new();
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));
        ChatEditWatcher watcher = Build.EditWatcher(cache);

        await watcher.HandleAsync(Edit("changed once"));

        Assert.NotNull((await watcher.HandleAsync(Edit("changed twice"))).Text);
    }

    [Fact]
    public async Task The_kill_switch_silences_the_call_out()
    {
        FakeSessionCache cache = new();
        await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));

        ChatDecision decision = await Build
            .EditWatcher(cache, new FakeFeatureGate().Off(FeatureNames.Chat))
            .HandleAsync(Edit("changed"));

        Assert.Equal(ChatDecision.SkipReasons.FeatureOff, decision.Skipped);
    }

    [Fact]
    public async Task The_same_edit_draws_the_same_line_twice()
    {
        // Seeded on the message id, not the clock: a retry says the same thing.
        static async Task<string?> Once()
        {
            FakeSessionCache cache = new();
            await Build.Pipeline(cache: cache).HandleAsync(Build.Request("hello"));
            return (await Build.EditWatcher(cache).HandleAsync(Edit("changed"))).Text;
        }

        Assert.Equal(await Once(), await Once());
    }

    private static ChatEdit Edit(string text)
        => new(Build.Guild, Build.Channel, Build.User, Build.Message, text);
}

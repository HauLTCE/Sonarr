using Discord.Interactions;
using Discord.Rest;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Preconditions;
using Sonarr.Domain.Caching;

namespace Sonarr.Application.Tests.Health;

/// <summary>
/// docs/checklist.md — "Global command flood guard". The guard is global by living on
/// <see cref="SonarrModuleBase{TContext}"/>; Discord.Net's module builder reads type attributes
/// with <c>inherit: true</c>, so these tests pin that mechanism down.
/// </summary>
public sealed class CommandRateLimitTests
{
    [Fact]
    public void SonarrModuleBase_carries_the_rate_limit_precondition()
    {
        var attributes = typeof(SonarrModuleBase<SocketInteractionContext>)
            .GetCustomAttributes(typeof(RequireCommandRateLimitAttribute), inherit: false);

        Assert.Single(attributes);
    }

    [Fact]
    public void Derived_modules_inherit_the_rate_limit_precondition()
    {
        // inherit: true is exactly how ModuleClassBuilder reads module attributes.
        var attributes = typeof(ExampleModule)
            .GetCustomAttributes(typeof(RequireCommandRateLimitAttribute), inherit: true);

        Assert.Single(attributes);
    }

    [Fact]
    public void SonarrModuleBase_is_abstract_so_it_registers_no_commands_of_its_own()
        => Assert.True(typeof(SonarrModuleBase<SocketInteractionContext>).IsAbstract);

    /// <summary>
    /// The reflection tests above describe intent; this one asks the real module builder, so a
    /// Discord.Net change to how module attributes are collected fails here instead of in prod.
    /// </summary>
    [Fact]
    public async Task InteractionService_applies_the_guard_to_a_module_that_never_declares_it()
    {
        using var rest = new DiscordRestClient();
        using var interactions = new InteractionService(rest);

        var module = await interactions.AddModuleAsync(typeof(ExampleModule), services: null);

        Assert.Contains(module.Preconditions, p => p is RequireCommandRateLimitAttribute);
    }

    private sealed class ExampleModule : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("example", "Nothing — exists so the module builder has a command to attach.")]
        public Task ExampleAsync() => Task.CompletedTask;
    }
}

/// <summary>
/// The store's fail-closed contract is what a Redis outage turns into: a friendly refusal.
/// Documented here so the trade-off is visible in the test names, not just a comment.
/// </summary>
public sealed class CooldownStoreContractTests
{
    [Fact]
    public async Task Fake_store_allows_the_first_call_in_a_window()
    {
        ICooldownStore store = new FakeCooldownStore(allow: true);

        Assert.True(await store.TryAcquireCommandAsync(1UL));
    }

    [Fact]
    public async Task Fake_store_denies_when_the_window_is_held_or_redis_is_down()
    {
        ICooldownStore store = new FakeCooldownStore(allow: false);

        Assert.False(await store.TryAcquireCommandAsync(1UL));
    }

    [Fact]
    public void The_refusal_message_is_a_friendly_sentence_with_no_case_id()
    {
        // InteractionHandler shows UnmetPrecondition reasons verbatim, so this string is
        // user-facing copy: no "error", no id, no jargon.
        Assert.Equal("Slow down a moment — try that again in a few seconds.",
            RequireCommandRateLimitAttribute.TooFast);
    }

    private sealed class FakeCooldownStore(bool allow) : ICooldownStore
    {
        public Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> TryConsumeLoginAsync(string identifier, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<bool> TryConsumeVerifyAsync(string identifier, CancellationToken cancellationToken = default)
            => Task.FromResult(allow);

        public Task<int> RecordMessageHashAsync(
            ulong guildId, ulong userId, string messageHash, CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task ClearMessageHashesAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Utility;

namespace Sonarr.Application.Utility;

/// <inheritdoc cref="IWelcomeService"/>
internal sealed class WelcomeService(IGuildConfigService config) : IWelcomeService
{
    /// <summary>An account younger than this gets a dry line instead of a warm one.</summary>
    private static readonly TimeSpan FreshAccount = TimeSpan.FromDays(7);

    public async Task<WelcomePlan> OnJoinAsync(
        MemberEvent member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        IReadOnlyDictionary<string, ConfigValue> settings =
            await config.GetAllAsync(member.GuildId, cancellationToken);

        var position = member.MemberCount is { } count ? $" You're number {count}." : string.Empty;
        var age = member.AccountCreatedAt is { } created
                  && DateTimeOffset.UtcNow - created < FreshAccount
            ? " That account is days old, by the way. I'm watching."
            : string.Empty;

        return new WelcomePlan(
            Snowflake(settings, ConfigKeys.WelcomeChannel),
            Snowflake(settings, ConfigKeys.AutoroleId),
            "Someone new",
            $"{member.DisplayName} turned up.{position}{age}");
    }

    public async Task<WelcomePlan> OnLeaveAsync(
        MemberEvent member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        IReadOnlyDictionary<string, ConfigValue> settings =
            await config.GetAllAsync(member.GuildId, cancellationToken);

        var remaining = member.MemberCount is { } count ? $" {count} left in here." : string.Empty;

        // Never an autorole on the way out.
        return new WelcomePlan(
            Snowflake(settings, ConfigKeys.WelcomeChannel),
            null,
            "One less",
            $"{member.DisplayName} is gone.{remaining}");
    }

    private static ulong? Snowflake(IReadOnlyDictionary<string, ConfigValue> settings, string key)
        => settings.TryGetValue(key, out ConfigValue? value) ? value.AsSnowflake : null;
}

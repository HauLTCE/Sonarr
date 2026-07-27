namespace Sonarr.Domain.Utility;

/// <summary>
/// What the welcome flow should do for one join or leave — decided in Application from config,
/// carried out by the Bot layer. No Discord types cross the boundary
/// (docs/02-architecture.md).
/// </summary>
/// <param name="ChannelId">Where to post, or <c>null</c> when no welcome channel is configured.</param>
/// <param name="AutoroleId">Role to grant, or <c>null</c>. Only ever set for a join.</param>
/// <param name="Title">Embed title.</param>
/// <param name="Body">Embed description.</param>
public sealed record WelcomePlan(
    ulong? ChannelId,
    ulong? AutoroleId,
    string Title,
    string Body)
{
    /// <summary>Nothing to post and nothing to grant — the flow is off for this guild.</summary>
    public bool IsNoop => ChannelId is null && AutoroleId is null;
}

/// <summary>The facts the welcome flow needs about the member who joined or left.</summary>
/// <param name="MemberCount">
/// Guild size after the event, for the "you're number 42" line. <c>null</c> when the gateway
/// hasn't told us.
/// </param>
public sealed record MemberEvent(
    ulong GuildId,
    ulong UserId,
    string DisplayName,
    DateTimeOffset? AccountCreatedAt,
    int? MemberCount);

namespace Sonarr.Domain.Abstractions;

/// <summary>One channel or role, as a person reads it plus the id the API stores.</summary>
/// <param name="Id">The snowflake, as a string — config values are stored and compared as text.</param>
/// <param name="Name">No leading <c>#</c> or <c>@</c>; the caller adds the sigil it wants.</param>
public sealed record NamedEntity(string Id, string Name);

/// <param name="Channels">Text channels, in the order they appear in Discord's sidebar.</param>
/// <param name="Roles">Every role except <c>@everyone</c>, highest first.</param>
public sealed record GuildDirectory(
    IReadOnlyList<NamedEntity> Channels,
    IReadOnlyList<NamedEntity> Roles);

/// <summary>
/// Turns snowflakes into names. A setting that reports a channel id is asking the admin to go and
/// fetch something Discord already knows.
/// </summary>
/// <remarks>
/// Read-only and cache-only on purpose. The gateway runs in the same process with
/// <c>AlwaysDownloadUsers</c>, so a name is a dictionary lookup; falling back to REST would turn one
/// page of thirty moderation cases into thirty HTTP requests. Unknown resolves to null and the
/// caller falls back to the id.
/// </remarks>
public interface IGuildDirectory
{
    /// <summary>Null when the bot is not in that guild — no channels to name.</summary>
    ValueTask<GuildDirectory?> GetAsync(ulong guildId, CancellationToken ct = default);

    /// <summary>
    /// What to call this member here — their server nickname, else their display name. Null when the
    /// guild or the member is not cached; callers fall back to the id.
    /// </summary>
    /// <param name="guildId">Zero asks the bot-wide user cache, for rows that span guilds.</param>
    string? DisplayName(ulong guildId, ulong userId);

    /// <summary>That server's name, or null when she is not in it. For rows that span guilds.</summary>
    string? GuildName(ulong guildId);
}

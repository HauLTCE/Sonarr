using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord.Preconditions;

namespace Sonarr.Bot.Discord;

/// <summary>
/// Base class for every Sonarr command module. Derive from this, not from
/// <see cref="InteractionModuleBase{T}"/> directly — it carries the two conventions from
/// docs/07-commands.md#design-rules:
/// <list type="number">
/// <item>
/// the global flood guard (<see cref="RequireCommandRateLimitAttribute"/>), inherited by every
/// derived module because Discord.Net reads module type attributes with <c>inherit: true</c>;
/// </item>
/// <item>
/// ephemeral-by-default for personal replies — <see cref="RespondPersonalAsync"/> for anything
/// about one user (<c>/privacy</c>, <c>/memories</c>, <c>/relationship</c>) and
/// <see cref="RespondInvalidAsync"/> for a failed input guard. Public replies are the explicit
/// choice via <see cref="RespondPublicAsync"/>, so "who can see this" is never an accident.
/// </item>
/// </list>
/// Input validation belongs here in the controller too: run <see cref="InputGuards"/> first,
/// answer with <see cref="RespondInvalidAsync"/>, and only then call into a service.
/// </summary>
[RequireCommandRateLimit]
public abstract class SonarrModuleBase<TContext> : InteractionModuleBase<TContext>
    where TContext : class, IInteractionContext
{
    /// <summary>Anything about a single user. Ephemeral — nobody else asked.</summary>
    protected Task RespondPersonalAsync(string text) => RespondAsync(text, ephemeral: true);

    /// <summary>A failed input guard: one friendly line, ephemeral, no case id (it is not a fault).</summary>
    protected Task RespondInvalidAsync(string problem) => RespondAsync(problem, ephemeral: true);

    /// <summary>A reply the whole channel is meant to see. Deliberate, never the default.</summary>
    protected Task RespondPublicAsync(string text) => RespondAsync(text, ephemeral: false);
}

/// <summary>
/// Controller-layer input guards (docs/07-commands.md#design-rules: "input validated at the
/// controller"). Every method returns <c>null</c> when the value is fine, or one friendly
/// sentence when it is not — so a module reads:
/// <code>
/// if (InputGuards.InRange(volume, 0, 150, "volume") is { } problem)
/// {
///     await RespondInvalidAsync(problem);
///     return;
/// }
/// </code>
/// Keep the messages user-facing: what is wrong and what would work, no type names.
/// </summary>
public static class InputGuards
{
    /// <summary>
    /// A channel or role option must belong to the guild the command ran in. Discord's picker
    /// normally enforces this, but a raw id can arrive from an autocomplete or a stale component.
    /// </summary>
    /// <param name="ownerGuildId">The guild the entity belongs to (<c>channel.GuildId</c>, <c>role.Guild.Id</c>).</param>
    /// <param name="contextGuildId">The guild the command ran in (<c>Context.Guild.Id</c>).</param>
    /// <param name="what">Lower-case noun for the message, e.g. "channel" or "role".</param>
    public static string? BelongsToGuild(ulong ownerGuildId, ulong contextGuildId, string what)
        => ownerGuildId == contextGuildId
            ? null
            : $"That {what} isn't from this server — pick one from here.";

    /// <summary>Inclusive numeric range.</summary>
    public static string? InRange(long value, long min, long max, string what)
        => value >= min && value <= max
            ? null
            : $"{Capitalize(what)} has to be between {min} and {max} — you gave {value}.";

    /// <summary>
    /// Trimmed length check for free text (reasons, playlist names, quotes). Rejects
    /// whitespace-only input, which Discord happily accepts as a filled-in option.
    /// </summary>
    public static string? Length(string? value, int max, string what, int min = 1)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length < min)
        {
            return min == 1
                ? $"I need an actual {what}, not blank space."
                : $"{Capitalize(what)} needs at least {min} characters.";
        }

        return trimmed.Length <= max
            ? null
            : $"{Capitalize(what)} is too long — {max} characters max, yours is {trimmed.Length}.";
    }

    /// <summary>
    /// IANA timezone id for <c>/timezone</c> and the reminder parser. Asks the platform rather
    /// than shipping a list of ids to maintain.
    /// </summary>
    /// <param name="zone">The resolved zone when the id is valid, otherwise <c>null</c>.</param>
    /// <remarks>
    /// Deployment requirement: the container needs the OS tz database (<c>/usr/share/zoneinfo</c>,
    /// i.e. the <c>tzdata</c> package) or every IANA id is rejected here. It is independent of
    /// <c>InvariantGlobalization=true</c> on Linux, but that switch does mean a Windows dev box
    /// only resolves Windows ids — behaviour differs from prod, so trust the container.
    /// </remarks>
    public static string? TimeZone(string? id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Give me a timezone like `Asia/Ho_Chi_Minh` or `Europe/London`.";
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"I don't know the timezone `{id.Trim()}` — try an IANA name like `Asia/Ho_Chi_Minh`.";
        }
    }

    private static string Capitalize(string what)
        => what.Length == 0 ? what : char.ToUpperInvariant(what[0]) + what[1..];
}

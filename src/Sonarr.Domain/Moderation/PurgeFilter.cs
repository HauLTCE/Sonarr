namespace Sonarr.Domain.Moderation;

/// <summary>
/// What <c>/purge</c> was asked to match. <see cref="Preview"/> is the dry-run switch:
/// when it is true nothing is ever deleted (docs/07-commands.md#moderation).
/// </summary>
/// <param name="Count">How many recent messages to consider, 1–100.</param>
/// <param name="FromUserId">Only this author's messages.</param>
/// <param name="Contains">Only messages containing this text, case-insensitive.</param>
/// <param name="BotsOnly">Only bot messages.</param>
/// <param name="Preview">Dry run: report what would go, delete nothing.</param>
public sealed record PurgeFilter(
    int Count,
    ulong? FromUserId = null,
    string? Contains = null,
    bool BotsOnly = false,
    bool Preview = false)
{
    public const int MinCount = 1;

    /// <summary>Discord's bulk-delete ceiling, and its own limit on a channel history page.</summary>
    public const int MaxCount = 100;

    /// <summary>Discord refuses to bulk-delete anything older than 14 days.</summary>
    public static readonly TimeSpan BulkDeleteWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// The filter as case-context — the only purge detail that gets persisted. Counts and
    /// filter names only: the <see cref="Contains"/> needle is a mod's own input, kept because
    /// an audit trail without it cannot answer "purged what?", but no message text is stored.
    /// </summary>
    public Dictionary<string, string> ToContext(int matched, int deleted)
    {
        Dictionary<string, string> context = new()
        {
            ["requested"] = Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["matched"] = matched.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["deleted"] = deleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (FromUserId is { } author)
        {
            context["from"] = author.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Contains))
        {
            context["contains"] = Contains.Trim();
        }

        if (BotsOnly)
        {
            context["bots_only"] = "true";
        }

        return context;
    }
}

/// <summary>One candidate message, reduced to what the filter needs. No content is retained.</summary>
/// <param name="MessageId">Snowflake, used to delete and to date the message.</param>
/// <param name="AuthorId">Author snowflake.</param>
/// <param name="AuthorIsBot">Whether the author is a bot.</param>
/// <param name="Content">Message text — matched against in memory, never persisted or logged.</param>
/// <param name="CreatedAt">Timestamp, for the 14-day bulk-delete window.</param>
/// <param name="IsPinned">Pinned messages are always skipped.</param>
public readonly record struct PurgeCandidate(
    ulong MessageId,
    ulong AuthorId,
    bool AuthorIsBot,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsPinned);

/// <summary>
/// The result of evaluating a <see cref="PurgeFilter"/> against a channel page.
/// A preview run returns this with <see cref="Deleted"/> = 0 and nothing removed.
/// </summary>
/// <param name="Matched">How many messages the filter selected.</param>
/// <param name="Deleted">How many were actually removed. Always 0 for a preview.</param>
/// <param name="TooOld">Matched but outside Discord's 14-day bulk-delete window.</param>
/// <param name="Pinned">Matched-except-pinned: skipped on purpose.</param>
/// <param name="MessageIds">The ids a real run would delete, oldest first.</param>
/// <param name="AuthorBreakdown">Per-author counts, for the preview report. Ids, not names, not text.</param>
public sealed record PurgePreview(
    int Matched,
    int Deleted,
    int TooOld,
    int Pinned,
    IReadOnlyList<ulong> MessageIds,
    IReadOnlyDictionary<ulong, int> AuthorBreakdown)
{
    public static readonly PurgePreview Nothing = new(0, 0, 0, 0, [], new Dictionary<ulong, int>());

    /// <summary>True when this was a dry run: matches found, none deleted.</summary>
    public bool WasDryRun { get; init; }
}

namespace Sonarr.Domain.Configuration;

/// <summary>
/// The kill-switch catalog (docs/checklist.md — "Kill switches &amp; health"). A disabled
/// module answers "this feature is currently off" rather than vanishing, so these names are
/// user-visible in <c>/feature</c>.
/// </summary>
public static class FeatureNames
{
    public const string Chat = "chat";
    public const string Music = "music";
    public const string Levels = "levels";
    public const string Moderation = "moderation";
    public const string Social = "social";
    public const string Welcome = "welcome";
    public const string Tickets = "tickets";
    public const string Events = "events";
    public const string Reminders = "reminders";

    public static IReadOnlyList<string> All { get; } =
        [Chat, Music, Levels, Moderation, Social, Welcome, Tickets, Events, Reminders];

    public static bool IsKnown(string? feature)
        => All.Contains(feature?.Trim().ToLowerInvariant() ?? string.Empty);
}

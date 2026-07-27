namespace Sonarr.Application.Chat;

/// <summary>
/// One inbound message the adapter has already decided is addressed to her, in domain terms.
/// </summary>
/// <remarks>
/// No Discord types: the pipeline lives in <c>Sonarr.Application</c>, so the gateway handler
/// translates a <c>SocketUserMessage</c> into this and translates the decision back.
/// </remarks>
/// <param name="Addressed">
/// True when she was mentioned or the message replies to her. False messages are only
/// ring-buffer material — she does not answer ambient chat (docs/10 gate).
/// </param>
public sealed record ChatRequest(
    ulong GuildId,
    ulong ChannelId,
    ulong UserId,
    ulong MessageId,
    string Text,
    bool Addressed);

/// <summary>An edit to a message she already replied to, in domain terms.</summary>
/// <remarks>
/// Only the message id and the new text: whether she cares is a Redis lookup
/// (<c>chat:replied</c>), not a Discord one, so an edit outside the window costs one round trip.
/// </remarks>
public sealed record ChatEdit(
    ulong GuildId,
    ulong ChannelId,
    ulong UserId,
    ulong MessageId,
    string Text);

/// <summary>What the pipeline decided. <see cref="Silent"/> means say nothing at all.</summary>
/// <param name="Text">The reply, or null.</param>
/// <param name="Reaction">An emoji to react with, or null.</param>
/// <param name="TypingDelay">How long to show the typing indicator before posting.</param>
/// <param name="StateHash">
/// Hash of the text she replied to, to store under <c>chat:replied</c> so the edit watcher can
/// tell a stealth edit from an unchanged message. Null when she stayed silent.
/// </param>
/// <param name="Skipped">Why she said nothing, for logs and metrics. Null when she replied.</param>
public sealed record ChatDecision(
    string? Text,
    string? Reaction,
    TimeSpan TypingDelay,
    string? StateHash,
    string? Skipped)
{
    public static readonly ChatDecision NotAddressed = Skip(SkipReasons.NotAddressed);

    public bool IsSilent => Text is null && Reaction is null;

    public static ChatDecision Skip(string reason) => new(null, null, TimeSpan.Zero, null, reason);

    /// <summary>Skip reasons, as stable strings so metrics and logs agree.</summary>
    public static class SkipReasons
    {
        public const string NotAddressed = "not_addressed";
        public const string FeatureOff = "feature_off";
        public const string BudgetSpent = "budget_spent";
        public const string NothingToSay = "nothing_to_say";
        public const string NotReplied = "not_replied";
        public const string Unedited = "unedited";
    }
}

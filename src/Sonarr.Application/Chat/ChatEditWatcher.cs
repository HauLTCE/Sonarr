using System.Globalization;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Chat;

/// <summary>
/// Stealth-edit detection (docs/10): someone edits a message she already answered, and she says so.
/// </summary>
/// <remarks>
/// <para>Cheap on purpose. The only state is the content hash <see cref="ChatPipeline"/> already
/// stored under <c>chat:replied</c> for an hour, so an edit outside that window is one Redis miss
/// and nothing else — no database read, no turn, no register movement.</para>
/// <para>It deliberately does not persist anything. A call-out is a remark, not a memory: the
/// receipts line is drawn from the persona and the replied hash is advanced so the same edit is
/// only ever called out once.</para>
/// </remarks>
public sealed class ChatEditWatcher(
    PersonaHolder persona,
    ISessionCache cache,
    IFeatureGate features,
    ILogger<ChatEditWatcher> log)
{
    /// <summary>The authored pool the call-out is drawn from ("nice try editing that. too slow.").</summary>
    public const string ReceiptsPool = "user_receipts";

    /// <summary>Purpose string for the RNG draw — one namespace per pool, per the engine's contract.</summary>
    private const string Purpose = "edit_callout";

    public async Task<ChatDecision> HandleAsync(ChatEdit edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        string? replied = await cache
            .GetRepliedStateHashAsync(edit.ChannelId, edit.MessageId, ct)
            .ConfigureAwait(false);

        // She only notices edits to messages she answered, inside the window she remembers them.
        if (replied is null)
        {
            return ChatDecision.Skip(ChatDecision.SkipReasons.NotReplied);
        }

        Normalized normalized = Normalizer.Normalize(edit.Text);
        string current = StableHash.Of(normalized.Lower).ToString(CultureInfo.InvariantCulture);
        if (current == replied)
        {
            // An embed resolving or a pin counts as an edit to the gateway but changes no words.
            return ChatDecision.Skip(ChatDecision.SkipReasons.Unedited);
        }

        if (!await features.IsEnabledAsync(FeatureNames.Chat, edit.GuildId, ct).ConfigureAwait(false))
        {
            return ChatDecision.Skip(ChatDecision.SkipReasons.FeatureOff);
        }

        PersonaGraph graph = persona.Current;

        // Seeded on the message, not the clock: the same edit draws the same line on a retry.
        TurnSeededRandom rng = new((long)edit.MessageId, StableHash.Of(nameof(ChatEditWatcher)));
        string? line = new LinePicker(graph).Pick(ReceiptsPool, null, rng, EmptySlots);
        if (line is null)
        {
            log.LogWarning("Edit call-out skipped: pool {Pool} produced no line", ReceiptsPool);
            return ChatDecision.Skip(ChatDecision.SkipReasons.NothingToSay);
        }

        // Advance the stored hash so the next edit of the same message is a fresh call-out and a
        // redelivered gateway event is not.
        await cache.MarkRepliedAsync(edit.ChannelId, edit.MessageId, current, ct).ConfigureAwait(false);

        return new ChatDecision(line, null, ChatPipeline.TypingDelayFor(line), current, null);
    }

    private static readonly IReadOnlyDictionary<string, string> EmptySlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

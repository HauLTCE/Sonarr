namespace Sonarr.Elaine.Matching;

/// <summary>Style signals detected on the raw message. Flags, because they combine.</summary>
[Flags]
public enum TextStyle
{
    None = 0,

    /// <summary>Mostly uppercase letters — shouting.</summary>
    Caps = 1 << 0,

    /// <summary>Long enough that "I'm not reading all that" is the honest reaction.</summary>
    WallOfText = 1 << 1,

    /// <summary>Enough emoji that they are the message.</summary>
    EmojiFlood = 1 << 2,

    /// <summary>A character stretched three or more times (sooo, ahhh, !!!).</summary>
    Elongated = 1 << 3,

    /// <summary>Contains a link.</summary>
    Link = 1 << 4,

    /// <summary>Contains an @mention.</summary>
    Mention = 1 << 5,

    /// <summary>Nothing left after normalization.</summary>
    Empty = 1 << 6,
}

/// <summary>
/// A normalized message: one canonical cased string plus its length-preserving lowering.
/// </summary>
/// <remarks>
/// <see cref="Cased"/> and <see cref="Lower"/> are index-aligned character for character,
/// so a span found on <see cref="Lower"/> slices the correctly-cased substring out of
/// <see cref="Cased"/>. This is why the engine can match case-insensitively and still
/// echo a name back as the user typed it. Never lower the whole string with
/// <c>ToLowerInvariant</c> on this path — some characters change length when lowered and
/// every capture offset downstream would silently shift.
/// </remarks>
public sealed record Normalized
{
    public required string Cased { get; init; }

    public required string Lower { get; init; }

    /// <summary>Punctuation-stripped lowercase tokens. Keyword matching works on these.</summary>
    public required IReadOnlyList<string> Tokens { get; init; }

    public required TextStyle Style { get; init; }

    /// <summary>Emoji count, kept for pools that react to the amount.</summary>
    public required int EmojiCount { get; init; }

    public bool Has(TextStyle style) => (Style & style) != 0;
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// Raw Discord text → <see cref="Normalized"/>. Pure, allocation-light, no configuration.
/// </summary>
public static partial class Normalizer
{
    /// <summary>Characters stripped from the end of the whole message.</summary>
    private const string TrailingPunctuation = ".!?,;:";

    /// <summary>Stripped from the ends of each token; interior kept, so "don't" survives.</summary>
    /// <remarks>
    /// Ends with <see cref="CustomEmojiPlaceholder"/>. Keyword matching runs on the tokens, and a
    /// custom emoji is not a word anyone typed — leaving it in would have made "nice :kekw: shot"
    /// three tokens instead of two, and given every keyword list a token it can never match.
    /// A token that is only the placeholder trims to empty and is dropped, which loses nothing:
    /// <see cref="Normalized.Cased"/> still holds it, so the style detectors and the regex
    /// patterns — the two things that are supposed to see an emoji — still do.
    /// </remarks>
    private const string TokenTrim = ".,!?;:\"'()[]{}<>*_~`" + CustomEmojiPlaceholder;

    /// <summary>Character count above which a message is a wall of text.</summary>
    public const int WallOfTextChars = 170;

    /// <summary>Emoji count at which emoji become the message.</summary>
    public const int EmojiFloodCount = 4;

    private const int CapsMinLetters = 4;
    private const double CapsMinRatio = 0.7;

    /// <summary>
    /// What a custom emoji collapses to: U+FFFC OBJECT REPLACEMENT CHARACTER, one per emoji.
    /// </summary>
    /// <remarks>
    /// This was a space, which deleted the emoji outright — and a message that is nothing but
    /// custom emoji then had no text at all, so it took <c>ChatEngine</c>'s empty path and drew
    /// "nothing to say? then don't speak." and "i'm an AI, not a mind reader." from
    /// <c>empty_message</c>. Five corpus rows: she told people who had sent her a reaction that
    /// they had sent her nothing. A custom emoji is content — <c>emoji_react</c> ("an emoji.
    /// groundbreaking.", "i don't speak hieroglyphics.") is what answers it, and the persona
    /// already routes there.
    ///
    /// U+FFFC and not some ordinary character because its Unicode category is <c>So</c>, the same
    /// as a native emoji's. That one property carries the whole fix: <see cref="CountEmoji"/>
    /// counts it, so four custom emoji flood exactly like four native ones; SINGLE_EMOJI's
    /// <c>\p{So}</c> class matches it, so one lands on <c>emoji_react</c>; and the text is no
    /// longer empty, so the empty path is not taken. It is also not a letter, so
    /// <see cref="IsShouting"/> ignores it, and it is in neither <see cref="TokenTrim"/> nor
    /// <see cref="TrailingPunctuation"/>, so nothing strips it back out.
    ///
    /// One char, so the cased/lower index alignment <see cref="Normalized"/> depends on survives.
    /// It never reaches a reply: pools are authored text, and this exists only in matcher input.
    /// </remarks>
    private const string CustomEmojiPlaceholder = "￼";

    public static Normalized Normalize(string? text)
    {
        string cased = Collapse(
            CustomEmojiRegex().Replace(text ?? string.Empty, CustomEmojiPlaceholder));
        cased = cased.TrimEnd(TrailingPunctuation.ToCharArray()).TrimEnd();
        string lower = LowerPreservingLength(cased);

        TextStyle style = TextStyle.None;
        if (cased.Length == 0)
        {
            style |= TextStyle.Empty;
        }

        if (IsShouting(cased))
        {
            style |= TextStyle.Caps;
        }

        if (cased.Length >= WallOfTextChars)
        {
            style |= TextStyle.WallOfText;
        }

        int emoji = CountEmoji(cased);
        if (emoji >= EmojiFloodCount)
        {
            style |= TextStyle.EmojiFlood;
        }

        if (ElongationRegex().IsMatch(lower))
        {
            style |= TextStyle.Elongated;
        }

        if (LinkRegex().IsMatch(lower))
        {
            style |= TextStyle.Link;
        }

        if (MentionRegex().IsMatch(lower))
        {
            style |= TextStyle.Mention;
        }

        return new Normalized
        {
            Cased = cased,
            Lower = lower,
            Tokens = Tokenize(lower),
            Style = style,
            EmojiCount = emoji,
        };
    }

    /// <summary>
    /// Trims, collapses internal whitespace, and folds typographic punctuation to ASCII.
    /// </summary>
    /// <remarks>
    /// Every substitution is strictly one character for one character, so the
    /// cased/lower index alignment survives. Collapsing happens before lowering for the
    /// same reason.
    /// </remarks>
    private static string Collapse(string text)
    {
        StringBuilder sb = new(text.Length);
        bool pendingSpace = false;
        foreach (char raw in text)
        {
            char c = Unify(raw);
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static char Unify(char c) => c switch
    {
        '“' or '”' or '„' or '‟' or '″' => '"',
        '‘' or '’' or '‚' or '‛' or '′' => '\'',
        '–' or '—' or '―' or '−' => '-',
        ' ' or ' ' or ' ' or '​' => ' ',
        _ => c,
    };

    /// <summary>
    /// Per-character lowering that keeps the original char whenever lowering it would not
    /// produce exactly one char, guaranteeing equal lengths.
    /// </summary>
    private static string LowerPreservingLength(string s)
    {
        return string.Create(s.Length, s, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char lc = char.ToLowerInvariant(c);
                span[i] = char.IsHighSurrogate(c) || char.IsLowSurrogate(c) ? c : lc;
            }
        });
    }

    private static bool IsShouting(string cased)
    {
        int letters = 0;
        int upper = 0;
        foreach (char c in cased)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;
            if (char.IsUpper(c))
            {
                upper++;
            }
        }

        return letters >= CapsMinLetters && (double)upper / letters >= CapsMinRatio;
    }

    private static List<string> Tokenize(string lower)
    {
        List<string> tokens = [];
        foreach (Range r in lower.AsSpan().Split(' '))
        {
            ReadOnlySpan<char> token = lower.AsSpan()[r].Trim(TokenTrim);
            if (!token.IsEmpty)
            {
                tokens.Add(token.ToString());
            }
        }

        return tokens;
    }

    /// <summary>
    /// Counts emoji and pictographic symbols by rune, so surrogate pairs count once.
    /// </summary>
    private static int CountEmoji(string cased)
    {
        int count = 0;
        foreach (Rune rune in cased.EnumerateRunes())
        {
            int v = rune.Value;
            bool pictographic =
                (v >= 0x1F000 && v <= 0x1FAFF)
                || (v >= 0x2600 && v <= 0x27BF)
                || (v >= 0x2B00 && v <= 0x2BFF)
                || (v >= 0x1F1E6 && v <= 0x1F1FF)
                || Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol;
            if (pictographic)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Any non-digit character three times running: "heyyy", "=))) ", "!!!".</summary>
    /// <remarks>
    /// Digits are excluded because a repeated digit is a value, not stretched typing. This was
    /// <c>(.)\1\1</c>, so the "000" in "…resistor for each B-C-E pins are all 1000 ohms, find the
    /// E output as Vcc is 12V" made a circuits question elongated, and she answered a homework
    /// problem with "that's a lot of extra letters." Refusing the homework is in voice;
    /// misdescribing it is not.
    /// </remarks>
    [GeneratedRegex(@"(\D)\1\1", RegexOptions.CultureInvariant)]
    private static partial Regex ElongationRegex();

    /// <summary>
    /// A Discord custom emoji, <c>&lt;:name:id&gt;</c> or animated <c>&lt;a:name:id&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Folded before anything else looks at the text, because the snowflake is 18 digits and
    /// repeats: <c>&lt;:pinecone_dumb:1492441226148843560&gt;</c> ends in "555", which trips the
    /// elongation detector, so a single emoji drew "stretching it out doesn't make it
    /// interesting." — she scolded someone for padding a message with no padding in it. Every
    /// other detector had the same exposure: the name and id are markup, not something anyone
    /// typed as words.
    /// </remarks>
    [GeneratedRegex(@"<a?:\w+:\d+>", RegexOptions.CultureInvariant)]
    private static partial Regex CustomEmojiRegex();

    [GeneratedRegex(@"https?://|www\.\w|\w+\.(?:com|net|org|io|gg)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<@!?\d+>|@\w+", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();
}

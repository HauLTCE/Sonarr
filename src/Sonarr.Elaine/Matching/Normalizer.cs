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
    private const string TokenTrim = ".,!?;:\"'()[]{}<>*_~`";

    /// <summary>Character count above which a message is a wall of text.</summary>
    public const int WallOfTextChars = 170;

    /// <summary>Emoji count at which emoji become the message.</summary>
    public const int EmojiFloodCount = 4;

    private const int CapsMinLetters = 4;
    private const double CapsMinRatio = 0.7;

    public static Normalized Normalize(string? text)
    {
        string cased = Collapse(text ?? string.Empty);
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

    [GeneratedRegex(@"(.)\1\1", RegexOptions.CultureInvariant)]
    private static partial Regex ElongationRegex();

    [GeneratedRegex(@"https?://|www\.\w|\w+\.(?:com|net|org|io|gg)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<@!?\d+>|@\w+", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();
}

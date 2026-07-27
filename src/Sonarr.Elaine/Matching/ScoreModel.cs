using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// The one place that decides how specific a pattern is. Both the matcher and the
/// validator's shadowing report use it, so the report can never disagree with runtime.
/// </summary>
/// <remarks>
/// Design note (docs/10): the old engine took the first transition whose matcher held, so
/// a broad <c>keyword: [mom]</c> declared above <c>regex: "your mom"</c> permanently ate
/// the specific one. Here specificity is a number, order only breaks exact ties, and the
/// validator reports anything that still cannot win.
/// </remarks>
public static class ScoreModel
{
    /// <summary>Added when the intent's topic is the live conversation topic.</summary>
    public const double TopicAffinity = 1.5;

    /// <summary>Added per additional matching pattern in the same intent (corroboration).</summary>
    public const double CorroborationStep = 0.25;

    /// <summary>Score a winner must clear, or the semantic tier gets a shot at rescuing.</summary>
    public const double LexicalThreshold = 1.0;

    /// <summary>How specific one pattern is, before match-length and guard adjustments.</summary>
    public static double Specificity(LexicalPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return pattern.Kind switch
        {
            // Fuzzy is the loosest thing we have: it matches words nobody wrote.
            MatchKind.Fuzzy => 0.5,

            // One word out of a bag. The baseline.
            MatchKind.Keyword => 1.0,

            // Every word must be present, so each extra word narrows it further.
            MatchKind.AllKeywords => 1.0 + Math.Max(0, pattern.Words.Count - 1),

            // How a message was typed is the weakest signal there is: a shouted question is
            // still a question, so any content match must outrank the style reaction.
            MatchKind.Style => 0.4,

            // A regex encodes word order and context, so it always outranks a bare keyword;
            // longer literal content narrows it further.
            MatchKind.Regex => 2.0 + (0.1 * Math.Min(20, LiteralLength(pattern.RegexSource))),

            _ => 0.0,
        };
    }

    /// <summary>Extra credit for how much of the message the pattern actually explained.</summary>
    public static double MatchLengthBonus(int matchedLength) =>
        0.02 * Math.Min(50, Math.Max(0, matchedLength));

    /// <summary>
    /// Literal characters a match is <i>guaranteed</i> to pin down: the shortest branch of
    /// every alternation, with optional elements excluded.
    /// </summary>
    /// <remarks>
    /// Guaranteed, not total, because a catch-all like <c>^(?:what|who|how|why|when)\b</c> has
    /// plenty of literal text but commits to almost nothing — summing its branches would let it
    /// outrank <c>what are you</c> and eat every real question.
    /// </remarks>
    private static int LiteralLength(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return 0;
        }

        int at = 0;
        return Alternation(source, ref at);
    }

    /// <summary>Shortest branch, stopping at an unbalanced <c>)</c> or the end of input.</summary>
    private static int Alternation(string source, ref int at)
    {
        int best = int.MaxValue;
        while (true)
        {
            best = Math.Min(best, Sequence(source, ref at));
            if (at < source.Length && source[at] == '|')
            {
                at++;
                continue;
            }

            return best == int.MaxValue ? 0 : best;
        }
    }

    private static int Sequence(string source, ref int at)
    {
        int total = 0;
        int last = 0;
        while (at < source.Length)
        {
            char c = source[at];
            if (c is '|' or ')')
            {
                if (c == ')')
                {
                    at++;
                }

                break;
            }

            switch (c)
            {
                case '\\':
                    // Escapes are either anchors (\b) or classes (\d) — no guaranteed literal.
                    at += at + 1 < source.Length ? 2 : 1;
                    last = 0;
                    break;

                case '(':
                    at++;
                    SkipGroupPrefix(source, ref at);
                    last = Alternation(source, ref at);
                    total += last;
                    break;

                case '[':
                    SkipClass(source, ref at);
                    last = 0;
                    break;

                case '{':
                    SkipTo(source, ref at, '}');
                    break;

                case '?':
                case '*':
                    // The preceding element is optional, so it guarantees nothing.
                    total -= last;
                    last = 0;
                    at++;
                    break;

                case '+':
                    at++;
                    break;

                default:
                    at++;
                    last = char.IsLetterOrDigit(c) || c is '\'' or '-' ? 1 : 0;
                    total += last;
                    break;
            }
        }

        return Math.Max(0, total);
    }

    /// <summary>Steps past <c>?:</c>, <c>?&lt;name&gt;</c>, <c>?=</c> and friends.</summary>
    private static void SkipGroupPrefix(string source, ref int at)
    {
        if (at >= source.Length || source[at] != '?')
        {
            return;
        }

        at++;
        if (at < source.Length && source[at] is '<' or 'P' or '\'')
        {
            SkipTo(source, ref at, source[at] == '\'' ? '\'' : '>');
            return;
        }

        while (at < source.Length && source[at] is ':' or '=' or '!' or 'i' or 'm' or 's' or 'x' or '-')
        {
            bool done = source[at] is ':' or '=' or '!';
            at++;
            if (done)
            {
                return;
            }
        }
    }

    private static void SkipClass(string source, ref int at)
    {
        at++;
        while (at < source.Length && source[at] != ']')
        {
            at += source[at] == '\\' ? 2 : 1;
        }

        if (at < source.Length)
        {
            at++;
        }
    }

    private static void SkipTo(string source, ref int at, char terminator)
    {
        while (at < source.Length && source[at] != terminator)
        {
            at++;
        }

        if (at < source.Length)
        {
            at++;
        }
    }
}

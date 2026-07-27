using System.Text.RegularExpressions;
using Sonarr.Elaine.Matching;

namespace Sonarr.Elaine.Persona;

public static partial class PersonaValidator
{
    /// <summary>
    /// The shadowing report: intents that can never win because a broader intent covers
    /// every input they match and outscores them (or ties and was declared first).
    /// </summary>
    /// <remarks>
    /// Reported as warnings — a shadowed intent is dead authored text, not a crash, so she
    /// still boots. CI treats any finding here as a merge blocker (docs/10 Testing).
    /// <para>
    /// Subsumption is decided syntactically and <em>conservatively</em>: a pair is only
    /// reported when every pattern of the narrow intent is provably covered by some pattern
    /// of the broad one. The match-length bonus is left out of the comparison because it is
    /// input-dependent, and in every subsumption shape below the broad intent matches at
    /// least as much text as the narrow one — so omitting it can only ever cause a missed
    /// report, never a false one.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PersonaIssue> ShadowingReport(PersonaGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<PersonaIssue> issues = [];

        foreach (IntentDef narrow in graph.Intents)
        {
            if (narrow.Patterns.Count == 0)
            {
                continue;
            }

            foreach (IntentDef broad in graph.Intents)
            {
                if (ReferenceEquals(broad, narrow) || broad.Patterns.Count == 0)
                {
                    continue;
                }

                if (!Covers(broad, narrow) || !OutscoresAlways(broad, narrow) || !SharesActivities(graph, broad, narrow))
                {
                    continue;
                }

                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Warning, Rules.Shadowed,
                    $"intent '{narrow.Id}' can never win: '{broad.Id}' matches everything it "
                    + $"matches and scores {BestScore(broad):0.##} >= {BestScore(narrow):0.##}"
                    + (BestScore(broad) == BestScore(narrow) ? " (tie broken by declaration order)" : ""),
                    narrow.Location));
                break;
            }
        }

        return issues;
    }

    /// <summary>Highest score the intent can reach on a perfect input, guards included.</summary>
    private static double BestScore(IntentDef intent) =>
        MaxSpecificity(intent) + intent.Guards.Sum(g => g.Bonus)
        + (intent.Topic is null ? 0 : ScoreModel.TopicAffinity);

    /// <summary>Lowest score the intent can reach when it does match: no guards, no topic.</summary>
    private static double WorstScore(IntentDef intent) => MinSpecificity(intent);

    private static double MaxSpecificity(IntentDef intent) =>
        intent.Specificity ?? intent.Patterns.Max(ScoreModel.Specificity);

    private static double MinSpecificity(IntentDef intent) =>
        intent.Specificity ?? intent.Patterns.Min(ScoreModel.Specificity);

    /// <summary>
    /// True when the broad intent beats the narrow one even in the broad intent's worst
    /// case and the narrow one's best case. Equal scores count, because the tie-break is
    /// declaration order and the earlier declaration then always wins.
    /// </summary>
    private static bool OutscoresAlways(IntentDef broad, IntentDef narrow)
    {
        double b = WorstScore(broad);
        double n = BestScore(narrow);
        return b > n || (b == n && broad.DeclarationIndex < narrow.DeclarationIndex);
    }

    /// <summary>The broad intent must be eligible everywhere the narrow one is.</summary>
    private static bool SharesActivities(PersonaGraph graph, IntentDef broad, IntentDef narrow) =>
        graph.Root.Activities
            .Where(a => a.Allows(narrow.Id))
            .All(a => a.Allows(broad.Id));

    /// <summary>Every pattern of <paramref name="narrow"/> is covered by one of the broad's.</summary>
    private static bool Covers(IntentDef broad, IntentDef narrow) =>
        narrow.Patterns.All(n => broad.Patterns.Any(b => Subsumes(b, n)));

    private static bool Subsumes(LexicalPattern broad, LexicalPattern narrow)
    {
        // Identical patterns: trivially mutual, and worth reporting as a duplicate.
        if (broad.Kind == narrow.Kind
            && broad.RegexSource == narrow.RegexSource
            && SameWords(broad.Words, narrow.Words))
        {
            return true;
        }

        return (broad.Kind, narrow.Kind) switch
        {
            // A bag of words covers a smaller bag of the same words.
            (MatchKind.Keyword, MatchKind.Keyword) or (MatchKind.Keyword, MatchKind.Fuzzy) =>
                narrow.Words.All(w => broad.Words.Contains(w, StringComparer.Ordinal)),

            // "all of these" implies each one, so a keyword bag sharing any required word
            // fires on every input the all-keywords pattern fires on.
            (MatchKind.Keyword, MatchKind.AllKeywords) =>
                broad.Words.Any(w => narrow.Words.Contains(w, StringComparer.Ordinal)),

            // A keyword the regex is guaranteed to contain.
            (MatchKind.Keyword, MatchKind.Regex) =>
                MandatoryWords(narrow.RegexSource).Any(w => broad.Words.Contains(w, StringComparer.Ordinal)),

            // A fuzzy bag covers a keyword bag whose every word is within edit distance.
            (MatchKind.Fuzzy, MatchKind.Keyword) =>
                narrow.Words.All(n => broad.Words.Any(b => Levenshtein(n, b, broad.MaxDistance) <= broad.MaxDistance)),

            // An unanchored regex that already matches each keyword on its own will match
            // any message containing it.
            (MatchKind.Regex, MatchKind.Keyword) or (MatchKind.Regex, MatchKind.Fuzzy) =>
                IsUnanchored(narrow.Kind == MatchKind.Fuzzy ? null : broad.RegexSource)
                && narrow.Words.All(w => SafeMatch(broad.Regex, w)),

            _ => false,
        };
    }

    private static bool SameWords(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.SequenceEqual(b, StringComparer.Ordinal);

    private static bool IsUnanchored(string? source) =>
        source is not null && !source.Contains('^', StringComparison.Ordinal)
        && !source.Contains('$', StringComparison.Ordinal);

    private static bool SafeMatch(Regex? regex, string input)
    {
        if (regex is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern too slow to probe is a pattern we refuse to claim anything about.
            return false;
        }
    }

    /// <summary>
    /// Literal words a regex must contain in every match: those outside any group and not
    /// made optional. Returns nothing when a top-level alternation makes all bets off.
    /// </summary>
    private static IReadOnlyList<string> MandatoryWords(string? source)
    {
        if (string.IsNullOrEmpty(source) || source.Contains('|', StringComparison.Ordinal))
        {
            return [];
        }

        List<string> words = [];
        System.Text.StringBuilder current = new();
        int depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\\')
            {
                Flush(words, current, optional: false);
                i++;
                continue;
            }

            if (c is '(' or '[')
            {
                Flush(words, current, optional: false);
                depth++;
                continue;
            }

            if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || c == '\'')
            {
                current.Append(c);
                continue;
            }

            // A quantifier applies to the character before it, so that char is optional and
            // the word it ends is not guaranteed intact.
            Flush(words, current, optional: c is '?' or '*');
        }

        Flush(words, current, optional: false);
        return words;
    }

    private static void Flush(List<string> into, System.Text.StringBuilder current, bool optional)
    {
        if (current.Length > 0 && !optional)
        {
            into.Add(current.ToString());
        }

        current.Clear();
    }

    /// <summary>Edit distance with an early exit once the best possible result exceeds cap.</summary>
    internal static int Levenshtein(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap)
        {
            return cap + 1;
        }

        int[] prev = new int[b.Length + 1];
        int[] cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }

            if (rowMin > cap)
            {
                return cap + 1;
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// Runs every eligible pattern of every eligible intent and ranks the results by score.
/// </summary>
/// <remarks>
/// This is the explicit fix for the old engine's first-match-wins shadowing bugs: there is
/// no early exit anywhere in here. Declaration order is consulted only to break an exact
/// score tie, so a broad keyword declared first can no longer eat a specific regex.
/// </remarks>
public sealed class LexicalMatcher(PersonaGraph persona)
{
    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    public MatchOutcome Match(Normalized input, MatchContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        if (input.Has(TextStyle.Empty))
        {
            return MatchOutcome.None;
        }

        ActivityDef? activity = _persona.Root.Activities
            .FirstOrDefault(a => a.Id == context.ActivityId);

        List<MatchCandidate> candidates = [];
        foreach (IntentDef intent in _persona.Intents)
        {
            if (activity is not null && !activity.Allows(intent.Id))
            {
                continue;
            }

            MatchCandidate? candidate = Evaluate(intent, input, context);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0
                ? byScore
                : a.Intent.DeclarationIndex.CompareTo(b.Intent.DeclarationIndex);
        });

        // A preface may score highest, but when the message has a substantive match that is the
        // answer. Keep the score order otherwise; the displaced preface rides as a side effect.
        if (candidates.Count > 1 && candidates[0].Intent.SideEffect)
        {
            int primary = candidates.FindIndex(c => !c.Intent.SideEffect);
            if (primary > 0)
            {
                (candidates[0], candidates[primary]) = (candidates[primary], candidates[0]);
            }
        }

        List<MatchCandidate> sideEffects =
            [.. candidates.Skip(1).Where(c => c.Intent.SideEffect)];
        return new MatchOutcome { Ranked = candidates, SideEffects = sideEffects };
    }

    private MatchCandidate? Evaluate(IntentDef intent, Normalized input, MatchContext context)
    {
        double bestSpecificity = double.NegativeInfinity;
        LexicalPattern? bestPattern = null;
        Dictionary<string, string> captures = new(StringComparer.Ordinal);
        int matched = 0;

        foreach (LexicalPattern pattern in intent.Patterns)
        {
            PatternHit? hit = Run(pattern, input);
            if (hit is null)
            {
                continue;
            }

            matched++;
            foreach ((string key, string value) in hit.Captures)
            {
                // First pattern to produce a capture owns it: patterns are ordered as
                // authored, and re-binding mid-turn would make the reply depend on
                // pattern iteration order.
                captures.TryAdd(key, value);
            }

            double specificity = (intent.Specificity ?? ScoreModel.Specificity(pattern))
                + ScoreModel.MatchLengthBonus(hit.Length);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                bestPattern = pattern;
            }
        }

        if (bestPattern is null)
        {
            return null;
        }

        double score = bestSpecificity
            + (ScoreModel.CorroborationStep * (matched - 1))
            + TopicBonus(intent, context)
            + GuardBonus(intent, context, _persona.Root, out bool guardsHold);

        // A failed hard guard (tier too low, wrong activity) removes the intent from the
        // running entirely rather than just costing it points.
        if (!guardsHold)
        {
            return null;
        }

        return new MatchCandidate
        {
            Intent = intent,
            Score = score,
            Captures = captures.ToFrozenDictionary(StringComparer.Ordinal),
            BestPattern = bestPattern,
            MatchedPatternCount = matched,
        };
    }

    private static double TopicBonus(IntentDef intent, MatchContext context) =>
        intent.Topic is not null && intent.Topic == context.Topic
            ? ScoreModel.TopicAffinity
            : 0;

    private static double GuardBonus(
        IntentDef intent, MatchContext context, PersonaRoot root, out bool hold)
    {
        double bonus = 0;
        hold = true;
        foreach (GuardDef guard in intent.Guards)
        {
            if (!GuardEvaluator.Holds(guard, context, root))
            {
                hold = false;
                return 0;
            }

            bonus += guard.Bonus;
        }

        return bonus;
    }

    private sealed record PatternHit(int Length, IReadOnlyDictionary<string, string> Captures);

    private static readonly IReadOnlyDictionary<string, string> NoCaptures =
        FrozenDictionary<string, string>.Empty;

    private static PatternHit? Run(LexicalPattern pattern, Normalized input)
    {
        switch (pattern.Kind)
        {
            case MatchKind.Keyword:
            {
                string? hit = input.Tokens
                    .FirstOrDefault(t => pattern.Words.Contains(t, StringComparer.Ordinal));
                return hit is null ? null : new PatternHit(hit.Length, NoCaptures);
            }

            case MatchKind.AllKeywords:
            {
                bool all = pattern.Words.All(w => input.Tokens.Contains(w, StringComparer.Ordinal));
                return all ? new PatternHit(pattern.Words.Sum(w => w.Length), NoCaptures) : null;
            }

            case MatchKind.Fuzzy:
            {
                foreach (string token in input.Tokens)
                {
                    if (token.Length < pattern.MinLength)
                    {
                        // Short tokens fuzz into everything ("no" is one edit from "go"),
                        // so below min_length only an exact hit counts.
                        if (pattern.Words.Contains(token, StringComparer.Ordinal))
                        {
                            return new PatternHit(token.Length, NoCaptures);
                        }

                        continue;
                    }

                    foreach (string word in pattern.Words)
                    {
                        if (PersonaValidator.Levenshtein(token, word, pattern.MaxDistance)
                            <= pattern.MaxDistance)
                        {
                            return new PatternHit(token.Length, NoCaptures);
                        }
                    }
                }

                return null;
            }

            case MatchKind.Style:
                // The normalizer already decided; a style hit explains the whole message,
                // which is what keeps "AHHHHH" reading as shouting rather than elongation.
                // Length bonus deliberately zero: a style flag explains how the message was
                // typed, not any span of it, and a long shout is not a better match than a
                // short one.
                return input.Has(pattern.Style) ? new PatternHit(0, NoCaptures) : null;

            case MatchKind.Regex:
                return RunRegex(pattern, input);

            default:
                return null;
        }
    }

    private static PatternHit? RunRegex(LexicalPattern pattern, Normalized input)
    {
        if (pattern.Regex is null)
        {
            return null;
        }

        Match m;
        try
        {
            m = pattern.Regex.Match(input.Lower);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern must not take the turn down; it just does not match.
            // The validator's job is to keep these out of the persona in the first place.
            return null;
        }

        if (!m.Success)
        {
            return null;
        }

        // Captures are sliced out of Cased at spans found on Lower — the two are
        // index-aligned by construction, so "my name is Sam" gives back "Sam", not "sam".
        Dictionary<string, string> captures = new(StringComparer.Ordinal)
        {
            ["0"] = input.Cased.Substring(m.Index, m.Length),
        };
        foreach (string name in pattern.CaptureNames)
        {
            Group g = m.Groups[name];
            if (g.Success)
            {
                captures[name] = input.Cased.Substring(g.Index, g.Length);
            }
        }

        return new PatternHit(m.Length, captures);
    }
}

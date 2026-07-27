using System.Text.RegularExpressions;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;

namespace Sonarr.Elaine.Persona;

public static partial class PersonaValidator
{
    private static void CheckPools(PersonaGraph graph, List<PersonaIssue> issues)
    {
        HashSet<string> referenced = new(StringComparer.Ordinal);
        foreach (IntentDef intent in graph.Intents)
        {
            referenced.Add(intent.Pool);
        }

        foreach (ActivityDef activity in graph.Root.Activities)
        {
            referenced.Add(activity.FallbackPool);
        }

        foreach (StanceDef stance in graph.Stances)
        {
            referenced.Add(stance.Pool);
        }

        foreach (OverlayDef overlay in graph.Overlays)
        {
            foreach ((string from, string to) in overlay.Replaces)
            {
                referenced.Add(from);
                referenced.Add(to);
            }
        }

        foreach (PoolDef pool in graph.Pools.Values)
        {
            if (pool.IsEmpty)
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.EmptyPool,
                    $"pool '{pool.Id}' has no lines", pool.Location));
            }

            if (!referenced.Contains(pool.Id))
            {
                // A warning, not an error: pools are migrated in bulk and an unused one is
                // dead weight, not a crash.
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Warning, Rules.OrphanPool,
                    $"pool '{pool.Id}' is never referenced", pool.Location));
            }
        }
    }

    /// <summary>
    /// Capture safety and slot safety: every <c>{$x}</c> in an intent's template must be a
    /// named group that <em>every</em> pattern of that intent produces, and every
    /// <c>{slot}</c> must be declared. Pool lines are checked for slots only, since a pool
    /// can be drawn from many intents and has no captures of its own.
    /// </summary>
    private static void CheckTemplates(PersonaGraph graph, List<PersonaIssue> issues)
    {
        HashSet<string> slots = [.. graph.Root.Slots];

        foreach (IntentDef intent in graph.Intents)
        {
            if (intent.Template is null)
            {
                continue;
            }

            // Only regex patterns can produce captures, so a template referencing $x is
            // only safe if all capture-bearing paths produce x — and a non-regex pattern
            // produces none at all, which is exactly the bug this rule exists to catch.
            List<LexicalPattern> patterns = [.. intent.Patterns];
            foreach (Match m in ReferenceRegex().Matches(intent.Template))
            {
                bool isCapture = m.Groups[1].Value == "$";
                string name = m.Groups[2].Value;
                if (!isCapture)
                {
                    if (!slots.Contains(name))
                    {
                        issues.Add(new PersonaIssue(
                            PersonaIssueSeverity.Error, Rules.UnknownSlot,
                            $"intent '{intent.Id}' template uses slot '{{{name}}}', which is not declared",
                            intent.Location));
                    }

                    continue;
                }

                List<LexicalPattern> missing =
                    [.. patterns.Where(p => !p.CaptureNames.Contains(name, StringComparer.Ordinal))];
                if (missing.Count > 0)
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.UnsafeCapture,
                        $"intent '{intent.Id}' template uses capture '{{${name}}}' but "
                        + $"{missing.Count} of its {patterns.Count} pattern(s) cannot produce it",
                        missing[0].Location));
                }
            }
        }

        CheckSlotWiring(graph, slots, issues);

        foreach (PoolDef pool in graph.Pools.Values)
        {
            foreach (string line in AllLines(pool))
            {
                foreach (Match m in ReferenceRegex().Matches(line))
                {
                    string name = m.Groups[2].Value;
                    if (m.Groups[1].Value != "$" && !slots.Contains(name))
                    {
                        issues.Add(new PersonaIssue(
                            PersonaIssueSeverity.Error, Rules.UnknownSlot,
                            $"pool '{pool.Id}' uses slot '{{{name}}}', which is not declared",
                            pool.Location));
                    }
                }
            }
        }
    }

    /// <summary>
    /// <c>learns:</c> and <c>asks:</c> are the engine's write path into memory slots, so both
    /// ends are checked here: the slot must be declared, and the capture it reads from must be
    /// producible by every pattern of the intent — same rule as a template, because a missing
    /// capture would store an empty fact instead of shipping a visible <c>{nm}</c>.
    /// </summary>
    private static void CheckSlotWiring(
        PersonaGraph graph, HashSet<string> slots, List<PersonaIssue> issues)
    {
        foreach (IntentDef intent in graph.Intents)
        {
            foreach ((string slot, string capture) in intent.Learns)
            {
                if (!slots.Contains(slot))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.UnknownSlot,
                        $"intent '{intent.Id}' learns slot '{slot}', which is not declared",
                        intent.Location));
                }

                List<LexicalPattern> missing = [.. intent.Patterns
                    .Where(p => !p.CaptureNames.Contains(capture, StringComparer.Ordinal))];
                if (missing.Count > 0)
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.UnsafeCapture,
                        $"intent '{intent.Id}' learns '{slot}' from capture '{capture}' but "
                        + $"{missing.Count} of its {intent.Patterns.Count} pattern(s) cannot produce it",
                        missing[0].Location));
                }
            }

            foreach (string slot in intent.Asks.Where(s => !slots.Contains(s)))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.UnknownSlot,
                    $"intent '{intent.Id}' asks about slot '{slot}', which is not declared",
                    intent.Location));
            }
        }
    }

    /// <summary>
    /// Register declaration: <c>personality.baselines</c> is the only place a register comes
    /// into existence. An affect delta naming anything else writes a value that never decays
    /// (decay iterates baselines) and that no mode or guard can read — silent dead affect,
    /// which is why this is an error rather than a warning.
    /// </summary>
    private static void CheckRegisters(PersonaGraph graph, List<PersonaIssue> issues)
    {
        HashSet<string> declared = [.. graph.Root.Personality.Baselines.Keys];

        foreach (string register in declared.Where(r => !graph.Root.Personality.Decay.ContainsKey(r)))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Warning, Rules.MissingField,
                $"register '{register}' has a baseline but no decay rate, so it never returns to it",
                graph.Root.Location));
        }

        foreach (IntentDef intent in graph.Intents)
        {
            foreach (AffectDelta delta in intent.Affect.Where(d => !declared.Contains(d.Register)))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.UnknownRegister,
                    $"intent '{intent.Id}' moves register '{delta.Register}', which is not declared "
                    + "in personality.baselines",
                    intent.Location));
            }

            foreach (GuardDef guard in intent.Guards.Where(g => g.Kind == GuardDef.Register))
            {
                CheckRegisterExpression(guard.Value, declared, $"intent '{intent.Id}' guard",
                    guard.Location, issues);
            }
        }

        foreach (ModeDef mode in graph.Root.Modes)
        {
            foreach (string clause in mode.When)
            {
                CheckRegisterExpression(clause, declared, $"mode '{mode.Id}' when-clause",
                    mode.Location, issues);
            }
        }
    }

    private static void CheckRegisterExpression(
        string expression, HashSet<string> declared, string who,
        PersonaLocation location, List<PersonaIssue> issues)
    {
        if (RegisterExpression.RegisterOf(expression) is not { } register)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.BadRegisterExpression,
                $"{who} '{expression}' is not 'register op number'", location));
            return;
        }

        if (!declared.Contains(register))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.UnknownRegister,
                $"{who} reads register '{register}', which is not declared in personality.baselines",
                location));
        }
    }

    private static IEnumerable<string> AllLines(PoolDef pool) =>
        pool.Lines.Concat(pool.ByMode.Values.SelectMany(v => v));

    /// <summary>
    /// Reachability: an intent no activity admits can never be selected, so it is dead
    /// authored text — an error, because it is always a wiring mistake.
    /// </summary>
    private static void CheckReachability(PersonaGraph graph, List<PersonaIssue> issues)
    {
        foreach (IntentDef intent in graph.Intents)
        {
            if (!graph.Root.Activities.Any(a => a.Allows(intent.Id)))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.Unreachable,
                    $"intent '{intent.Id}' is not allowed by any activity", intent.Location));
            }
        }
    }

    /// <summary>
    /// Pool coverage per mode: pools listed in <c>mode_coverage_pools</c> must have a
    /// variant for every non-fallback mode, so an angry Sonarr never falls back to a
    /// neutral line mid-rage.
    /// </summary>
    private static void CheckModeCoverage(PersonaGraph graph, List<PersonaIssue> issues)
    {
        List<string> required = [.. graph.Root.Modes.Where(m => !m.Always).Select(m => m.Id)];
        foreach (string poolId in graph.Root.ModeCoveragePools)
        {
            if (!graph.Pools.TryGetValue(poolId, out PoolDef? pool))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingPool,
                    $"mode_coverage_pools names pool '{poolId}', which does not exist",
                    graph.Root.Location));
                continue;
            }

            List<string> gaps = [.. required.Where(m => !pool.ByMode.ContainsKey(m))];
            if (gaps.Count > 0)
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.ModeCoverage,
                    $"pool '{poolId}' is mode-covered but has no lines for {string.Join(", ", gaps)}",
                    pool.Location));
            }
        }
    }

    /// <summary>
    /// Overlay collisions: two overlays with the same activation expression and the same
    /// priority that both replace one pool have no deterministic winner.
    /// </summary>
    private static void CheckOverlays(PersonaGraph graph, List<PersonaIssue> issues)
    {
        foreach (OverlayDef overlay in graph.Overlays
            .Where(o => !OverlayActivation.IsWellFormed(o.Activation)))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.BadActivationExpression,
                $"overlay '{overlay.Id}' activation '{overlay.Activation}' is not "
                + "'field op number' or 'field in low..high', so it could never activate",
                overlay.Location));
        }

        for (int i = 0; i < graph.Overlays.Count; i++)
        {
            for (int j = i + 1; j < graph.Overlays.Count; j++)
            {
                OverlayDef a = graph.Overlays[i];
                OverlayDef b = graph.Overlays[j];
                if (a.Priority != b.Priority)
                {
                    continue;
                }

                List<string> clashes = [.. a.Replaces.Keys.Where(b.Replaces.ContainsKey)];
                if (clashes.Count > 0)
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.OverlayCollision,
                        $"overlays '{a.Id}' and '{b.Id}' share priority {a.Priority} and both "
                        + $"replace {string.Join(", ", clashes)}; the winner would be arbitrary",
                        b.Location));
                }
            }
        }
    }
}

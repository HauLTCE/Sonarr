using System.Text.RegularExpressions;
using Sonarr.Elaine.Matching;

namespace Sonarr.Elaine.Persona;

/// <summary>
/// Checks a loaded graph for the failure classes docs/10 requires: reachability, dangling
/// refs, capture safety, pool coverage per mode, overlay collisions, and a shadowing
/// report. Returns findings; never throws for content problems.
/// </summary>
public static partial class PersonaValidator
{
    /// <summary>Slot/capture reference in a template: <c>{name}</c> or <c>{$nm}</c>.</summary>
    [GeneratedRegex(@"\{(\$?)([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();

    public static IReadOnlyList<PersonaIssue> Validate(PersonaGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<PersonaIssue> issues = [];

        CheckModes(graph, issues);
        CheckDanglingRefs(graph, issues);
        CheckPools(graph, issues);
        CheckTemplates(graph, issues);
        CheckRegisters(graph, issues);
        CheckReachability(graph, issues);
        CheckModeCoverage(graph, issues);
        CheckOverlays(graph, issues);
        issues.AddRange(ShadowingReport(graph));

        return issues;
    }

    private static void CheckModes(PersonaGraph graph, List<PersonaIssue> issues)
    {
        List<ModeDef> always = [.. graph.Root.Modes.Where(m => m.Always)];
        if (always.Count == 0)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.NoFallbackMode,
                "no mode is marked 'always: true'; mood selection would fall through",
                graph.Root.Location));
        }
        else if (always.Count > 1)
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.MultipleFallbackModes,
                $"modes {string.Join(", ", always.Select(m => m.Id))} are all 'always'",
                always[1].Location));
        }
    }

    private static void CheckDanglingRefs(PersonaGraph graph, List<PersonaIssue> issues)
    {
        HashSet<string> pools = [.. graph.Pools.Keys];
        HashSet<string> activities = [.. graph.Root.Activities.Select(a => a.Id)];
        HashSet<string> tiers = [.. graph.Root.Tiers.Select(t => t.Id)];
        HashSet<string> modes = [.. graph.Root.Modes.Select(m => m.Id)];
        HashSet<string> intents = [.. graph.Intents.Select(i => i.Id)];
        HashSet<string> slots = [.. graph.Root.Slots];

        if (!activities.Contains(graph.Root.StartActivity))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.DanglingActivity,
                $"start_activity '{graph.Root.StartActivity}' is not a declared activity",
                graph.Root.Location));
        }

        foreach (ActivityDef activity in graph.Root.Activities)
        {
            if (!pools.Contains(activity.FallbackPool))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingPool,
                    $"activity '{activity.Id}' fallback_pool '{activity.FallbackPool}' does not exist",
                    activity.Location));
            }

            foreach (string id in activity.Intents.Where(i => i != ActivityDef.AllIntents && !intents.Contains(i)))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingIntent,
                    $"activity '{activity.Id}' allows intent '{id}', which does not exist",
                    activity.Location));
            }
        }

        foreach (IntentDef intent in graph.Intents)
        {
            if (!pools.Contains(intent.Pool))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingPool,
                    $"intent '{intent.Id}' draws from pool '{intent.Pool}', which does not exist",
                    intent.Location));
            }

            if (intent.PushActivity is { } push && !activities.Contains(push))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingActivity,
                    $"intent '{intent.Id}' pushes activity '{push}', which does not exist",
                    intent.Location));
            }

            foreach (GuardDef guard in intent.Guards)
            {
                CheckGuard(guard, tiers, modes, activities, slots, issues);
            }
        }

        foreach (StanceDef stance in graph.Stances.Where(s => !pools.Contains(s.Pool)))
        {
            issues.Add(new PersonaIssue(
                PersonaIssueSeverity.Error, Rules.DanglingPool,
                $"stance '{stance.Topic}/{stance.Stance}' names pool '{stance.Pool}', which does not exist",
                stance.Location));
        }

        foreach (OverlayDef overlay in graph.Overlays)
        {
            foreach ((string from, string to) in overlay.Replaces)
            {
                foreach (string id in new[] { from, to }.Where(p => !pools.Contains(p)))
                {
                    issues.Add(new PersonaIssue(
                        PersonaIssueSeverity.Error, Rules.DanglingPool,
                        $"overlay '{overlay.Id}' references pool '{id}', which does not exist",
                        overlay.Location));
                }
            }
        }

        foreach (PoolDef pool in graph.Pools.Values)
        {
            foreach (string mode in pool.ByMode.Keys.Where(m => !modes.Contains(m)))
            {
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingMode,
                    $"pool '{pool.Id}' has a variant for mode '{mode}', which does not exist",
                    pool.Location));
            }
        }
    }

    private static void CheckGuard(
        GuardDef guard, HashSet<string> tiers, HashSet<string> modes,
        HashSet<string> activities, HashSet<string> slots, List<PersonaIssue> issues)
    {
        switch (guard.Kind)
        {
            case GuardDef.MinTier or GuardDef.MaxTier when !tiers.Contains(guard.Value):
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingTier,
                    $"guard {guard.Kind} references tier '{guard.Value}', which does not exist",
                    guard.Location));
                break;

            case GuardDef.Mode when !modes.Contains(guard.Value):
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingMode,
                    $"guard references mode '{guard.Value}', which does not exist", guard.Location));
                break;

            case GuardDef.Activity when !activities.Contains(guard.Value):
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.DanglingActivity,
                    $"guard references activity '{guard.Value}', which does not exist", guard.Location));
                break;

            case GuardDef.HasSlot when !slots.Contains(guard.Value):
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.UnknownSlot,
                    $"guard references slot '{guard.Value}', which is not declared in sonarr.yaml",
                    guard.Location));
                break;

            case GuardDef.MinTier or GuardDef.MaxTier or GuardDef.Mode
                 or GuardDef.Activity or GuardDef.HasSlot or GuardDef.Register:
                break;

            default:
                issues.Add(new PersonaIssue(
                    PersonaIssueSeverity.Error, Rules.UnknownGuardKind,
                    $"guard kind '{guard.Kind}' is not recognized", guard.Location));
                break;
        }
    }
}

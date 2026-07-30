namespace Sonarr.Elaine.Persona;

/// <summary>
/// Stable ids for every validation rule. Tests and CI assert on these strings, so they
/// are part of the public contract — rename one and you break the persona lint gate.
/// </summary>
public static class Rules
{
    // --- structural (loader) ---

    /// <summary>A persona file is not parseable YAML.</summary>
    public const string YamlSyntax = "yaml-syntax";

    /// <summary><c>sonarr.yaml</c> is absent or empty.</summary>
    public const string MissingRoot = "missing-root";

    /// <summary>A required key is absent.</summary>
    public const string MissingField = "missing-field";

    /// <summary>A pool is neither a list nor a map of mode → list.</summary>
    public const string PoolShape = "pool-shape";

    /// <summary>Two files define the same pool id.</summary>
    public const string DuplicatePool = "duplicate-pool";

    /// <summary>Two intents share an id.</summary>
    public const string DuplicateIntent = "duplicate-intent";

    /// <summary>An intent's match block produced no patterns.</summary>
    public const string EmptyMatch = "empty-match";

    /// <summary>A regex does not compile.</summary>
    public const string BadRegex = "bad-regex";

    /// <summary>A <c>style:</c> entry is not one of the normalizer's flags.</summary>
    public const string UnknownStyle = "unknown-style";

    // --- referential integrity ---

    /// <summary>An intent, activity, stance, or overlay names a pool that does not exist.</summary>
    public const string DanglingPool = "dangling-pool-ref";

    /// <summary>A guard or intent names an activity that does not exist.</summary>
    public const string DanglingActivity = "dangling-activity-ref";

    /// <summary>An activity's intent allow-list names an intent that does not exist.</summary>
    public const string DanglingIntent = "dangling-intent-ref";

    /// <summary>A guard names a tier that does not exist.</summary>
    public const string DanglingTier = "dangling-tier-ref";

    /// <summary>A guard or pool variant names a mode that does not exist.</summary>
    public const string DanglingMode = "dangling-mode-ref";

    /// <summary>A guard has an unrecognized kind.</summary>
    public const string UnknownGuardKind = "unknown-guard-kind";

    // --- content integrity ---

    /// <summary>A pool has no lines at all.</summary>
    public const string EmptyPool = "empty-pool";

    /// <summary>A template references a capture no pattern of that intent can produce.</summary>
    public const string UnsafeCapture = "unsafe-capture";

    /// <summary>A template references a slot not declared in <c>sonarr.yaml</c>.</summary>
    public const string UnknownSlot = "unknown-slot";

    /// <summary>An intent is unreachable: no activity allows it.</summary>
    public const string Unreachable = "unreachable-intent";

    /// <summary>A pool that nothing references.</summary>
    public const string OrphanPool = "orphan-pool";

    /// <summary>
    /// A <c>side_effect</c> intent declares something a side-effect clause does not apply —
    /// <c>learns</c>, <c>asks</c>, <c>push</c>, <c>pop</c> or <c>topic</c>.
    /// </summary>
    public const string SideEffectSideEffects = "side-effect-does-more-than-text";

    /// <summary>A mode-coverage pool lacks a variant for some mode.</summary>
    public const string ModeCoverage = "mode-coverage";

    /// <summary>Two overlays that can be active together replace the same pool.</summary>
    public const string OverlayCollision = "overlay-collision";

    /// <summary>No mode is marked <c>always</c>, so mood selection can fall through.</summary>
    public const string NoFallbackMode = "no-fallback-mode";

    /// <summary>Two modes are marked <c>always</c>.</summary>
    public const string MultipleFallbackModes = "multiple-fallback-modes";

    /// <summary>An intent can never win: a broader intent outscores it on every input it matches.</summary>
    public const string Shadowed = "shadowed-intent";

    /// <summary>
    /// An affect delta, mode <c>when</c> clause, or <c>register</c> guard names a register that
    /// <c>personality.baselines</c> does not declare, so it never decays and nothing reads it.
    /// </summary>
    public const string UnknownRegister = "unknown-register";

    /// <summary>A register expression is not <c>name op number</c>.</summary>
    public const string BadRegisterExpression = "bad-register-expression";

    /// <summary>
    /// An overlay's <c>activation</c> is not an expression the adapter can evaluate, so the
    /// overlay would silently never activate.
    /// </summary>
    public const string BadActivationExpression = "bad-activation-expression";
}

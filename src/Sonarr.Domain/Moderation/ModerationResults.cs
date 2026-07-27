namespace Sonarr.Domain.Moderation;

/// <summary>
/// Why a moderation action was refused. The controller maps this to a sentence; the service
/// decides it, so the web panel gets the same verdict for the same reason.
/// </summary>
public enum ModerationDenial
{
    /// <summary>No denial.</summary>
    None,

    /// <summary>The invoker lacks the Discord permission the action needs.</summary>
    ActorMissingPermission,

    /// <summary>The target's highest role is not below the invoker's.</summary>
    ActorRoleTooLow,

    /// <summary>Sonarr's own top role is not above the target's, so Discord would reject the call.</summary>
    BotRoleTooLow,

    /// <summary>Sonarr lacks the guild permission the action needs.</summary>
    BotMissingPermission,

    /// <summary>Nobody moderates themselves.</summary>
    SelfTarget,

    /// <summary>Not even with a case number.</summary>
    GuildOwnerTarget,

    /// <summary>Sonarr is not going to ban Sonarr.</summary>
    BotTarget,

    /// <summary>The member left, or was never here.</summary>
    TargetNotFound,

    /// <summary>Duration outside the allowed window (timeout &gt; 28 d, tempban &lt; 1 min, …).</summary>
    InvalidDuration,
}

/// <summary>
/// Outcome of a destructive action. <see cref="Case"/> is set only when the action happened —
/// a denial writes no case row, because nothing was done.
/// </summary>
public sealed record ModerationOutcome(bool Allowed, ModerationDenial Denial, CaseRecord? Case)
{
    public static ModerationOutcome Denied(ModerationDenial denial) => new(false, denial, null);

    public static ModerationOutcome Done(CaseRecord record) => new(true, ModerationDenial.None, record);
}

/// <summary>
/// The role-hierarchy facts the service needs, lifted out of Discord types by the controller.
/// Positions are Discord role positions: higher number = higher role.
/// </summary>
/// <param name="ActorHasPermission">Invoker holds the guild permission this action requires.</param>
/// <param name="BotHasPermission">Sonarr holds it too.</param>
/// <param name="ActorIsOwner">Guild owner: outranks everyone regardless of role position.</param>
/// <param name="ActorTopRole">Invoker's highest role position.</param>
/// <param name="BotTopRole">Sonarr's highest role position.</param>
/// <param name="TargetTopRole">Target's highest role position, or <c>null</c> when the target is not a member.</param>
/// <param name="TargetIsOwner">Target is the guild owner.</param>
/// <param name="TargetIsBot">Target is Sonarr itself.</param>
public sealed record HierarchySnapshot(
    bool ActorHasPermission,
    bool BotHasPermission,
    bool ActorIsOwner,
    int ActorTopRole,
    int BotTopRole,
    int? TargetTopRole,
    bool TargetIsOwner = false,
    bool TargetIsBot = false);

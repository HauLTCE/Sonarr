using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Moderation;

/// <summary>
/// The service-layer half of the security rule (docs/checklist.md — Moderation): the invoker
/// must hold the permission AND outrank the target, and Sonarr must outrank them too.
/// </summary>
/// <remarks>
/// Discord's own preconditions already stop most of this at the controller. This exists because
/// "checked in the module" is not a guarantee: the web panel calls the same service, and a role
/// can change between the precondition and the API call. Deny-by-default — an unknown target
/// role position means "not below me", not "fine".
/// </remarks>
public static class ModerationGuard
{
    /// <summary>
    /// <see cref="ModerationDenial.None"/> when the action may proceed.
    /// </summary>
    /// <param name="action">What is being attempted; read-only actions skip the hierarchy check.</param>
    /// <param name="actorId">Invoker.</param>
    /// <param name="targetId">Target.</param>
    /// <param name="hierarchy">The permission and role-position facts.</param>
    public static ModerationDenial Check(
        CaseAction action,
        ulong actorId,
        ulong targetId,
        HierarchySnapshot hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        if (!hierarchy.ActorHasPermission)
        {
            return ModerationDenial.ActorMissingPermission;
        }

        if (!hierarchy.BotHasPermission)
        {
            return ModerationDenial.BotMissingPermission;
        }

        if (actorId == targetId)
        {
            return ModerationDenial.SelfTarget;
        }

        if (hierarchy.TargetIsBot)
        {
            return ModerationDenial.BotTarget;
        }

        if (hierarchy.TargetIsOwner)
        {
            return ModerationDenial.GuildOwnerTarget;
        }

        // A member who has left has no role position. Ban and unban are still legal against
        // them (that is the point of a ban by id); anything that needs them present is not.
        if (hierarchy.TargetTopRole is not { } targetRole)
        {
            return action.NeedsPresentMember()
                ? ModerationDenial.TargetNotFound
                : ModerationDenial.None;
        }

        // The owner outranks everyone regardless of role positions.
        if (!hierarchy.ActorIsOwner && hierarchy.ActorTopRole <= targetRole)
        {
            return ModerationDenial.ActorRoleTooLow;
        }

        // Discord would reject the call anyway; refusing here gives the mod a real reason
        // instead of a 403 dressed up as "something broke".
        return hierarchy.BotTopRole <= targetRole
            ? ModerationDenial.BotRoleTooLow
            : ModerationDenial.None;
    }

    /// <summary>The user-facing sentence for a denial. One place, so Discord and the panel agree.</summary>
    public static string Explain(ModerationDenial denial) => denial switch
    {
        ModerationDenial.ActorMissingPermission => "You don't have the permission for that.",
        ModerationDenial.ActorRoleTooLow => "They're above you in the role list. Not happening.",
        ModerationDenial.BotRoleTooLow => "Their top role is above mine, so Discord won't let me touch them. Move my role up.",
        ModerationDenial.BotMissingPermission => "I don't have the permission for that here — check `/checkperms`.",
        ModerationDenial.SelfTarget => "You can't moderate yourself.",
        ModerationDenial.GuildOwnerTarget => "That's the server owner.",
        ModerationDenial.BotTarget => "I'm not doing that to myself.",
        ModerationDenial.TargetNotFound => "They're not in this server.",
        ModerationDenial.InvalidDuration => "That duration doesn't work — check the limits and try again.",
        _ => "Done.",
    };
}

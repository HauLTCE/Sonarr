using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// Decides what happens on member join/leave (docs/08-background-services.md — WelcomeFlow).
/// The gateway handler in Sonarr.Bot performs the plan; the wording and the config reads live
/// here, so the panel can preview a welcome without a Discord round trip.
/// </summary>
public interface IWelcomeService
{
    /// <summary>Welcome embed + autorole, per <c>welcome_channel</c> and <c>autorole_id</c>.</summary>
    Task<WelcomePlan> OnJoinAsync(MemberEvent member, CancellationToken cancellationToken = default);

    /// <summary>Farewell embed. Never grants a role.</summary>
    Task<WelcomePlan> OnLeaveAsync(MemberEvent member, CancellationToken cancellationToken = default);
}

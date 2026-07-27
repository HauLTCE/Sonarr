using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// Wires the durable scheduler and the job kinds this slice owns. Other slices add their own
/// <see cref="IJobHandler"/> registrations (moderation's tempban lift, capsules, season closes) and
/// the scheduler picks them up by kind — no change here needed.
/// </summary>
public static class JobServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c> (the poller needs <c>IJobRepository</c>) and after
    /// <c>AddSonarrUtility</c> / <c>AddSonarrModeration</c> so their handlers are registered — order
    /// among the handler registrations themselves does not matter.
    /// </summary>
    public static IServiceCollection AddSonarrJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped, like every other handler: the scheduler opens one scope per poll tick so a
        // handler shares that tick's DbContext.
        services.AddScoped<IJobHandler, ReminderJobHandler>();
        services.AddScoped<IJobHandler, AnnounceJobHandler>();

        services.AddHostedService<JobScheduler>();

        return services;
    }
}

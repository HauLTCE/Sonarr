using Sonarr.Domain.Entities.Core;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// One kind of core.job row. The JobScheduler background service claims due rows
/// (docs/08-background-services.md) and dispatches each to the handler whose
/// <see cref="Kind"/> matches <see cref="Job.Kind"/>.
/// </summary>
/// <remarks>
/// A handler is registered per kind; the scheduler owns claim/complete/fail, so a handler
/// only does the work and throws on failure. Handlers must be idempotent: a job claimed
/// before a crash is retried, and the same tempban lift may run twice.
/// </remarks>
public interface IJobHandler
{
    /// <summary>The <see cref="Job.Kind"/> value this handler answers for.</summary>
    string Kind { get; }

    /// <summary>Do the work. Throwing marks the job failed with the exception message.</summary>
    Task HandleAsync(Job job, CancellationToken ct = default);
}

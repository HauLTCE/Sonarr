using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;
using Sonarr.Domain.Utility;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>/capsule write</c> (docs/07-commands.md#community): a message this channel reads at a future
/// date. The text lives in <c>social.capsule</c> and the delivery is a <c>core.job</c> row, so a
/// restart loses nothing.
/// </summary>
/// <remarks>
/// Two rows rather than a reminder's one, because a capsule can sit for a year: the table is the
/// one place <c>/privacy</c> and a forget-me delete can both reach the text (docs/06), and a
/// payload field would be invisible to both.
/// </remarks>
public interface ICapsuleService
{
    /// <summary>
    /// Seals a capsule. <paramref name="when"/> is parsed in the author's timezone; a recurring
    /// phrase is rejected because a capsule opens once.
    /// </summary>
    Task<ScheduleResult> WriteAsync(
        ulong guildId,
        ulong channelId,
        ulong authorId,
        string when,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The capsule a claimed job should post, or <c>null</c> when there is nothing deliverable —
    /// a hand-edited payload, a deleted capsule, or one already opened.
    /// </summary>
    Task<Capsule?> ReadAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps the capsule opened, <b>after</b> a successful post. False means it was already
    /// stamped, which the handler logs as a possible duplicate.
    /// </summary>
    Task<bool> MarkOpenedAsync(long capsuleId, CancellationToken cancellationToken = default);
}

using System.Collections.Concurrent;

namespace Sonarr.Bot.Observability;

/// <summary>
/// The last few command failures per user, so a member can be shown their own failures rather than
/// a shared errors channel. Only the write path has a caller on the current surface.
/// </summary>
/// <remarks>
/// Stores the case id, the command name and the friendly line — never the exception, never the
/// arguments. A user's command text is not logged anywhere else (docs/06) and this is not the place
/// to start; the case id is what correlates the user-facing line with the Serilog entry an admin reads.
/// </remarks>
// ponytail: in-memory and per-process, so a restart clears the list and only the last 10 per user
// are kept. Upgrade path is a Redis list per user (`errors:{user}`, same shape, 7 d TTL) — the
// interface here is already "append, read newest first", so the swap is one class.
public sealed class UserErrorLog
{
    /// <summary>Kept per user. Ten is a screenful; nothing pages past it.</summary>
    public const int PerUser = 10;

    /// <summary>
    /// Ceiling on how many users we hold at once. A restart is the eviction policy for the rest:
    /// an unbounded dictionary keyed by a user id is a slow leak on a box with 8 GB.
    /// </summary>
    public const int MaxUsers = 500;

    private readonly ConcurrentDictionary<ulong, Queue<UserError>> _byUser = new();

    public void Record(ulong userId, UserError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (userId == 0)
        {
            return;
        }

        if (_byUser.Count >= MaxUsers && !_byUser.ContainsKey(userId))
        {
            // Full and this is a new user: drop the record rather than the bound. The alternative
            // is an LRU, which is a lot of bookkeeping for a list that dies at restart anyway.
            return;
        }

        Queue<UserError> queue = _byUser.GetOrAdd(userId, static _ => new Queue<UserError>(PerUser));

        // The queue is not thread-safe and two interactions can fail at once.
        lock (queue)
        {
            queue.Enqueue(error);
            while (queue.Count > PerUser)
            {
                queue.Dequeue();
            }
        }
    }

    /// <summary>Newest first. No caller on the current surface reads it back.</summary>
    public IReadOnlyList<UserError> Recent(ulong userId)
    {
        if (!_byUser.TryGetValue(userId, out Queue<UserError>? queue))
        {
            return [];
        }

        lock (queue)
        {
            return [.. queue.Reverse()];
        }
    }
}

/// <param name="CaseId">The reference shown to the user; the same value is in the log line.</param>
/// <param name="Command">Slash command name only, no arguments.</param>
/// <param name="Message">The friendly line the user already saw.</param>
public sealed record UserError(string CaseId, string Command, string Message, DateTimeOffset At);

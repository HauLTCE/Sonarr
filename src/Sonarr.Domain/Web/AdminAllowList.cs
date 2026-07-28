namespace Sonarr.Domain.Web;

/// <summary>
/// Who may reach <c>/api/admin/*</c>. Built once from <c>ADMIN_USER_IDS</c> and consulted on every
/// admin request rather than being folded into the session (docs/09: the cached session is not the
/// authority for admin rights, so removing an id takes effect on the next request, not the next login).
/// </summary>
/// <remarks>
/// ponytail: the list is env-sourced because docs/04 has no admin table. Ceiling — changing it
/// needs a restart, and there is no per-guild granularity. Upgrade path: a <c>web.admin</c> table
/// plus a cache in the <c>cfg</c> area, behind this same type so no caller changes.
/// </remarks>
public sealed class AdminAllowList
{
    private readonly HashSet<ulong> _ids;

    public AdminAllowList(IEnumerable<ulong> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        _ids = [.. userIds];
    }

    public int Count => _ids.Count;

    public bool Contains(ulong userId) => _ids.Contains(userId);
}

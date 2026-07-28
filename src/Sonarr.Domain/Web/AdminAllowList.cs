namespace Sonarr.Domain.Web;

/// <summary>
/// Who holds the <b>bot</b> tier — the one that reaches every guild and the cross-guild routes. Built
/// once from <c>ADMIN_USER_IDS</c> and consulted on every admin request rather than being folded into
/// the session (docs/09: the cached session is not the authority for admin rights, so removing an id
/// takes effect on the next request, not the next login).
/// </summary>
/// <remarks>
/// Not the only tier. Per-guild admins are resolved live from the gateway through
/// <see cref="Abstractions.IGuildAuthority"/> and never appear here; <see cref="PanelGate"/> is where
/// the two are combined.
/// <para>ponytail: the list is env-sourced because docs/04 has no admin table. Ceiling — changing who
/// holds the bot tier needs a restart. Upgrade path: a <c>web.admin</c> table plus a cache in the
/// <c>cfg</c> area, behind this same type so no caller changes.</para>
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

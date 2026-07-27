using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Mod;
using Sonarr.Domain.Moderation;

namespace Sonarr.Infrastructure.Persistence.Repositories.Mod;

/// <inheritdoc cref="IModCaseRepository"/>
public sealed class ModCaseRepository(SonarrDbContext db) : IModCaseRepository
{
    public async Task<CaseRecord> AddAsync(NewCase newCase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newCase);

        ModCase row = new()
        {
            GuildId = (long)newCase.GuildId,
            TargetId = (long)newCase.TargetId,
            ActorId = (long)newCase.ActorId,
            Action = newCase.Action.ToStorage(),
            Reason = newCase.Reason,
            ExpiresAt = newCase.ExpiresAt,
            Context = ToJson(newCase.Context),
        };

        db.Cases.Add(row);
        await db.SaveChangesAsync(ct);

        // case_id is IDENTITY ALWAYS, so the number only exists after the insert — that is the
        // case number users see, and it is Postgres's to hand out, never ours to guess.
        return ToRecord(row);
    }

    public async Task<CaseRecord?> GetAsync(long guildId, long caseId, CancellationToken ct = default)
    {
        ModCase? row = await db.Cases
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CaseId == caseId && c.GuildId == guildId, ct);

        return row is null ? null : ToRecord(row);
    }

    public async Task<CasePage> GetPageAsync(
        long guildId,
        long? targetId,
        int page,
        CancellationToken ct = default)
    {
        var wanted = Math.Max(page, 1);

        IQueryable<ModCase> query = db.Cases
            .AsNoTracking()
            .Where(c => c.GuildId == guildId);

        if (targetId is { } target)
        {
            query = query.Where(c => c.TargetId == target);
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)CasePage.PageSize));

        List<ModCase> rows = await query
            .OrderByDescending(c => c.CaseId)
            .Skip((wanted - 1) * CasePage.PageSize)
            .Take(CasePage.PageSize)
            .ToListAsync(ct);

        return new CasePage([.. rows.Select(ToRecord)], wanted, pageCount, total);
    }

    public async Task<InfractionTally> GetTallyAsync(long guildId, long targetId, CancellationToken ct = default)
    {
        // The view aggregates per (guild, target); a member with no cases has no row.
        InfractionSummary? row = await db.InfractionSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.GuildId == guildId && s.TargetId == targetId, ct);

        return row is null
            ? InfractionTally.Empty
            : new InfractionTally(
                (int)row.WarnCount,
                (int)row.KickCount,
                (int)row.BanCount,
                row.LastCaseAt);
    }

    private static CaseRecord ToRecord(ModCase row) => new(
        row.CaseId,
        (ulong)row.GuildId,
        (ulong)row.TargetId,
        (ulong)row.ActorId,
        CaseActions.FromStorage(row.Action),
        row.Reason,
        row.ExpiresAt,
        row.CreatedAt,
        FromJson(row.Context));

    private static JsonObject ToJson(IReadOnlyDictionary<string, string>? context)
    {
        JsonObject json = [];
        if (context is null)
        {
            return json;
        }

        foreach ((var key, var value) in context)
        {
            json[key] = value;
        }

        return json;
    }

    private static Dictionary<string, string> FromJson(JsonObject context)
        => context.ToDictionary(
            p => p.Key,
            p => p.Value switch
            {
                null => string.Empty,
                JsonValue value => value.ToString(),
                _ => p.Value.ToJsonString(),
            },
            StringComparer.Ordinal);
}

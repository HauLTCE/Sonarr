using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Repositories.Chat;

/// <inheritdoc cref="IGuildStateRepository"/>
/// <remarks>
/// Loads the row, hands it to <see cref="GuildEventLog"/>, saves only if that says something
/// changed — which on a 5-minute sampler is almost never.
/// </remarks>
public sealed class GuildStateRepository(SonarrDbContext db) : IGuildStateRepository
{
    public async Task<IReadOnlyList<GuildEvent>> GetEventsAsync(
        long guildId, CancellationToken ct = default)
        => GuildEventLog.Read(await db.ChatGuildStates
            .AsNoTracking()
            .Where(s => s.GuildId == guildId)
            .Select(s => s.EventLog)
            .FirstOrDefaultAsync(ct));

    public async Task<bool> RecordOnlineRecordAsync(
        long guildId, int online, DateTimeOffset at, CancellationToken ct = default)
    {
        if (online <= 0)
        {
            // Cheap reject before the read: the sampler calls this every pass for every guild.
            return false;
        }

        ChatGuildState? state = await db.ChatGuildStates
            .FirstOrDefaultAsync(s => s.GuildId == guildId, ct);

        if (GuildEventLog.WithOnlineRecord(state?.EventLog, online, at) is not { } log)
        {
            return false;
        }

        if (state is null)
        {
            db.ChatGuildStates.Add(new ChatGuildState { GuildId = guildId, EventLog = log });
        }
        else
        {
            // Replaced wholesale rather than appended in SQL: the comparison already needs the row
            // loaded, and jsonb_insert would not make it atomic. There is one sampler, not two.
            state.EventLog = log;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}

using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Infrastructure.Persistence.Repositories.Music;

/// <inheritdoc cref="IMusicPrefsRepository"/>
public sealed class MusicPrefsRepository(SonarrDbContext db) : IMusicPrefsRepository
{
    public async Task<int?> GetVolumeAsync(ulong guildId, ulong userId, CancellationToken ct = default)
    {
        var guild = (long)guildId;
        var user = (long)userId;

        return await db.MusicUserPrefs
            .AsNoTracking()
            .Where(p => p.GuildId == guild && p.UserId == user)
            .Select(p => p.Volume)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetVolumeAsync(ulong guildId, ulong userId, int volume, CancellationToken ct = default)
    {
        if (volume < MusicRules.MinVolume || volume > MusicRules.MaxVolume)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volume), volume, $"Volume is {MusicRules.MinVolume}–{MusicRules.MaxVolume}.");
        }

        var guild = (long)guildId;
        var user = (long)userId;

        MusicUserPrefs? row = await db.MusicUserPrefs
            .FirstOrDefaultAsync(p => p.GuildId == guild && p.UserId == user, ct);

        if (row is null)
        {
            db.MusicUserPrefs.Add(new MusicUserPrefs { GuildId = guild, UserId = user, Volume = volume });
        }
        else
        {
            row.Volume = volume;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}

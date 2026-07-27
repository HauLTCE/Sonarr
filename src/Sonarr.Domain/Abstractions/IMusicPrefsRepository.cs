namespace Sonarr.Domain.Abstractions;

/// <summary>
/// <c>music.user_prefs</c> — per (guild, user) volume. Durable on purpose: the old bot forgot
/// these on restart (docs/07-commands.md: "/volume … per-user preference remembered").
/// </summary>
public interface IMusicPrefsRepository
{
    /// <summary><c>null</c> when the user has no preference stored.</summary>
    Task<int?> GetVolumeAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default);

    Task SetVolumeAsync(ulong guildId, ulong userId, int volume, CancellationToken cancellationToken = default);
}

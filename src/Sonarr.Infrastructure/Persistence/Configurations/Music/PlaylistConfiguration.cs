using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Music;

namespace Sonarr.Infrastructure.Persistence.Configurations.Music;

internal sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("playlist", "music");
        builder.HasKey(p => p.PlaylistId);
        builder.Property(p => p.PlaylistId).UseIdentityAlwaysColumn();
        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.HasAuditTimestamps();

        // "name unique per guild" (docs/04-database.md).
        builder.HasIndex(p => new { p.GuildId, p.Name }).IsUnique();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(p => p.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlaylistTrackConfiguration : IEntityTypeConfiguration<PlaylistTrack>
{
    public void Configure(EntityTypeBuilder<PlaylistTrack> builder)
    {
        builder.ToTable("playlist_track", "music");
        builder.HasKey(t => new { t.PlaylistId, t.Position });
        builder.Property(t => t.Title).HasMaxLength(512).IsRequired();
        builder.Property(t => t.Uri).HasMaxLength(1024).IsRequired();
        builder.HasAuditTimestamps();

        builder.HasOne(t => t.Playlist)
            .WithMany(p => p.Tracks)
            .HasForeignKey(t => t.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

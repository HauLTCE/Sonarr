using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Music;

namespace Sonarr.Infrastructure.Persistence.Configurations.Music;

internal sealed class TrackRatingConfiguration : IEntityTypeConfiguration<TrackRating>
{
    public void Configure(EntityTypeBuilder<TrackRating> builder)
    {
        builder.ToTable("track_rating", "music", t =>
            t.HasCheckConstraint("ck_track_rating_vote", "vote IN (-1, 1)"));
        builder.HasKey(r => new { r.GuildId, r.Uri, r.UserId });
        builder.Property(r => r.Uri).HasMaxLength(1024);
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(r => r.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MusicUserPrefsConfiguration : IEntityTypeConfiguration<MusicUserPrefs>
{
    public void Configure(EntityTypeBuilder<MusicUserPrefs> builder)
    {
        builder.ToTable("user_prefs", "music");
        builder.HasKey(p => new { p.GuildId, p.UserId });
        builder.Property(p => p.Favorites).IsJsonb();
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(p => p.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

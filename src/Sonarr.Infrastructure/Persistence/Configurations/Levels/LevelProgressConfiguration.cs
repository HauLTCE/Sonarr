using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Infrastructure.Persistence.Configurations.Levels;

internal sealed class LevelProgressConfiguration : IEntityTypeConfiguration<LevelProgress>
{
    public void Configure(EntityTypeBuilder<LevelProgress> builder)
    {
        builder.ToTable("progress", "levels");
        builder.HasKey(p => new { p.GuildId, p.UserId });
        builder.HasAuditTimestamps();

        // /leaderboard
        builder.HasIndex(p => new { p.GuildId, p.Xp });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(p => p.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

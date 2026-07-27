using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Infrastructure.Persistence.Configurations.Levels;

internal sealed class LevelRewardConfiguration : IEntityTypeConfiguration<LevelReward>
{
    public void Configure(EntityTypeBuilder<LevelReward> builder)
    {
        builder.ToTable("reward", "levels");
        builder.HasKey(r => new { r.GuildId, r.Level });
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(r => r.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;

namespace Sonarr.Infrastructure.Persistence.Configurations.Levels;

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.ToTable("season", "levels");
        builder.HasKey(s => s.SeasonId);
        builder.Property(s => s.SeasonId).UseIdentityAlwaysColumn();
        builder.Property(s => s.Status).HasMaxLength(16).IsRequired();
        builder.HasAuditTimestamps();

        builder.HasIndex(s => new { s.GuildId, s.Status });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(s => s.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeasonResultConfiguration : IEntityTypeConfiguration<SeasonResult>
{
    public void Configure(EntityTypeBuilder<SeasonResult> builder)
    {
        builder.ToTable("season_result", "levels");
        builder.HasKey(r => new { r.SeasonId, r.UserId });
        builder.HasAuditTimestamps();

        builder.HasOne(r => r.Season)
            .WithMany(s => s.Results)
            .HasForeignKey(r => r.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

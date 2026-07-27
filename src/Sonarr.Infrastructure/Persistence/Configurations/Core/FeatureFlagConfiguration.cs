using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Configurations.Core;

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flag", "core");
        builder.HasKey(f => new { f.GuildId, f.Feature });
        builder.Property(f => f.Feature).HasMaxLength(64).IsRequired();
        builder.Property(f => f.ChangedAt).HasDefaultValueSql("now()");
        builder.HasAuditTimestamps();

        // No FK to core.guild: guild_id 0 is the global row and has no guild.
    }
}

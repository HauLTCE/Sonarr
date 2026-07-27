using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Configurations.Core;

internal sealed class GuildConfigConfiguration : IEntityTypeConfiguration<GuildConfig>
{
    public void Configure(EntityTypeBuilder<GuildConfig> builder)
    {
        builder.ToTable("guild_config", "core");
        builder.HasKey(c => new { c.GuildId, c.Key });
        builder.Property(c => c.Key).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Value).IsJsonb();
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(c => c.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

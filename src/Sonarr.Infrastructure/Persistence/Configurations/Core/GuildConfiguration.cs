using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Configurations.Core;

internal sealed class GuildConfiguration : IEntityTypeConfiguration<Guild>
{
    public void Configure(EntityTypeBuilder<Guild> builder)
    {
        builder.ToTable("guild", "core");
        builder.HasKey(g => g.GuildId);
        builder.Property(g => g.GuildId).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(256).IsRequired();
        builder.HasAuditTimestamps();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Configurations.Chat;

internal sealed class FactConfiguration : IEntityTypeConfiguration<Fact>
{
    public void Configure(EntityTypeBuilder<Fact> builder)
    {
        builder.ToTable("fact", "chat");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).UseIdentityAlwaysColumn();
        builder.Property(f => f.Predicate).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Value).HasMaxLength(512).IsRequired();
        builder.Property(f => f.Active).HasDefaultValue(true);
        builder.HasAuditTimestamps();

        // Recall path: active facts for one person, optionally by predicate.
        builder.HasIndex(f => new { f.GuildId, f.UserId, f.Predicate });
    }
}

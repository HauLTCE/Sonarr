using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Configurations.Core;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("member", "core");
        builder.HasKey(m => new { m.GuildId, m.UserId });
        builder.Property(m => m.Username).HasMaxLength(64).IsRequired();
        builder.Property(m => m.DisplayName).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Timezone).HasMaxLength(64);
        builder.Property(m => m.Locale).HasMaxLength(16);
        builder.HasAuditTimestamps();

        // Birthday announcer scans by month+day; leaderboards sort by message_count.
        builder.HasIndex(m => new { m.GuildId, m.Birthday });
        builder.HasIndex(m => new { m.GuildId, m.MessageCount });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(m => m.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

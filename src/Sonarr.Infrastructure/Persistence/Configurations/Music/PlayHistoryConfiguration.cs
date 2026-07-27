using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Music;

namespace Sonarr.Infrastructure.Persistence.Configurations.Music;

internal sealed class PlayHistoryConfiguration : IEntityTypeConfiguration<PlayHistory>
{
    public void Configure(EntityTypeBuilder<PlayHistory> builder)
    {
        builder.ToTable("play_history", "music");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityAlwaysColumn();
        builder.Property(h => h.Title).HasMaxLength(512).IsRequired();
        builder.Property(h => h.Uri).HasMaxLength(1024).IsRequired();
        builder.HasAuditTimestamps();

        // /musicstats (guild, recent) and /mytracks (requester).
        builder.HasIndex(h => new { h.GuildId, h.PlayedAt });
        builder.HasIndex(h => new { h.GuildId, h.RequesterId });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(h => h.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Configurations.Social;

internal sealed class SocialEventConfiguration : IEntityTypeConfiguration<SocialEvent>
{
    public void Configure(EntityTypeBuilder<SocialEvent> builder)
    {
        builder.ToTable("event", "social");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).UseIdentityAlwaysColumn();
        builder.Property(e => e.Name).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2048);
        builder.Property(e => e.Status).HasMaxLength(16).IsRequired();
        builder.HasAuditTimestamps();

        builder.HasIndex(e => new { e.GuildId, e.StartsAt });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(e => e.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventRsvpConfiguration : IEntityTypeConfiguration<EventRsvp>
{
    public void Configure(EntityTypeBuilder<EventRsvp> builder)
    {
        builder.ToTable("event_rsvp", "social");
        builder.HasKey(r => new { r.EventId, r.UserId });
        builder.Property(r => r.Response).HasMaxLength(16).IsRequired();
        builder.HasAuditTimestamps();

        builder.HasOne(r => r.Event)
            .WithMany(e => e.Rsvps)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

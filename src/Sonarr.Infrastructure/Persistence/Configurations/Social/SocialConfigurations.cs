using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Social;

namespace Sonarr.Infrastructure.Persistence.Configurations.Social;

internal sealed class QuoteBoardConfiguration : IEntityTypeConfiguration<QuoteBoard>
{
    public void Configure(EntityTypeBuilder<QuoteBoard> builder)
    {
        builder.ToTable("quote_board", "social");
        builder.HasKey(q => q.QuoteId);
        builder.Property(q => q.QuoteId).UseIdentityAlwaysColumn();
        builder.Property(q => q.Content).HasMaxLength(2048).IsRequired();
        builder.HasAuditTimestamps();

        // /quote random per guild, plus "quotes of @user" for the chat engine.
        builder.HasIndex(q => new { q.GuildId, q.AuthorId });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(q => q.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CapsuleConfiguration : IEntityTypeConfiguration<Capsule>
{
    public void Configure(EntityTypeBuilder<Capsule> builder)
    {
        builder.ToTable("capsule", "social");
        builder.HasKey(c => c.CapsuleId);
        builder.Property(c => c.CapsuleId).UseIdentityAlwaysColumn();
        builder.Property(c => c.Message).HasMaxLength(2048).IsRequired();
        builder.HasAuditTimestamps();

        builder.HasIndex(c => c.DeliverAt);

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(c => c.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("ticket", "social");
        builder.HasKey(t => t.TicketId);
        builder.Property(t => t.TicketId).UseIdentityAlwaysColumn();
        builder.Property(t => t.Status).HasMaxLength(16).IsRequired();
        builder.Property(t => t.TranscriptRef).HasMaxLength(512);
        builder.HasAuditTimestamps();

        builder.HasIndex(t => t.ThreadId).IsUnique();
        builder.HasIndex(t => new { t.GuildId, t.Status });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(t => t.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

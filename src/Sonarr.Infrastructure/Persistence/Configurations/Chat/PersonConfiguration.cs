using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Infrastructure.Persistence.Configurations.Chat;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("person", "chat");
        builder.HasKey(p => new { p.GuildId, p.UserId });
        builder.Property(p => p.DialogueState).HasMaxLength(64).IsRequired();
        builder.Property(p => p.RelationshipTier).HasMaxLength(32).IsRequired();
        builder.Property(p => p.AssignedNickname).HasMaxLength(64);
        builder.Property(p => p.Registers).IsJsonb();
        builder.Property(p => p.Slots).IsJsonb();
        builder.Property(p => p.FiredLog).IsJsonb();
        builder.Property(p => p.ActivityStack).IsJsonb();
        builder.HasAuditTimestamps();

        // No FK to core.guild: chat state is the engine's own and outlives a guild
        // removal (a re-invite should not wipe her memory of you).
    }
}

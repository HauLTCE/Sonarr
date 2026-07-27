using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Stats;

namespace Sonarr.Infrastructure.Persistence.Configurations.Stats;

internal sealed class CommandUsageConfiguration : IEntityTypeConfiguration<CommandUsage>
{
    public void Configure(EntityTypeBuilder<CommandUsage> builder)
    {
        builder.ToTable("command_usage", "stats");

        // Composite natural key: the counter is upserted per (guild, command, day).
        builder.HasKey(u => new { u.GuildId, u.Command, u.Day });
        builder.Property(u => u.Command).HasMaxLength(64);
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(u => u.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ActivitySampleConfiguration : IEntityTypeConfiguration<ActivitySample>
{
    public void Configure(EntityTypeBuilder<ActivitySample> builder)
    {
        builder.ToTable("activity_sample", "stats");
        builder.HasKey(s => new { s.GuildId, s.HourBucket });
        builder.HasAuditTimestamps();

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(s => s.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

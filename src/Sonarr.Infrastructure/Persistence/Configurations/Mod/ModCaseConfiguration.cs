using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Mod;

namespace Sonarr.Infrastructure.Persistence.Configurations.Mod;

internal sealed class ModCaseConfiguration : IEntityTypeConfiguration<ModCase>
{
    public void Configure(EntityTypeBuilder<ModCase> builder)
    {
        builder.ToTable("case", "mod");
        builder.HasKey(c => c.CaseId);
        builder.Property(c => c.CaseId).UseIdentityAlwaysColumn();
        builder.Property(c => c.Action).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(1024).IsRequired();
        builder.Property(c => c.Context).IsJsonb();
        builder.HasAuditTimestamps();

        // /cases @user — per-user history in one guild.
        builder.HasIndex(c => new { c.GuildId, c.TargetId });

        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(c => c.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

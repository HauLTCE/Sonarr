using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Configurations.Core;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("job", "core");
        builder.HasKey(j => j.JobId);
        builder.Property(j => j.JobId).UseIdentityAlwaysColumn();
        builder.Property(j => j.Kind).HasMaxLength(64).IsRequired();
        builder.Property(j => j.Recurrence).HasMaxLength(128);
        builder.Property(j => j.Status).HasMaxLength(16).IsRequired();
        builder.Property(j => j.Payload).IsJsonb();
        builder.HasAuditTimestamps();

        // The 15 s poller's only query: pending rows whose run_at has passed.
        builder.HasIndex(j => new { j.Status, j.RunAt });
    }
}

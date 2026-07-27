using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities;

namespace Sonarr.Infrastructure.Persistence.Configurations;

/// <summary>
/// Shared created_at/updated_at mapping. Database defaults mean a plain INSERT never
/// leaves them null, including inserts from raw SQL (the job claimer) and the Migrator.
/// </summary>
internal static class AuditedConfig
{
    public static EntityTypeBuilder<T> HasAuditTimestamps<T>(this EntityTypeBuilder<T> builder)
        where T : AuditedEntity
    {
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");
        return builder;
    }
}

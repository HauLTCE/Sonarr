using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sonarr.Domain.Entities.Mod;

namespace Sonarr.Infrastructure.Persistence.Configurations.Mod;

/// <summary>
/// mod.infraction_summary is a view: keyless and read-only. EF does not emit DDL for
/// views, so the CREATE VIEW lives in the Initial migration (see <see cref="ViewSql"/>).
/// </summary>
internal sealed class InfractionSummaryConfiguration : IEntityTypeConfiguration<InfractionSummary>
{
    /// <summary>Kept next to the mapping so the two cannot drift apart.</summary>
    public const string ViewSql = """
        CREATE OR REPLACE VIEW mod.infraction_summary AS
        SELECT guild_id,
               target_id,
               count(*) FILTER (WHERE action = 'warn')                     AS warn_count,
               count(*) FILTER (WHERE action = 'kick')                     AS kick_count,
               count(*) FILTER (WHERE action IN ('ban', 'tempban'))        AS ban_count,
               max(created_at)                                             AS last_case_at
        FROM mod."case"
        GROUP BY guild_id, target_id;
        """;

    public void Configure(EntityTypeBuilder<InfractionSummary> builder)
    {
        builder.HasNoKey();
        builder.ToView("infraction_summary", "mod");
    }
}

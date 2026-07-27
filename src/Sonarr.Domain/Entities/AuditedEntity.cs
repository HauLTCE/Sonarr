namespace Sonarr.Domain.Entities;

/// <summary>
/// Base for every persisted table: docs/04-database.md requires created_at/updated_at
/// on all tables. Not an entity type itself (abstract, no key, never a DbSet).
/// </summary>
public abstract class AuditedEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

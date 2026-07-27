using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Sonarr.Infrastructure.Persistence;

/// <summary>
/// Rewrites table, column, key and index names to snake_case so the DDL matches
/// docs/04-database.md without repeating <c>HasColumnName</c> on every property.
/// Explicit names set in a configuration are only rewritten if they are still PascalCase.
/// </summary>
internal static class SnakeCaseNames
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetViewName() is null)
            {
                entity.SetTableName(ToSnake(entity.GetTableName()));
            }
            else
            {
                entity.SetViewName(ToSnake(entity.GetViewName()));
            }

            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(ToSnake(property.GetColumnName()));
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                key.SetName(ToSnake(key.GetName()));
            }

            foreach (IMutableForeignKey fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnake(fk.GetConstraintName()));
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnake(index.GetDatabaseName()));
            }
        }
    }

    private static string? ToSnake(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        StringBuilder sb = new(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool boundary = i > 0
                    && name[i - 1] != '_'
                    && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (boundary)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sonarr.Infrastructure.Persistence;

/// <summary>
/// jsonb mapping for the schema's json columns. Everything goes through an explicit
/// serialize-to-text converter (rather than the provider's DOM mapping) so all jsonb
/// columns behave identically and change tracking works on mutable payloads.
/// </summary>
public static class JsonbConverters
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a property to jsonb, serialized with the shared options.</summary>
    public static PropertyBuilder<T> IsJsonb<T>(this PropertyBuilder<T> builder)
        where T : class
        => builder
            .HasColumnType("jsonb")
            .HasConversion(
                v => Serialize(v),
                v => Deserialize<T>(v),
                new ValueComparer<T>(
                    (a, b) => Serialize(a) == Serialize(b),
                    v => Serialize(v).GetHashCode(StringComparison.Ordinal),
                    v => Deserialize<T>(Serialize(v))))
            .IsRequired();

    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, Options);

    private static T Deserialize<T>(string json)
        where T : class
        => JsonSerializer.Deserialize<T>(json, Options) ?? NewEmpty<T>();

    private static T NewEmpty<T>()
        where T : class
    {
        if (typeof(T) == typeof(JsonObject))
        {
            return (T)(object)new JsonObject();
        }

        if (typeof(T) == typeof(JsonArray))
        {
            return (T)(object)new JsonArray();
        }

        return Activator.CreateInstance<T>();
    }
}

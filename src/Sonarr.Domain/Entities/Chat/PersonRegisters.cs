using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.person.registers — the orthogonal affect dimensions, stored as one jsonb
/// column. Typed because docs/04-database.md and docs/checklist.md name the fields.
/// Range is engine-defined (Sonarr.Elaine owns clamping); the DB just stores them.
/// </summary>
/// <remarks>
/// The persona is the sole declaration site for which registers exist, and it declares more
/// than the five named here (amusement, confidence, energy — the ones the playful and smug
/// moods read). Those round-trip through <see cref="Others"/> as flat keys of the same jsonb
/// object, so adding a register to the persona needs no migration and never silently drops
/// the value on save.
/// </remarks>
public sealed record PersonRegisters
{
    public double Anger { get; init; }

    public double Boredom { get; init; }

    public double Fondness { get; init; }

    public double Trust { get; init; }

    public double Grudge { get; init; }

    /// <summary>Registers the persona declares beyond the named five, flat in the same object.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Others { get; init; } = [];

    /// <summary>Every register as one map — what the engine consumes.</summary>
    public IReadOnlyDictionary<string, double> ToDictionary()
    {
        Dictionary<string, double> all = new(StringComparer.Ordinal)
        {
            [Names.Anger] = Anger,
            [Names.Boredom] = Boredom,
            [Names.Fondness] = Fondness,
            [Names.Trust] = Trust,
            [Names.Grudge] = Grudge,
        };

        foreach ((string name, JsonElement value) in Others)
        {
            // A hand-edited row (or an older shape) can hold anything here; a non-number is
            // skipped rather than throwing, because bad jsonb must not stop her replying.
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            {
                all[name] = number;
            }
        }

        return all;
    }

    /// <summary>Splits a register map back into the typed five plus the rest.</summary>
    public static PersonRegisters From(IReadOnlyDictionary<string, double> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        double Get(string name) => registers.TryGetValue(name, out double value) ? value : 0;

        return new PersonRegisters
        {
            Anger = Get(Names.Anger),
            Boredom = Get(Names.Boredom),
            Fondness = Get(Names.Fondness),
            Trust = Get(Names.Trust),
            Grudge = Get(Names.Grudge),
            Others = registers
                .Where(r => !Names.Typed.Contains(r.Key))
                .ToDictionary(
                    r => r.Key,
                    r => JsonSerializer.SerializeToElement(r.Value),
                    StringComparer.Ordinal),
        };
    }

    /// <summary>Column-backed register names. The persona may declare more.</summary>
    public static class Names
    {
        public const string Anger = "anger";
        public const string Boredom = "boredom";
        public const string Fondness = "fondness";
        public const string Trust = "trust";
        public const string Grudge = "grudge";

        public static readonly IReadOnlySet<string> Typed =
            new HashSet<string>(StringComparer.Ordinal) { Anger, Boredom, Fondness, Trust, Grudge };
    }
}

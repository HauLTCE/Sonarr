namespace Sonarr.Elaine.Determinism;

/// <summary>
/// Turn-seeded RNG: <c>seed = mix(turn, salt, purpose)</c>, stateless splitmix64 draw.
/// </summary>
/// <remarks>
/// Stateless is deliberate. A stateful <see cref="Random"/> would make every draw depend
/// on how many draws came before it, so adding one unrelated pool lookup would change
/// every later line in the turn and break replay of stored traces. Here each (turn,
/// purpose) pair owns its own value independently.
/// <para><c>salt</c> is the per-conversation seed (e.g. mood-of-the-day, hashed user id)
/// supplied by the adapter, so two users on the same turn do not hear the same line.</para>
/// </remarks>
public sealed class TurnSeededRandom(long turn, ulong salt = 0) : IDeterministicRandom
{
    public long Turn { get; } = turn;

    /// <summary>A source for the next logical turn, keeping the same salt.</summary>
    public TurnSeededRandom Advance() => new(Turn + 1, salt);

    public int Next(string purpose, int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exclusiveMax, 1);
        // Rejection-free modulo: pool sizes are tiny (tens), so the modulo bias is far
        // below anything an authored-text engine could notice.
        // ponytail: biased-but-uniform-enough; swap for Lemire rejection if a draw ever
        // needs statistical quality (it does not — this picks lines, not lottery numbers).
        return (int)(Draw(purpose) % (ulong)exclusiveMax);
    }

    public int NextInclusive(string purpose, int minInclusive, int maxInclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minInclusive, maxInclusive);
        long span = (long)maxInclusive - minInclusive + 1;
        return (int)(minInclusive + (long)(Draw(purpose) % (ulong)span));
    }

    public T Pick<T>(string purpose, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("cannot pick from an empty list", nameof(items));
        }

        return items[Next(purpose, items.Count)];
    }

    private ulong Draw(string purpose)
    {
        ulong seed = unchecked((ulong)Turn * 0x9E3779B97F4A7C15UL) ^ salt ^ StableHash.Of(purpose);
        return SplitMix64(seed);
    }

    private static ulong SplitMix64(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

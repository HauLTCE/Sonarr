namespace Sonarr.Elaine.Determinism;

/// <summary>
/// The only source of randomness in the engine. Every draw is a pure function of
/// (logical turn, session salt, <c>purpose</c>) — there is no hidden stream position, so
/// the same turn always draws the same values no matter what order the engine asks in.
/// </summary>
/// <remarks>
/// <c>purpose</c> is a caller-supplied label (usually a pool name or slot key) that
/// separates otherwise-identical draws within one turn. Two draws with the same purpose
/// in the same turn are the same value on purpose: that is what makes replay exact.
/// </remarks>
public interface IDeterministicRandom
{
    /// <summary>The logical turn this source is bound to.</summary>
    long Turn { get; }

    /// <summary>Uniform integer in <c>[0, exclusiveMax)</c>.</summary>
    int Next(string purpose, int exclusiveMax);

    /// <summary>Uniform integer in <c>[minInclusive, maxInclusive]</c>.</summary>
    int NextInclusive(string purpose, int minInclusive, int maxInclusive);

    /// <summary>One element of <paramref name="items"/>. Throws if empty.</summary>
    T Pick<T>(string purpose, IReadOnlyList<T> items);
}

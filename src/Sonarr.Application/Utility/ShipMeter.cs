using System.Globalization;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Utility;

/// <summary>
/// <c>/ship</c> (docs/07): a compatibility percentage seeded from the id pair, with an authored
/// line for the band it lands in.
/// </summary>
/// <remarks>
/// No table, by design (docs/checklist "deterministic seed from the id pair, no table"). The same
/// two people always get the same number, in either order, forever — which is the only way the
/// joke works: a re-roll would make it obvious the number means nothing, and storing it would be
/// a row per pair per guild for a command nobody audits.
/// <para>Deliberately guild-independent. Two people are the same two people in every server they
/// share, and a number that changed per server would invite shopping for a better one.</para>
/// </remarks>
public sealed class ShipMeter(PersonaHolder persona)
{
    /// <summary>Authored snark per band. Code-referenced, so the validator lists them as orphans.</summary>
    public const string LowPool = "ship_low";

    public const string MidPool = "ship_mid";

    public const string HighPool = "ship_high";

    /// <summary>Top of the low band, and of the middle one. Roughly even thirds.</summary>
    public const int LowCeiling = 32;

    public const int MidCeiling = 65;

    /// <summary>
    /// The percentage for a pair, stable in both directions.
    /// </summary>
    /// <remarks>
    /// The ids are sorted before hashing, so <c>/ship a b</c> and <c>/ship b a</c> are the same
    /// question and get the same answer. FNV-1a rather than <c>GetHashCode</c> for the same reason
    /// it is used everywhere else on this path: process-randomized hashing would re-roll on every
    /// restart.
    /// </remarks>
    public static int Percent(long a, long b)
    {
        (long low, long high) = a <= b ? (a, b) : (b, a);
        string pair = string.Create(
            CultureInfo.InvariantCulture, $"ship:{low}:{high}");
        return (int)(StableHash.Of(pair) % 101);
    }

    /// <summary>Her verdict on a pair: the number and the line that goes with it.</summary>
    public ShipVerdict Rate(long a, long b)
    {
        int percent = Percent(a, b);
        string pool = percent <= LowCeiling ? LowPool : percent <= MidCeiling ? MidPool : HighPool;

        // Seeded on the percentage, not the turn: the line has to be as stable as the number, or
        // she would keep changing her mind about a verdict she claims is arithmetic.
        string line = new LinePicker(persona.Current).Pick(
            pool, modeId: null, new TurnSeededRandom(percent, StableHash.Of(pool)), NoSlots)
            ?? string.Empty;

        return new ShipVerdict(percent, line);
    }

    /// <summary>
    /// The portmanteau, or null when neither name has enough to cut. Half of the first plus half
    /// of the second, which is how everyone does this.
    /// </summary>
    public static string? Name(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        string a = new([.. first.Where(char.IsLetter)]);
        string b = new([.. second.Where(char.IsLetter)]);
        return a.Length < 2 || b.Length < 2
            ? null
            : string.Concat(a[..((a.Length + 1) / 2)], b[(b.Length / 2)..]);
    }

    private static readonly IReadOnlyDictionary<string, string> NoSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <param name="Percent">0–100, stable for the pair.</param>
/// <param name="Line">Her authored comment, or empty when the persona has no pool for the band.</param>
public sealed record ShipVerdict(int Percent, string Line);

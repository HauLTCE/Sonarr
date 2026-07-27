using System.Collections.Frozen;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// The affect registers as one immutable snapshot: register name → value.
/// </summary>
/// <remarks>
/// Orthogonal by design (docs/10). The old engine folded mood into the FSM state, so being
/// angry and fond at once was unrepresentable and every combination needed its own state.
/// Here each register moves on its own and the <em>mood mode</em> is derived from all of
/// them at read time, never stored.
/// <para>Which registers exist is the persona's call — <c>personality.baselines</c> is the
/// declaration. <see cref="Names"/> only pins the five the database column names.</para>
/// </remarks>
public sealed record Registers
{
    /// <summary>Registers <c>chat.person.registers</c> has typed columns for.</summary>
    public static class Names
    {
        public const string Anger = "anger";
        public const string Boredom = "boredom";
        public const string Fondness = "fondness";
        public const string Trust = "trust";
        public const string Grudge = "grudge";
    }

    /// <summary>Registers are authored on a 0-10 scale; trust runs negative for nemeses.</summary>
    public const double Min = -20;

    public const double Max = 20;

    public static readonly Registers Empty = new(FrozenDictionary<string, double>.Empty);

    private readonly FrozenDictionary<string, double> _values;

    private Registers(FrozenDictionary<string, double> values) => _values = values;

    /// <summary>Everything a <c>register</c> guard or a mode <c>when</c> clause can read.</summary>
    public IReadOnlyDictionary<string, double> Values => _values;

    public double this[string register] =>
        _values.TryGetValue(register, out double v) ? v : 0;

    /// <summary>Resting state for a person she has never met.</summary>
    public static Registers FromBaselines(PersonaRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return From(root.Personality.Baselines);
    }

    public static Registers From(IEnumerable<KeyValuePair<string, double>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new Registers(values
            .ToFrozenDictionary(v => v.Key, v => Clamp(v.Value), StringComparer.Ordinal));
    }

    /// <summary>This snapshot with <paramref name="deltas"/> applied and clamped.</summary>
    public Registers With(IEnumerable<AffectDelta> deltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        Dictionary<string, double> next = new(_values, StringComparer.Ordinal);
        foreach (AffectDelta delta in deltas)
        {
            next[delta.Register] = Clamp(this[delta.Register] + delta.Delta);
        }

        return From(next);
    }

    /// <summary>
    /// One turn of decay: every register moves toward its baseline by its declared step,
    /// never past it.
    /// </summary>
    /// <remarks>
    /// Applied per logical turn, not per second, so a conversation resumed next week starts
    /// from where it left off. Wall-clock decay (multi-day grudge cooling) is the adapter's
    /// job — it feeds elapsed time in as extra steps, because the engine has no clock.
    /// </remarks>
    public Registers Decay(PersonaRoot root, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (steps <= 0)
        {
            return this;
        }

        Dictionary<string, double> next = new(_values, StringComparer.Ordinal);
        foreach ((string register, double baseline) in root.Personality.Baselines)
        {
            double current = this[register];
            if (!root.Personality.Decay.TryGetValue(register, out double step) || step <= 0)
            {
                next[register] = current;
                continue;
            }

            double moved = current > baseline
                ? Math.Max(baseline, current - (step * steps))
                : Math.Min(baseline, current + (step * steps));
            next[register] = moved;
        }

        return From(next);
    }

    private static double Clamp(double value) => Math.Clamp(value, Min, Max);
}

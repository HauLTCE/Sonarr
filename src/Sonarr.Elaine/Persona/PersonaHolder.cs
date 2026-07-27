namespace Sonarr.Elaine.Persona;

/// <summary>
/// Holds the live persona and enforces the fail-fast contract from docs/10: an invalid
/// persona refuses to load at boot, and at hot-reload the previously-valid graph stays
/// live so a broken edit can never take her down.
/// </summary>
public sealed class PersonaHolder
{
    private volatile PersonaGraph _current;

    private PersonaHolder(PersonaGraph graph, PersonaValidationResult result)
    {
        _current = graph;
        LastResult = result;
    }

    /// <summary>The live graph. Never null, never partially updated.</summary>
    public PersonaGraph Current => _current;

    /// <summary>Result of the most recent load attempt, successful or not.</summary>
    public PersonaValidationResult LastResult { get; private set; }

    /// <summary>How many times a reload has been rejected for being invalid.</summary>
    public int RejectedReloads { get; private set; }

    /// <summary>
    /// Boot-time load. Returns null when the persona is invalid — the caller must refuse to
    /// start rather than run with a half-built graph.
    /// </summary>
    public static PersonaHolder? TryCreate(IPersonaSource source, out PersonaValidationResult result)
    {
        result = PersonaLoader.Load(source);
        return result.IsValid && result.Graph is not null
            ? new PersonaHolder(result.Graph, result)
            : null;
    }

    /// <summary>
    /// Hot-reload. Swaps atomically only if the new persona validates; otherwise the live
    /// graph is untouched and the failure is returned for logging.
    /// </summary>
    /// <returns>True if the swap happened.</returns>
    public bool TryReload(IPersonaSource source, out PersonaValidationResult result)
    {
        result = PersonaLoader.Load(source);
        LastResult = result;
        if (!result.IsValid || result.Graph is null)
        {
            RejectedReloads++;
            return false;
        }

        _current = result.Graph;
        return true;
    }
}

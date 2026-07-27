using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Matching;

/// <summary>
/// The understand step of the pipeline: normalize → lexical matchers scored → below
/// threshold? semantic rescue. The only entry point the decide layer needs.
/// </summary>
public sealed class IntentRecognizer(PersonaGraph persona, ISemanticMatcher? semantic = null)
{
    private readonly PersonaGraph _persona = persona
        ?? throw new ArgumentNullException(nameof(persona));

    private readonly LexicalMatcher _lexical = new(persona);
    private readonly ISemanticMatcher _semantic = semantic ?? NullSemanticMatcher.Instance;

    /// <summary>Recognize intents in raw user text.</summary>
    public MatchOutcome Recognize(string? text, MatchContext context) =>
        Recognize(Normalizer.Normalize(text), context);

    /// <summary>Recognize intents in already-normalized text.</summary>
    public MatchOutcome Recognize(Normalized input, MatchContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        MatchOutcome lexical = _lexical.Match(input, context);
        if (lexical.IsConfident)
        {
            return lexical;
        }

        ActivityDef? activity = _persona.Root.Activities
            .FirstOrDefault(a => a.Id == context.ActivityId);
        IReadOnlyList<IntentDef> eligible = activity is null
            ? _persona.Intents
            : [.. _persona.Intents.Where(i => activity.Allows(i.Id))];

        MatchCandidate? rescued = _semantic.Rescue(input, context, eligible);
        if (rescued is null)
        {
            return lexical;
        }

        // A rescue is added, not substituted: a weak lexical hit still carries its captures
        // and can still be reported as a side effect.
        List<MatchCandidate> ranked = [rescued, .. lexical.Ranked];
        return new MatchOutcome
        {
            Ranked = ranked,
            SideEffects = [.. ranked.Skip(1).Where(c => c.Intent.SideEffect)],
        };
    }
}

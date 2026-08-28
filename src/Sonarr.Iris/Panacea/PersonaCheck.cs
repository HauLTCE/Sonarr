using Sonarr.Elaine.Persona;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// The persona on disk, loaded through the same loader the bot uses. The one check with no
/// remedy by design: an invalid persona is authored text with an error in it, and a doctor
/// that rewrites her words is a doctor that has overstepped. The report names the error
/// count and points at <c>sonarr persona</c>, which prints them all.
/// </summary>
internal sealed class PersonaCheck : IDoctorCheck
{
    public string Name => "persona";

    public Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        try
        {
            (_, PersonaValidationResult result) = PersonaCommand.Load(null);
            if (!result.IsValid)
            {
                return Task.FromResult(Diagnosis.Broken(
                    $"{Output.Count(result.Errors.Count(), "error", "errors")}",
                    "run `sonarr persona` for the full list — the persona is authored text, "
                    + "so the fix is an edit, and the bot will not boot until it is made"));
            }

            PersonaGraph graph = result.Graph!;
            return Task.FromResult(Diagnosis.Ok(
                $"valid — {graph.Intents.Count} intents, {graph.Pools.Count} pools"));
        }
        catch (CliError ex)
        {
            return Task.FromResult(Diagnosis.Broken(
                Doctor.OneLine(ex.Message),
                "the persona directory must exist where the CLI runs — check PERSONA_PATH "
                + "or run this from inside the deployment tree"));
        }
    }
}

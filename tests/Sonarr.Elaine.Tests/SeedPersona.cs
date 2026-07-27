using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Reads the real <c>persona/</c> directory at the repo root. Tests are the only place the
/// filesystem is touched — the engine takes an <see cref="IPersonaSource"/> so it never
/// has to.
/// </summary>
internal static class SeedPersona
{
    private static readonly Lazy<PersonaValidationResult> LazyResult =
        new(() => PersonaLoader.Load(new DirectoryPersonaSource(Directory())));

    /// <summary>Load result for the shipped persona. Cached: the files do not change mid-run.</summary>
    public static PersonaValidationResult Result => LazyResult.Value;

    public static PersonaGraph Graph =>
        Result.Graph ?? throw new InvalidOperationException(
            "the shipped persona does not validate:\n" + Result.Report());

    /// <summary>Walks up from the test binary to the repo root's <c>persona</c> directory.</summary>
    public static string Directory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "persona");
            if (File.Exists(Path.Combine(candidate, PersonaLoader.RootFile)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the persona/ directory");
    }
}

/// <summary>Reads persona files off disk. Test-only; production wiring lives in the host.</summary>
internal sealed class DirectoryPersonaSource(string root) : IPersonaSource
{
    public IReadOnlyList<PersonaFile> Read() =>
        [.. Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new PersonaFile(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText(path)))];
}

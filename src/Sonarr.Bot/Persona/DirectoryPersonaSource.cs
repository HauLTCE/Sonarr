using Sonarr.Elaine.Persona;

namespace Sonarr.Bot.Persona;

/// <summary>
/// Reads the mounted persona directory (<c>PERSONA_PATH</c>, <c>./persona:/app/persona</c>).
/// The engine takes an <see cref="IPersonaSource"/> precisely so the filesystem stays on this
/// side of the boundary (docs/02-architecture.md: Elaine touches no I/O).
/// </summary>
public sealed class DirectoryPersonaSource(string root) : IPersonaSource
{
    public string Root { get; } = root;

    public IReadOnlyList<PersonaFile> Read()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(Root, "*.yaml", SearchOption.AllDirectories)
                .Select(path => new PersonaFile(
                    Path.GetRelativePath(Root, path).Replace('\\', '/'),
                    File.ReadAllText(path)))
        ];
    }
}

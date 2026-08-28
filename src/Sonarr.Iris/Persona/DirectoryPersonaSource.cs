using Sonarr.Elaine.Persona;

namespace Sonarr.Iris.Persona;

/// <summary>
/// Reads the persona directory from disk. The engine takes an <see cref="IPersonaSource"/>
/// precisely so the filesystem stays on this side of the boundary (docs/02-architecture.md:
/// Elaine touches no I/O) — the bot loads it at boot and on hot reload, and <c>sonarr persona</c>
/// loads it to report what the bot would see, so the source lives in Iris, which both reference.
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

namespace Sonarr.Elaine.Persona;

/// <summary>
/// Supplies persona file contents to the loader. The engine never touches the filesystem
/// itself, so loading is a pure function of these bytes — tests feed in-memory files and
/// the host feeds a real directory (or a mounted volume) from outside this assembly.
/// </summary>
public interface IPersonaSource
{
    /// <summary>
    /// Every persona file, with paths relative to the persona root and forward slashes.
    /// Order is irrelevant: the loader sorts by path so declaration order is reproducible.
    /// </summary>
    IReadOnlyList<PersonaFile> Read();
}

/// <summary>An in-memory persona source. Used by tests and by hot-reload snapshots.</summary>
public sealed class InMemoryPersonaSource(IEnumerable<PersonaFile> files) : IPersonaSource
{
    private readonly IReadOnlyList<PersonaFile> _files = [.. files];

    public InMemoryPersonaSource(params (string Path, string Text)[] files)
        : this(files.Select(f => new PersonaFile(f.Path, f.Text)))
    {
    }

    public IReadOnlyList<PersonaFile> Read() => _files;
}

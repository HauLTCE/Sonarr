namespace Sonarr.Iris.Tests;

/// <summary>
/// A throwaway directory for file-based tests. The commands under test read real files, so the
/// tests write real files and delete them afterwards.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "iris-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    /// <summary>Writes a file under the temp directory, creating parents, and returns its path.</summary>
    public string File(string relativeName, string content)
    {
        string full = System.IO.Path.Combine(Path, relativeName.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Leftover temp files are noise, not a test failure.
        }
    }
}

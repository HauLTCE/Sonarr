namespace Sonarr.Iris.Panacea;

/// <summary>
/// The embedding model on disk — the semantic matching tier's weights. Absent, the bot still
/// runs and matching stays lexical-only, so this is a real ailment with a real remedy and not
/// a boot blocker: the doctor runs <c>scripts/fetch-model.sh</c>, which is what a person would
/// do, and reports honestly when it cannot find the script or the download fails.
/// </summary>
internal sealed class ModelCheck : IDoctorCheck
{
    /// <summary>The two files <c>OnnxEmbedderOptions</c> requires.</summary>
    private static readonly string[] Files = ["model.onnx", "vocab.txt"];

    /// <summary>~90 MB of fp32 weights; anything much smaller is a truncated download.</summary>
    private const long PlausibleModelBytes = 50L * 1024 * 1024;

    public string Name => "model";

    public Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        (string dir, string? note) = Directory();
        string where = note is null ? dir : $"{dir} ({note})";

        List<string> missing = [.. Files.Where(f => !File.Exists(Path.Combine(dir, f)))];
        if (missing.Count > 0)
        {
            return Task.FromResult(Diagnosis.Broken(
                $"{string.Join(" and ", missing)} missing from {where}",
                "run scripts/fetch-model.sh (the doctor tries this itself) — without it she still "
                + "answers, but matching is lexical-only, with no semantic tier"));
        }

        long bytes = new FileInfo(Path.Combine(dir, "model.onnx")).Length;
        return Task.FromResult(bytes < PlausibleModelBytes
            ? Diagnosis.Broken(
                $"model.onnx is only {bytes / 1024d / 1024d:F1} MB — a truncated download",
                "delete it and re-run scripts/fetch-model.sh; a partial file loads and then "
                + "fails at the first embed")
            : Diagnosis.Ok($"{bytes / 1024d / 1024d:F0} MB in {where}"));
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        (string dir, _) = Directory();

        // A truncated file is the one case where the fetch script does nothing: it skips any file
        // that exists and is non-empty, so the doctor has to clear the bad one first.
        List<string> steps = [];
        string model = Path.Combine(dir, "model.onnx");
        if (File.Exists(model) && new FileInfo(model).Length < PlausibleModelBytes)
        {
            try
            {
                File.Delete(model);
                steps.Add("deleted the truncated model.onnx so the fetch would re-download it");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                steps.Add($"could not delete the truncated model.onnx — {Doctor.OneLine(ex.Message)}");
                return steps;
            }
        }

        if (FetchScript() is not { } script)
        {
            steps.Add("looked for scripts/fetch-model.sh — it is not next to the CLI or above it");
            return steps;
        }

        steps.Add($"running {script}");
        steps.AddRange(await Shell.RunAsync("sh", [script, dir], TimeSpan.FromMinutes(15), ct));
        return steps;
    }

    /// <summary>
    /// Where the model is, and a note when that is not the first place asked. Candidates in
    /// order: <c>MODEL_PATH</c>, the tree's own models/ directory walking up from here, then the
    /// host install path — the same resolution <c>PersonaCommand.ResolveRoot</c> uses, and for
    /// the same reason.
    /// </summary>
    /// <remarks>
    /// The host path is load-bearing rather than a nicety. On the server <c>.env</c> is shared
    /// with the compose stack and holds the <em>container's</em> view (<c>/app/models/...</c>);
    /// the unit file overrides it with the real path, and unit environment is invisible to this
    /// process. Without the fallback the doctor reported a missing model while the bot had it
    /// loaded and the semantic tier was running — a red row for a healthy thing, which is the
    /// same class of lie as a green row for a broken one.
    /// </remarks>
    private static (string Dir, string? Note) Directory()
    {
        string? fromEnv = CliHost.Configuration["MODEL_PATH"];
        if (!string.IsNullOrWhiteSpace(fromEnv) && System.IO.Directory.Exists(fromEnv))
        {
            return (fromEnv, null);
        }

        string? note = string.IsNullOrWhiteSpace(fromEnv)
            ? null
            : $"MODEL_PATH is {fromEnv}, which does not exist here";

        if (FindUpwards(ModelRelative) is { } inTree)
        {
            return (inTree, note);
        }

        return System.IO.Directory.Exists(HostPath)
            ? (HostPath, note)
            : (fromEnv ?? ModelRelative, note);
    }

    /// <summary>Where install-host.sh puts the model on the server.</summary>
    private const string HostPath = "/opt/sonarr/models/minilm-l6-v2";

    private static readonly string ModelRelative = Path.Combine("models", "minilm-l6-v2");

    private static string? FetchScript() => FindUpwards(Path.Combine("scripts", "fetch-model.sh"));

    /// <summary>
    /// Walks up from the working directory looking for a relative path — the same search
    /// <c>DotEnv.FindUpwards</c> does, so the CLI finds the tree from anywhere inside it.
    /// </summary>
    private static string? FindUpwards(string relative)
    {
        DirectoryInfo? at = new(System.IO.Directory.GetCurrentDirectory());
        while (at is not null)
        {
            string candidate = Path.Combine(at.FullName, relative);
            if (File.Exists(candidate) || System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            at = at.Parent;
        }

        return null;
    }
}

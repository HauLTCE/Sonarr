using Sonarr.Elaine.Persona;

namespace Sonarr.Bot.Persona;

/// <summary>
/// Watches the persona directory and hot-reloads it. The contract from docs/10 is one-sided:
/// a valid edit swaps atomically, an invalid one is logged and the live graph is untouched, so
/// a bad save can never take her down mid-conversation.
/// </summary>
public sealed class PersonaWatcher(
    PersonaHolder holder,
    DirectoryPersonaSource source,
    ILogger<PersonaWatcher> logger) : BackgroundService
{
    // ponytail: editors write-then-rename, so one save fires several events. A fixed settle
    // delay is enough here (a reload is a few ms of YAML parsing). If persona/ ever grows to
    // where that matters, debounce per-file instead.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(750);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(source.Root))
        {
            logger.LogWarning(
                "Persona hot-reload is off: {Root} does not exist. The boot-time graph stays live.",
                source.Root);
            return;
        }

        using FileSystemWatcher watcher = new(source.Root, "*.yaml")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };

        // A channel of one: we only ever need to know "something changed since the last reload",
        // not which file or how many times.
        SemaphoreSlim dirty = new(0, 1);
        void Touch(object _, FileSystemEventArgs __)
        {
            if (dirty.CurrentCount == 0)
            {
                try
                {
                    dirty.Release();
                }
                catch (SemaphoreFullException)
                {
                    // Raced with another event; the pending reload already covers it.
                }
            }
        }

        watcher.Changed += Touch;
        watcher.Created += Touch;
        watcher.Deleted += Touch;
        watcher.Renamed += Touch;
        watcher.EnableRaisingEvents = true;

        logger.LogInformation("Persona hot-reload watching {Root}.", source.Root);

        while (!stoppingToken.IsCancellationRequested)
        {
            await dirty.WaitAsync(stoppingToken);
            await Task.Delay(Settle, stoppingToken);
            Reload();
        }
    }

    private void Reload()
    {
        try
        {
            if (holder.TryReload(source, out PersonaValidationResult result))
            {
                logger.LogInformation(
                    "Persona reloaded: {Intents} intents, {Pools} pools, {Warnings} warning(s).",
                    result.Graph!.Intents.Count, result.Graph.Pools.Count, result.Warnings.Count());
            }
            else
            {
                logger.LogError(
                    "Persona edit REJECTED, previous persona still live (rejected {Count}x since boot).\n{Report}",
                    holder.RejectedReloads, result.Report());
            }
        }
        catch (IOException ex)
        {
            // Caught the editor mid-write. The next event reloads; nothing was swapped.
            logger.LogWarning("Persona reload skipped, files were busy: {Reason}", ex.Message);
        }
    }
}

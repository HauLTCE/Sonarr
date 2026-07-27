using Microsoft.EntityFrameworkCore;
using Sonarr.Infrastructure.Persistence;
using Sonarr.Migrator.Import;

// One-shot console app (docs/02-architecture.md). Two verbs:
//   migrate  apply pending EF migrations, so the schema exists without the EF CLI on the server
//   import   pull the old SQLite/JSON data in (docs/04 "Data-migration map"), idempotent
//
// Usage: Sonarr.Migrator migrate [--connection "Host=...;"]
//        Sonarr.Migrator import  [--connection "Host=...;"] [--source /root/sonarr]
//        --connection falls back to PG_CONNECTION from the environment.

string verb = args.FirstOrDefault() ?? "migrate";
if (verb is "-h" or "--help" or "help")
{
    Console.WriteLine("usage: Sonarr.Migrator migrate [--connection <npgsql-connection-string>]");
    Console.WriteLine("       Sonarr.Migrator import  [--connection <...>] [--source <legacy-dir>]");
    return 0;
}

if (verb is not ("migrate" or "import"))
{
    Console.Error.WriteLine($"unknown verb '{verb}'. Expected 'migrate' or 'import'.");
    return 2;
}

string? connection = Option("--connection") ?? Environment.GetEnvironmentVariable("PG_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("no connection string: pass --connection or set PG_CONNECTION.");
    return 2;
}

DbContextOptionsBuilder<SonarrDbContext> options = new();
options.UseNpgsql(connection, npgsql => npgsql.UseVector());

try
{
    await using SonarrDbContext db = new(options.Options);
    return verb == "migrate" ? await MigrateAsync(db) : await ImportAsync(db);
}
catch (Exception ex)
{
    // The connection string carries the password, so report the exception only — never
    // the string we were handed.
    Console.Error.WriteLine($"{verb} failed: {ex.Message}");
    return 1;
}

static async Task<int> MigrateAsync(SonarrDbContext db)
{
    string[] pending = [.. await db.Database.GetPendingMigrationsAsync()];
    if (pending.Length == 0)
    {
        Console.WriteLine("schema up to date, nothing to apply.");
        return 0;
    }

    Console.WriteLine($"applying {pending.Length} migration(s):");
    foreach (string name in pending)
    {
        Console.WriteLine($"  {name}");
    }

    await db.Database.MigrateAsync();
    Console.WriteLine("done.");
    return 0;
}

async Task<int> ImportAsync(SonarrDbContext db)
{
    // Default is /root/sonarr — the live directory. /root/sonarr-data is a stale copy and
    // importing it would silently roll everyone's data back (docs/11).
    string root = Option("--source") ?? Environment.GetEnvironmentVariable("LEGACY_DIR") ?? "/root/sonarr";
    LegacySource source = new(root);
    Console.WriteLine($"reading legacy data from {source.Root}");

    if (!source.HasBotDb && !source.HasElaineDb && !source.HasPlaylists)
    {
        Console.Error.WriteLine("nothing to import: no bot_data.db, data/elaine.db or playlists.json there.");
        return 2;
    }

    if (await db.Database.GetPendingMigrationsAsync() is { } pending && pending.Any())
    {
        Console.Error.WriteLine("schema is behind — run 'migrate' first.");
        return 2;
    }

    LegacyImporter importer = new(source, db, DateTimeOffset.UtcNow);
    ImportReport report = await importer.RunAsync();
    Console.WriteLine(report);
    Console.WriteLine($"{report.TotalWritten} row(s) written. Safe to re-run.");
    return 0;
}

string? Option(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

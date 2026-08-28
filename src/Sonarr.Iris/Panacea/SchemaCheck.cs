using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sonarr.Infrastructure.Persistence;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// The schema, examined by asking EF what migrations are pending. A database the code outran
/// is the one ailment the doctor can cure outright: applying migrations is exactly what
/// <c>Sonarr.Migrator migrate</c> does, so the remedy is the same call, made here instead of
/// sending the operator to run it. Runs after the Postgres check by construction — without a
/// database there is nothing to ask, and the row says so rather than failing a second time.
/// </summary>
internal sealed class SchemaCheck : IDoctorCheck
{
    public string Name => "schema";

    public async Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        if (CliHost.Configuration["PG_CONNECTION"] is not { Length: > 0 })
        {
            return Diagnosis.Blocked("PG_CONNECTION is not set — see the env row");
        }

        try
        {
            using IServiceScope scope = CliHost.Scope();
            SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();

            string[] pending = [.. await db.Database.GetPendingMigrationsAsync(ct)];
            return pending.Length == 0
                ? Diagnosis.Ok("up to date")
                : Diagnosis.Broken(
                    $"{Output.Count(pending.Length, "migration", "migrations")} pending "
                    + $"(oldest: {pending[0]})",
                    "re-run this command — applying migrations is one of the things the doctor does, "
                    + "or run `Sonarr.Migrator migrate` by hand");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A pending-migrations query that cannot run is a database problem, and the
            // postgres row already owns it.
            return Diagnosis.Blocked($"cannot ask the database — {Doctor.OneLine(ex.Message)}");
        }
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = CliHost.Scope();
            SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();

            string[] pending = [.. await db.Database.GetPendingMigrationsAsync(ct)];
            if (pending.Length == 0)
            {
                return ["checked for pending migrations — none"];
            }

            await db.Database.MigrateAsync(ct);
            return [$"applied {Output.Count(pending.Length, "migration", "migrations")}"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [$"tried to apply the pending migrations — {Doctor.OneLine(ex.Message)}"];
        }
    }
}

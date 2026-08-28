using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sonarr.Infrastructure.Persistence;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// Postgres, examined by connecting — the same <c>CanConnect</c> the plain health check used.
/// The remedy is the stopped-container start: a database whose container merely exited comes
/// back with one <c>docker start</c>, which is the common 03:30-restart failure on the server
/// and the common "Docker Desktop quit" failure on a dev box. Anything else — no container,
/// docker absent, wrong credentials — is reported honestly as tried-and-could-not.
/// </summary>
internal sealed class PostgresCheck : IDoctorCheck
{
    public string Name => "postgres";

    public async Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        if (IsBlank("PG_CONNECTION"))
        {
            // The env check already carries this failure; blocking avoids counting it twice.
            return Diagnosis.Blocked("PG_CONNECTION is not set — see the env row");
        }

        try
        {
            using IServiceScope scope = CliHost.Scope();
            SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();
            return await db.Database.CanConnectAsync(ct)
                ? Diagnosis.Ok("connected")
                : Diagnosis.Broken(
                    "reachable but the connection was refused",
                    "check the host, port and credentials in PG_CONNECTION, and that Postgres is running");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Diagnosis.Broken(
                Doctor.OneLine(ex.Message),
                "start the Postgres server (on the server: the compose stack in /root/sonarr-net), "
                + "then re-run this command");
        }
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
        => await DockerRemedy.TryStartAsync("postgres", async () =>
        {
            using IServiceScope scope = CliHost.Scope();
            SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();
            return await db.Database.CanConnectAsync(ct);
        }, ct);

    private static bool IsBlank(string key) => CliHost.Configuration[key] is not { Length: > 0 } v
        || string.IsNullOrWhiteSpace(v);
}

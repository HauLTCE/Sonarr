using Serilog;
using Serilog.Events;
using Sonarr.Application.Config;
using Sonarr.Application.Health;
using Sonarr.Bot.Api;
using Sonarr.Bot.Discord.Chat;
using Sonarr.Bot.Discord.Jobs;
using Sonarr.Bot.Discord.Levels;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Bot.Discord.Music;
using Sonarr.Bot.Discord.Utility;
using Sonarr.Bot.Configuration;
using Sonarr.Bot.DependencyInjection;
using Sonarr.Bot.Observability;
using Sonarr.Bot.Persona;
using Sonarr.Infrastructure.Caching;
using Sonarr.Infrastructure.Persistence;

// Serilog before anything else, so config failures are logged in the same shape as
// everything else (docs/03-stack.md: structured console = the `docker logs` view).
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddDotEnvFile(Path.Combine(builder.Environment.ContentRootPath, ".env"));
    builder.Configuration.AddEnvironmentVariables();

    var options = builder.Services.AddSonarrOptions(builder.Configuration);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/sonarr-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate:
            "{Timestamp:o} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddSingleton<SonarrMetrics>();
    builder.Services.AddSonarrPersistence(options.PgConnection);
    builder.Services.AddSonarrRedis(options.RedisConnection);
    builder.Services.AddSonarrHealth();
    builder.Services.AddSonarrConfig();
    builder.Services.AddSonarrModeration();
    builder.Services.AddSonarrUtility();
    builder.Services.AddSonarrLevels();
    builder.Services.AddSonarrMusic(options);
    // Last of the service registrations: the scheduler resolves every IJobHandler the slices
    // above registered (docs/08 — core.job is the authority, one 15 s poller).
    builder.Services.AddSonarrJobs();
    builder.Services.AddSonarrPersona(options);
    // After the persona: the chat pipeline resolves the live PersonaHolder that call created.
    builder.Services.AddSonarrChat(options);
    builder.Services.AddSonarrDiscord();

    builder.Services.AddSonarrPanelApi(options);

    // Plain HTTP: HTTPS is terminated by the user's tunnel in front of
    // sonarr.hault.io.vn (docs/02-architecture.md).
    builder.WebHost.UseUrls($"http://0.0.0.0:{options.ApiPort}");

    var app = builder.Build();

    app.UseCors(PanelApiServiceCollectionExtensions.CorsPolicy);

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    // docs/03-stack.md: lightweight counters, rendered by the panel. No auth needed —
    // it exposes no user data, and the API is only reachable behind the tunnel.
    app.MapGet("/api/metrics", (SonarrMetrics m) => Results.Ok(new
    {
        startedAt = m.StartedAt,
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - m.StartedAt).TotalSeconds,
        counters = m.Snapshot(),
    }));

    // The panel surface (docs/09): login, the user's own data, the public status blob and the
    // allow-listed admin routes.
    app.MapSonarrAuth();
    app.MapSonarrMe();
    app.MapSonarrStatus();
    app.MapSonarrAdmin();

    Log.Information("Sonarr starting — API on :{Port}, commands {Scope}",
        options.ApiPort,
        options.DiscordDevGuildId is { } g ? $"scoped to dev guild {g}" : "registered globally");

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sonarr failed to start");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

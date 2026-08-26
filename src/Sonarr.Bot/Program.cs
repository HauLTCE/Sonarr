using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Sonarr.Application.Config;
using Sonarr.Application.Health;
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
    .WriteTo.Console(outputTemplate: LogTemplates.Console)
    .CreateBootstrapLogger();

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.AddDotEnvFile(Path.Combine(builder.Environment.ContentRootPath, ".env"));
    builder.Configuration.AddEnvironmentVariables();

    var options = builder.Services.AddSonarrOptions(builder.Configuration);

    builder.Services.AddSerilog((services, cfg) => cfg
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: LogTemplates.Console)
        .WriteTo.File("logs/sonarr-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: LogTemplates.File));

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

    IHost app = builder.Build();

    Log.Information("Sonarr starting — {Now:yyyy-MM-dd HH:mm:ss zzz} ({Zone}), commands {Scope}",
        DateTimeOffset.Now,
        TimeZoneInfo.Local.Id,
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

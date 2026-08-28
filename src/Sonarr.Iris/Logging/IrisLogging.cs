using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace Sonarr.Iris.Logging;

/// <summary>
/// The logging the bot writes through and that <c>sonarr logs</c> reads back — both ends in one
/// module so the writer and the reader of the same files cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The bot calls <see cref="Bootstrap"/> before anything else and <see cref="AddIrisLogging"/>
/// once the host exists; the CLI reads the rolling files those calls produce. The file template
/// (<see cref="LogTemplates.File"/>) and the rolling constants here are therefore load-bearing in
/// two processes, which is exactly why they moved out of the bot into Iris.
/// </para>
/// <para>
/// Paths are relative to the process's working directory, which the service unit pins to
/// <c>/opt/sonarr</c> — that is where the files land on the server, and where <c>sonarr logs</c>
/// looks first.
/// </para>
/// </remarks>
public static class IrisLogging
{
    /// <summary>The rolling file's directory, relative to the working directory.</summary>
    public const string LogDirectory = "logs";

    /// <summary>
    /// Serilog's rolling path with the trailing dash: <c>sonarr-20260828.log</c> per day.
    /// </summary>
    public const string FileSinkPath = "logs/sonarr-.log";

    /// <summary>The fixed part of the file name, before the date.</summary>
    public const string FilePrefix = "sonarr-";

    /// <summary>How many daily files the sink keeps. <c>sonarr logs</c> never looks past this.</summary>
    public const int RetainedDays = 14;

    /// <summary>
    /// The before-DI logger: console only, so a config failure is reported in the same shape as
    /// everything else instead of vanishing into a default sink.
    /// </summary>
    public static void Bootstrap() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: LogTemplates.Console)
            .CreateBootstrapLogger();

    /// <summary>
    /// The real logger: console for <c>journalctl -u sonarr</c>, plus the daily rolling file that
    /// <c>sonarr logs</c> tails. Configuration can still override levels via <c>SERILOG__*</c>,
    /// which is why the configuration object is passed in rather than ignored.
    /// </summary>
    public static IServiceCollection AddIrisLogging(
        this IServiceCollection services, IConfiguration configuration) =>
        services.AddSerilog((provider, cfg) => cfg
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(provider)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: LogTemplates.Console)
            .WriteTo.File(FileSinkPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedDays,
                outputTemplate: LogTemplates.File));
}

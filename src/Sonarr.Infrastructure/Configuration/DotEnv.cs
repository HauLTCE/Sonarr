using Microsoft.Extensions.Configuration;

namespace Sonarr.Infrastructure.Configuration;

/// <summary>
/// Loads a `.env` file into the configuration as an in-memory source. Real environment variables
/// always win — nothing here overwrites what the shell or the service unit already set.
/// </summary>
/// <remarks>
/// In Infrastructure rather than in the host that reads it, because both hosts read it: the bot
/// and the <c>sonarr</c> CLI run on the same box against the same <c>.env</c>, and a CLI that
/// resolved connection strings differently from the bot would be a debugging trap rather than a
/// tool.
/// </remarks>
// ponytail: dumbest possible parser (KEY=VALUE, # comments, optional surrounding
// quotes). No multi-line values, no ${VAR} expansion. If .env ever needs those,
// swap in DotNetEnv rather than growing this.
public static class DotEnv
{
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!File.Exists(path))
        {
            return builder;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#')
            {
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return builder.AddInMemoryCollection(values);
    }

    /// <summary>
    /// Where the <c>.env</c> lives, searching upwards from <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// The bot is started from its own directory by its service unit, but a person running
    /// <c>sonarr</c> is standing wherever they happen to be standing. Walking up means the CLI
    /// works from anywhere inside the deployment tree instead of only from its root.
    /// </remarks>
    public static string? FindUpwards(string start, string fileName = ".env")
    {
        DirectoryInfo? directory = new(start);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

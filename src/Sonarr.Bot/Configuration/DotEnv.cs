namespace Sonarr.Bot.Configuration;

/// <summary>
/// Loads a `.env` file into the configuration as an in-memory source, so `dotnet run`
/// on a dev box behaves like compose's `env_file` in prod. Real environment variables
/// always win — nothing here overwrites what the container already set.
/// </summary>
// ponytail: dumbest possible parser (KEY=VALUE, # comments, optional surrounding
// quotes). No multi-line values, no ${VAR} expansion. If .env ever needs those,
// swap in DotNetEnv rather than growing this.
public static class DotEnv
{
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder builder, string path)
    {
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
}

using Microsoft.Data.Sqlite;

namespace Sonarr.Migrator.Import;

/// <summary>
/// Locates the old bot's data files. Source of truth at cutover is <c>/root/sonarr/</c>, the
/// systemd deployment — NOT <c>/root/sonarr-data/</c>, which is stale (docs/04-database.md).
/// Passing the wrong directory silently imports old data, so this refuses a directory that
/// looks like the stale copy unless it is named explicitly.
/// </summary>
public sealed class LegacySource(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string BotDbPath => Path.Combine(Root, "bot_data.db");

    public string ElaineDbPath => Path.Combine(Root, "data", "elaine.db");

    public string PlaylistsPath => Path.Combine(Root, "playlists.json");

    public bool HasBotDb => File.Exists(BotDbPath);

    public bool HasElaineDb => File.Exists(ElaineDbPath);

    public bool HasPlaylists => File.Exists(PlaylistsPath);

    /// <summary>Read-only connection: the old DB is the rollback copy and must not be written.</summary>
    public SqliteConnection OpenBotDb() => OpenReadOnly(BotDbPath);

    public SqliteConnection OpenElaineDb() => OpenReadOnly(ElaineDbPath);

    private static SqliteConnection OpenReadOnly(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>True if the table exists — old deployments differ in which tables were created.</summary>
    public static bool TableExists(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>
    /// Old ids are TEXT. Anything that is not a plain snowflake (the literal 'global', a
    /// pre-guild bucket name, junk) is skipped rather than coerced to 0 — a row under guild 0
    /// would be invisible forever.
    /// </summary>
    public static bool TryId(string? raw, out long id) =>
        long.TryParse(raw, out id) && id > 0;
}

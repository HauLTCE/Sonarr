using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Entities.Music;
using Sonarr.Infrastructure.Persistence;

namespace Sonarr.Migrator.Import;

/// <summary>
/// One-shot import of the old SQLite/JSON data into Postgres, per the data-migration map in
/// docs/04-database.md. Idempotent: every step upserts on the natural key, so a re-run after a
/// partial failure converges instead of duplicating. Economy, games, adventure, achievements and
/// the dead AI-cache tables are deliberately NOT migrated (docs/01 guardrails).
/// </summary>
public sealed class LegacyImporter(LegacySource source, SonarrDbContext db, DateTimeOffset now)
{
    public async Task<ImportReport> RunAsync(CancellationToken ct = default)
    {
        ImportReport report = new();

        // Every table cascades from core.guild, so guild rows have to exist first. The old data
        // never stored guild names, so they stay blank until the gateway refreshes them on boot.
        await SeedGuildsAsync(report, ct);
        await ImportLevelsAsync(report, ct);
        await ImportGuildConfigAsync(report, ct);
        await ImportPersonsAsync(report, ct);
        await ImportEpisodesAsync(report, ct);
        await ImportGuildStateAsync(report, ct);
        await ImportPlaylistsAsync(report, ct);

        report.AddSkipped("economy/games/adventure", "not migrated by design (docs/01 guardrails)");
        report.AddSkipped("response_cache/message_cache", "dead AI cache, not migrated");

        return report;
    }

    // ---- core.guild ----------------------------------------------------------------

    private async Task SeedGuildsAsync(ImportReport report, CancellationToken ct)
    {
        HashSet<long> ids = [];
        CollectIds(ids, "SELECT DISTINCT guild_id FROM levels", "levels");
        CollectIds(ids, "SELECT DISTINCT guild_id FROM guild_config", "guild_config");

        if (source.HasElaineDb)
        {
            using SqliteConnection elaine = source.OpenElaineDb();
            if (LegacySource.TableExists(elaine, "global_state"))
            {
                foreach (string raw in Query(elaine, "SELECT guild_id FROM global_state"))
                {
                    if (LegacySource.TryId(raw, out long id))
                    {
                        ids.Add(id);
                    }
                }
            }

            if (LegacySource.TableExists(elaine, "user_state"))
            {
                foreach (string raw in Query(elaine, "SELECT user_key FROM user_state"))
                {
                    if (TrySplitScopeKey(raw, out long guildId, out _))
                    {
                        ids.Add(guildId);
                    }
                }
            }
        }

        foreach (long guildId in ReadPlaylistGuildIds())
        {
            ids.Add(guildId);
        }

        HashSet<long> existing = [.. await db.Guilds.Select(g => g.GuildId).ToListAsync(ct)];
        int written = 0;
        foreach (long guildId in ids.Where(id => !existing.Contains(id)))
        {
            db.Guilds.Add(new Guild
            {
                GuildId = guildId,
                Name = string.Empty,
                JoinedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("core.guild", ids.Count, written, ids.Count - written, "existing rows left alone");
    }

    private void CollectIds(HashSet<long> ids, string sql, string table)
    {
        if (!source.HasBotDb)
        {
            return;
        }

        using SqliteConnection bot = source.OpenBotDb();
        if (!LegacySource.TableExists(bot, table))
        {
            return;
        }

        foreach (string raw in Query(bot, sql))
        {
            if (LegacySource.TryId(raw, out long id))
            {
                ids.Add(id);
            }
        }
    }

    // ---- levels.progress -----------------------------------------------------------

    private async Task ImportLevelsAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasBotDb)
        {
            report.AddSkipped("levels.progress", "bot_data.db not found");
            return;
        }

        using SqliteConnection bot = source.OpenBotDb();
        if (!LegacySource.TableExists(bot, "levels"))
        {
            report.AddSkipped("levels.progress", "no levels table");
            return;
        }

        using SqliteCommand command = bot.CreateCommand();
        command.CommandText = "SELECT user_id, guild_id, xp, level FROM levels";
        using SqliteDataReader reader = command.ExecuteReader();

        int read = 0, written = 0, skipped = 0;
        while (reader.Read())
        {
            read++;
            // guild_id defaulted to the literal 'global' before the bot was multi-guild; those
            // rows cannot be attributed to a server, so they are dropped rather than guessed.
            if (!LegacySource.TryId(reader.GetString(0), out long userId)
                || !LegacySource.TryId(reader.GetString(1), out long guildId))
            {
                skipped++;
                continue;
            }

            long xp = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            int level = reader.IsDBNull(3) ? 1 : reader.GetInt32(3);

            LevelProgress? existing = await db.LevelProgress
                .FirstOrDefaultAsync(p => p.GuildId == guildId && p.UserId == userId, ct);

            if (existing is null)
            {
                // Voice seconds and streaks start at 0: the old bot never tracked them, and
                // inventing a streak would hand out rewards nobody earned.
                db.LevelProgress.Add(new LevelProgress
                {
                    GuildId = guildId,
                    UserId = userId,
                    Xp = xp,
                    Level = level,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.Xp = xp;
                existing.Level = level;
                existing.UpdatedAt = now;
            }

            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("levels.progress", read, written, skipped, "voice/streak start 0");
    }

    // ---- core.guild_config ---------------------------------------------------------

    private async Task ImportGuildConfigAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasBotDb)
        {
            report.AddSkipped("core.guild_config", "bot_data.db not found");
            return;
        }

        using SqliteConnection bot = source.OpenBotDb();
        if (!LegacySource.TableExists(bot, "guild_config"))
        {
            report.AddSkipped("core.guild_config", "no guild_config table");
            return;
        }

        using SqliteCommand command = bot.CreateCommand();
        command.CommandText = "SELECT guild_id, config_key, config_value FROM guild_config";
        using SqliteDataReader reader = command.ExecuteReader();

        int read = 0, written = 0, skipped = 0;
        while (reader.Read())
        {
            read++;
            string rawKey = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            string? value = reader.IsDBNull(2) ? null : reader.GetString(2);

            // The new config catalog is a closed set (ConfigKeys). Anything not in it — every
            // games/dungeon/economy key, plus keys the rewrite dropped — is skipped, which is
            // exactly the "drop games/dungeon/economy keys" line in docs/04.
            if (!LegacySource.TryId(reader.GetString(0), out long guildId)
                || !ConfigKeys.TryGet(rawKey, out ConfigKeyDefinition? definition)
                || !ConfigKeys.TryValidate(rawKey, value, out string canonical, out _))
            {
                skipped++;
                continue;
            }

            // Store under the catalog's spelling, not the old one, so /config finds it.
            string key = definition.Key;
            GuildConfig? existing = await db.GuildConfigs
                .FirstOrDefaultAsync(c => c.GuildId == guildId && c.Key == key, ct);

            JsonObject payload = new() { ["value"] = canonical };
            if (existing is null)
            {
                db.GuildConfigs.Add(new GuildConfig
                {
                    GuildId = guildId,
                    Key = key,
                    Value = payload,
                    UpdatedBy = 0, // imported, not set by a person
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.Value = payload;
                existing.UpdatedAt = now;
            }

            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("core.guild_config", read, written, skipped, "non-catalog keys dropped");
    }

    // ---- chat.person ---------------------------------------------------------------

    private async Task ImportPersonsAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasElaineDb)
        {
            report.AddSkipped("chat.person", "data/elaine.db not found");
            return;
        }

        using SqliteConnection elaine = source.OpenElaineDb();
        if (!LegacySource.TableExists(elaine, "user_state"))
        {
            report.AddSkipped("chat.person", "no user_state table");
            return;
        }

        using SqliteCommand command = elaine.CreateCommand();
        command.CommandText =
            "SELECT user_key, dialogue_state, registers, slots, logical_clock FROM user_state";
        using SqliteDataReader reader = command.ExecuteReader();

        int read = 0, written = 0, skipped = 0;
        while (reader.Read())
        {
            read++;
            if (!TrySplitScopeKey(reader.GetString(0), out long guildId, out long userId))
            {
                skipped++;
                continue;
            }

            (PersonRegisters registers, string tier) = MapRegisters(reader.IsDBNull(2) ? null : reader.GetString(2));
            JsonObject slots = ParseObject(reader.IsDBNull(3) ? null : reader.GetString(3));

            Person? existing = await db.People
                .FirstOrDefaultAsync(p => p.GuildId == guildId && p.UserId == userId, ct);

            string state = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            long clock = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);

            if (existing is null)
            {
                // fired_log and activity_stack start fresh (docs/04): the old fired log was reset
                // every message anyway, so there is nothing meaningful to carry over.
                db.People.Add(new Person
                {
                    GuildId = guildId,
                    UserId = userId,
                    DialogueState = state,
                    Registers = registers,
                    Slots = slots,
                    LogicalClock = clock,
                    RelationshipTier = tier,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.DialogueState = state;
                existing.Registers = registers;
                existing.Slots = slots;
                existing.LogicalClock = clock;
                existing.RelationshipTier = tier;
                existing.UpdatedAt = now;
            }

            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("chat.person", read, written, skipped, "fired_log/stack fresh");
    }

    // ---- chat.episode --------------------------------------------------------------

    private async Task ImportEpisodesAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasElaineDb)
        {
            report.AddSkipped("chat.episode", "data/elaine.db not found");
            return;
        }

        using SqliteConnection elaine = source.OpenElaineDb();
        if (!LegacySource.TableExists(elaine, "episodic"))
        {
            report.AddSkipped("chat.episode", "no episodic table");
            return;
        }

        // Idempotency without a stable old id: (guild, user, turn, quote) is the natural key of an
        // episode, so re-running matches instead of appending. Loaded per user to keep the working
        // set small on the J2900.
        using SqliteCommand command = elaine.CreateCommand();
        command.CommandText = "SELECT user_key, turn, text, tag FROM episodic ORDER BY user_key, id";
        using SqliteDataReader reader = command.ExecuteReader();

        int read = 0, written = 0, skipped = 0;
        long currentGuild = -1, currentUser = -1;
        HashSet<(long Turn, string Quote)> seen = [];

        while (reader.Read())
        {
            read++;
            if (!TrySplitScopeKey(reader.GetString(0), out long guildId, out long userId))
            {
                skipped++;
                continue;
            }

            if (guildId != currentGuild || userId != currentUser)
            {
                await db.SaveChangesAsync(ct);
                currentGuild = guildId;
                currentUser = userId;
                seen =
                [
                    .. await db.Episodes
                        .Where(e => e.GuildId == guildId && e.UserId == userId)
                        .Select(e => new ValueTuple<long, string>(e.Turn, e.Quote))
                        .ToListAsync(ct)
                ];
            }

            long turn = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            string quote = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            string tag = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

            if (quote.Length == 0 || !seen.Add((turn, quote)))
            {
                skipped++;
                continue;
            }

            // Embedding stays null: the backfill job embeds these later (docs/04). The old rows
            // had no timestamp either, so HappenedAt is the import time — ordering still comes
            // from Turn, which is the value the engine actually reasons about.
            db.Episodes.Add(new Episode
            {
                GuildId = guildId,
                UserId = userId,
                Quote = quote,
                SentimentTag = tag,
                Turn = turn,
                HappenedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("chat.episode", read, written, skipped, "embeddings backfilled later");
    }

    // ---- chat.guild_state ----------------------------------------------------------

    private async Task ImportGuildStateAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasElaineDb)
        {
            report.AddSkipped("chat.guild_state", "data/elaine.db not found");
            return;
        }

        using SqliteConnection elaine = source.OpenElaineDb();
        if (!LegacySource.TableExists(elaine, "global_state"))
        {
            report.AddSkipped("chat.guild_state", "no global_state table");
            return;
        }

        using SqliteCommand command = elaine.CreateCommand();
        command.CommandText = "SELECT guild_id, global_mood, global_clock FROM global_state";
        using SqliteDataReader reader = command.ExecuteReader();

        int read = 0, written = 0, skipped = 0;
        while (reader.Read())
        {
            read++;
            if (!LegacySource.TryId(reader.GetString(0), out long guildId))
            {
                skipped++;
                continue;
            }

            double mood = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
            long clock = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            JsonObject roomMood = new() { ["mood"] = mood };

            ChatGuildState? existing = await db.ChatGuildStates
                .FirstOrDefaultAsync(s => s.GuildId == guildId, ct);

            if (existing is null)
            {
                db.ChatGuildStates.Add(new ChatGuildState
                {
                    GuildId = guildId,
                    RoomMood = roomMood,
                    GlobalClock = clock,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.RoomMood = roomMood;
                existing.GlobalClock = clock;
                existing.UpdatedAt = now;
            }

            written++;
        }

        await db.SaveChangesAsync(ct);
        report.Add("chat.guild_state", read, written, skipped);
    }

    // ---- music.playlist + playlist_track -------------------------------------------

    private async Task ImportPlaylistsAsync(ImportReport report, CancellationToken ct)
    {
        if (!source.HasPlaylists)
        {
            report.AddSkipped("music.playlist", "playlists.json not found");
            return;
        }

        JsonObject? root = ParseObjectOrNull(File.ReadAllText(source.PlaylistsPath));
        if (root is null)
        {
            report.AddSkipped("music.playlist", "playlists.json unreadable");
            return;
        }

        int read = 0, written = 0, skipped = 0, tracks = 0;
        foreach ((string rawGuildId, JsonNode? guildNode) in root)
        {
            // The pre-guild flat format was preserved by the old bot under a '_legacy' bucket it
            // never served to anyone. It has no owning guild, so it cannot be imported either.
            if (guildNode is not JsonObject playlists || !LegacySource.TryId(rawGuildId, out long guildId))
            {
                skipped++;
                continue;
            }

            foreach ((string name, JsonNode? entries) in playlists)
            {
                read++;
                if (entries is not JsonArray items || name.Length == 0)
                {
                    skipped++;
                    continue;
                }

                Playlist? playlist = await db.Playlists
                    .Include(p => p.Tracks)
                    .FirstOrDefaultAsync(p => p.GuildId == guildId && p.Name == name, ct);

                if (playlist is null)
                {
                    playlist = new Playlist
                    {
                        GuildId = guildId,
                        Name = name,
                        OwnerId = 0, // the old format never recorded who saved it
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.Playlists.Add(playlist);
                }
                else
                {
                    // Re-import replaces the tracks so a re-run converges rather than appending.
                    db.PlaylistTracks.RemoveRange(playlist.Tracks);
                    playlist.Tracks.Clear();
                    playlist.UpdatedAt = now;
                }

                int position = 0;
                foreach (JsonNode? entry in items)
                {
                    (string? title, string? uri) = ReadEntry(entry);
                    if (uri is null)
                    {
                        continue;
                    }

                    playlist.Tracks.Add(new PlaylistTrack
                    {
                        Position = position++,
                        Title = title ?? uri,
                        Uri = uri,
                        DurationMs = 0, // unknown offline; resolved by Lavalink on first load
                        AddedBy = 0,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                    tracks++;
                }

                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        report.Add("music.playlist", read, written, skipped, $"{tracks} track(s)");
    }

    private IEnumerable<long> ReadPlaylistGuildIds()
    {
        if (!source.HasPlaylists)
        {
            return [];
        }

        JsonObject? root = ParseObjectOrNull(File.ReadAllText(source.PlaylistsPath));
        if (root is null)
        {
            return [];
        }

        List<long> ids = [];
        foreach ((string rawGuildId, JsonNode? node) in root)
        {
            if (node is JsonObject && LegacySource.TryId(rawGuildId, out long id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    // ---- helpers -------------------------------------------------------------------

    /// <summary>Old chat keys are <c>"{guild_id}:{user_id}"</c> (legacy <c>scope_key</c>).</summary>
    public static bool TrySplitScopeKey(string? key, out long guildId, out long userId)
    {
        guildId = 0;
        userId = 0;
        if (key is null)
        {
            return false;
        }

        int split = key.IndexOf(':');
        if (split > 0
            && LegacySource.TryId(key[..split], out long guild)
            && LegacySource.TryId(key[(split + 1)..], out long user))
        {
            (guildId, userId) = (guild, user);
            return true;
        }

        // Both stay 0 on any failure — a half-parsed key must not leak a real guild id to a
        // caller that ignored the return value.
        return false;
    }

    /// <summary>
    /// Maps the old flat register blob onto the new orthogonal dimensions. The old model mixed
    /// affect with a relationship tier and bookkeeping fields; only what the new engine declares
    /// is carried over, and the rest is dropped rather than stored as dead jsonb.
    /// </summary>
    public static (PersonRegisters Registers, string Tier) MapRegisters(string? json)
    {
        JsonObject blob = ParseObject(json);
        double Get(string name) => blob[name] switch
        {
            JsonValue v when v.TryGetValue(out double d) => d,
            JsonValue v when v.TryGetValue(out long l) => l,
            _ => 0,
        };

        bool grudge = blob["grudge"] is JsonValue g && g.TryGetValue(out bool b) && b;

        PersonRegisters registers = new()
        {
            Anger = Get("anger"),
            Boredom = Get("boredom"),
            // Old 'warmth' (0-10) is the closest thing to fondness; 'relationship' was -10..10
            // and is what the tier came from, so it is not reused here.
            Fondness = Get("warmth"),
            Trust = Get("trust"),
            // ponytail: the old grudge was a bool, the new one is a decaying scalar. Seeding a
            // held grudge at 5 preserves "she is still mad at you" without pretending to know
            // how mad. Real values accrue from the first conversation after cutover.
            Grudge = grudge ? 5 : 0,
        };

        string tier = blob["role"]?.GetValue<string>() switch
        {
            "favorite" => "regular",
            "nemesis" => "nemesis",
            _ => "stranger",
        };

        return (registers, tier);
    }

    private static JsonObject ParseObject(string? json) => ParseObjectOrNull(json) ?? new JsonObject();

    private static JsonObject? ParseObjectOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Old entries are either <c>{title,url}</c> or a <c>[title,url]</c> pair.</summary>
    private static (string? Title, string? Uri) ReadEntry(JsonNode? entry) => entry switch
    {
        JsonObject obj => (Trim(obj["title"]), Trim(obj["url"])),
        JsonArray arr when arr.Count >= 2 => (Trim(arr[0]), Trim(arr[1])),
        _ => (null, null),
    };

    private static string? Trim(JsonNode? node)
    {
        string? text = node?.ToString().Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static IEnumerable<string> Query(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                yield return reader.GetString(0);
            }
        }
    }
}

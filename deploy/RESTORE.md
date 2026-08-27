# Restore drill

The drill docs/11-deployment.md requires in-repo. Run it **once before cutover, then
quarterly**. It restores a nightly dump into a throwaway container, points a bot at it,
and verifies. Nothing here touches the live database.

Backup layout (written by BackupRunner, 03:30 nightly):

```
/root/backups/sonarr/YYYY/MM/sonarr-YYYY-MM-DD.dump      # pg_dump --format=custom
/root/backups/sonarr/YYYY/MM/config-YYYY-MM-DD.tar.gz    # weekly: persona/ + .env
```

Those are the **host** paths, which have not changed and are what every step below uses. The
bot itself now writes to `/opt/sonarr/backups`, because it runs as `sonarr.service` with
`ProtectHome=true` and the unit bind-mounts the host directory there (docs/11) — the same files,
seen from inside the service's namespace. `BACKUP_PATH` in the unit names the namespace path;
nothing in this drill does.

No `.gz` on the dump: `--format=custom` is already zlib-compressed, so piping it through
gzip would cost J2900 CPU to add nothing and invite a drill that gunzips first. The
config archive *is* gzipped — that one is text.

A file named `*.dump.partial` is a dump still being written (or a killed one). It is not a
backup: `BackupRetention.Parse` cannot see it, so the nightly prune leaves it alone, and
the runner only renames to the real name once the header check passes. Never restore one.

## 0. Pick a dump and sanity-check it

```sh
DUMP=$(ls -1 /root/backups/sonarr/$(date +%Y)/$(date +%m)/sonarr-*.dump | tail -1)
echo "$DUMP"
ls -lh "$DUMP"                                 # must be non-zero; ~50-100MB at this scale
head -c 5 "$DUMP" | grep -q PGDMP && echo "pg_dump header ok"
# The strongest check short of restoring: pg_restore reads the whole table of contents.
# pg_restore is a host binary now — postgresql-client-17 is installed on the box, because
# BackupProbe goes red at boot without pg_dump on PATH (docs/11). It used to have to be run
# inside the bot image, which was the only place a client 17 existed.
pg_restore --list "$DUMP" >/dev/null && echo "table of contents ok"
```

If any check fails, stop and use the previous night's dump — then find out why
`BackupVerification` did not catch it (`/status` should already be red on the Backups
check; that is the same verdict docs/08's log line reports).

## 1. Fresh throwaway Postgres

Port 55432 so it cannot collide with the live 5432. Loopback only.

```sh
docker run -d --name sonarr-restore-test \
  -e POSTGRES_DB=sonarr_restore \
  -e POSTGRES_USER=sonarr \
  -e POSTGRES_PASSWORD="$(openssl rand -base64 18)" \
  -p 127.0.0.1:55432:5432 \
  pgvector/pgvector:pg17

# wait for it
until docker exec sonarr-restore-test pg_isready -U sonarr -d sonarr_restore; do sleep 1; done
```

The dump's `CREATE EXTENSION vector` needs the extension available — that is why this
uses the pgvector image, same as prod.

## 2. Restore

```sh
docker exec -i sonarr-restore-test \
  pg_restore -U sonarr -d sonarr_restore --no-owner --no-privileges --exit-on-error -v < "$DUMP"
```

`--no-owner --no-privileges` so the restore does not depend on prod role names.
Drop `--exit-on-error` only if you want to see the full error list; a clean drill
must pass with it on.

## 3. Verify the data

```sh
psql() { docker exec -i sonarr-restore-test psql -U sonarr -d sonarr_restore -Atc "$1"; }

psql "select extname from pg_extension order by 1"                  # expect: plpgsql, vector
psql "select schemaname||'.'||relname||' '||n_live_tup
      from pg_stat_user_tables order by n_live_tup desc limit 15"    # row counts, spot-check
psql "select count(*) from chat.episode where embedding is not null" # vectors survived
psql "select max(created_at) from chat.episode"                      # freshness ~= dump date
```

Compare the row counts against live (`docker compose exec postgres psql ...` with the same
query). Small deltas are expected — the dump is from 03:30, live has moved on.

## 4. Point a bot at the restored DB

Do **not** use the real Discord token: a second gateway connection on the same bot user
fights the live one. Use a scratch test-app token, or run only the migrator.

The bot is a host service now, so this runs the installed binaries directly with an override
`.env` rather than starting a container. `SONARR_ENV` is what makes that possible from outside
the deployment tree (docs/12) — the same variable `/usr/local/bin/sonarr` exports.

```sh
cp /opt/sonarr/.env /tmp/.env.restore
# edit /tmp/.env.restore:
#   PG_CONNECTION=Host=127.0.0.1;Port=55432;Database=sonarr_restore;Username=sonarr;Password=<the one from step 1>
#   DISCORD_TOKEN=<scratch test-app token>
#   DISCORD_DEV_GUILD_ID=<test guild>
#   REDIS_CONNECTION=127.0.0.1:6379   # fine to share; keys are TTL'd and namespaced by guild
#   PERSONA_PATH=/opt/sonarr/persona
#   MODEL_PATH=/opt/sonarr/models/minilm-l6-v2
#   BACKUP_PATH=/tmp/restore-backups   # NOT the real tree — a drill must not write into it

mkdir -p /tmp/restore-backups
cd /tmp && SONARR_ENV=/tmp/.env.restore dotnet /opt/sonarr/app/Sonarr.Bot.dll
```

The three path keys are in the file because the *unit* normally supplies them and this is not
running under the unit. Leaving them out means the bot reads whatever the copied `.env` says,
which is the compose stack's container view (`/app/persona`) — it would refuse to boot on a
missing persona, and the reason would not be obvious.

The schema check is the same either way: `dotnet /opt/sonarr/app/Sonarr.Migrator.dll migrate`
with `SONARR_ENV` pointed at the same file applies migrations to the restored DB alone.

Pass if: boot config validation passes and the startup self-test reports Postgres green —
`Self-test GREEN` in the container's own output, since the bot serves no health endpoint
(Discord is its only surface). Then `/status` on the test guild, which reports the same
self-test summary.

## 5. Tear down

```sh
shred -u /tmp/.env.restore 2>/dev/null || rm -f /tmp/.env.restore
docker rm -f sonarr-restore-test        # -f also drops its anonymous volume's contents
docker volume prune -f
```

## 6. Record the result

Append one line to this file's log below — a drill nobody wrote down did not happen.

| Date | Dump used | Restore | Verify | Notes |
|---|---|---|---|---|
| | | | | |

## Config restore (the other half)

The DB is useless without the token. Weekly `config-YYYY-MM-DD.tar.gz` holds `persona/`
and `.env`:

```sh
mkdir -p /tmp/cfg && tar -xzf /root/backups/sonarr/YYYY/MM/config-YYYY-MM-DD.tar.gz -C /tmp/cfg
ls -R /tmp/cfg    # expect persona/*.yaml and .env
rm -rf /tmp/cfg   # it contains live secrets — do not leave it lying around
```

⚠ Single-disk honesty (docs/11): these backups sit on the same disk as the database.
They protect against bugs and bad deploys, not disk death. Real redundancy starts when
the J2900 lands and the old CT keeps a weekly `rsync` of the tree.

# 11 — Deployment, Backups & Cutover

## Environments

| | Where | What |
|---|---|---|
| **Dev** | this PC (`e:\projects\bot`) | code, docker compose for pg/redis, test guild with per-guild command registration |
| **Prod (now)** | Proxmox CT "sonarr" @ 192.168.1.101 — Debian 13, 4GB RAM, 20GB disk | current Python bot (systemd `sonarr.service`), Lavalink 4.2.2 (systemd), yt-cipher (container) |
| **Prod (~Sep 2026)** | Pentium J2900 box — 4C/4T, no AVX, 8GB RAM | everything moves here; same compose file — that's the point |

## Compose stack (new)

```yaml
# /root/sonarr-net/docker-compose.yml  (music stack deliberately NOT here)
services:
  bot:        # sonarr-bot: .NET 10, Discord + API :5088
    network_mode: host          # reaches Lavalink/yt-cipher like the old bot
    env_file: .env
    volumes:
      - ./persona:/app/persona           # hot-reloadable persona
      - /root/backups/sonarr:/backups    # BackupRunner writes here
    depends_on: [postgres, redis]
  web:        # sonarr-web: Next.js, :3000 — user's tunnel maps sonarr.hault.io.vn → :3000
  postgres:   # postgres:17 + pgvector, volume pgdata, host port 5432 (LAN only)
  redis:      # redis:7, maxmemory 256mb allkeys-lru
```

`.env`: DISCORD_TOKEN, LAVALINK_URI/PASSWORD, PG/REDIS conn strings, ADMIN_USER_IDS,
PANEL_BASE_URL. Validated at boot; boot fails loudly on bad config.

**Untouched forever**: `lavalink.service`, yt-cipher container, `/root/lavalink/*`.

## Backups (user decision: files on host, foldered by year/month)

- Nightly 03:30, BackupRunner (08): `pg_dump --format=custom` →
  `/root/backups/sonarr/YYYY/MM/sonarr-YYYY-MM-DD.dump`. No `| gzip` as originally
  planned: `-Fc` is already zlib-compressed, so the pipe costs J2900 CPU for nothing
  and the double extension invites a restore that gunzips first. Written as
  `.dump.partial` and renamed only once the header check passes, so a killed dump can
  never look like last night's backup.
- Also weekly (Monday): persona directory + `.env` (secrets are part of disaster
  recovery) → same tree, `config-` prefix, `.tar.gz`. The `.env` is reconstructed from
  the running options — inside the container it is environment, not a file — so it
  cannot drift from what actually booted. Mode 0600 inside the archive.
- Retention: dailies 30 days, then the **earliest surviving file of each month** kept 12
  months, judged per set. Literal "first-of-month" would delete nearly every weekly
  config archive. ~50–100MB total at this scale — fine on the 20GB disk, but the tree is
  one `scp -r` to move.
- Failure surfaces as a red `Backups` check in SelfTest, which is what produces docs/08's
  red line in the log channel and what `/status` shows. A dump that succeeded but more
  than two days ago is also red — a dead timer looks identical to a healthy one otherwise.
- Restore drill documented in-repo ([deploy/RESTORE.md](../deploy/RESTORE.md)):
  `pg_restore` into a fresh container + point a bot at it. Tested once before cutover,
  then quarterly, with the result logged in that file's table.
- ⚠ Single-disk honesty: backups on the same disk as the DB protect against bugs
  and bad deploys, not disk death. When the J2900 arrives, the old CT keeps a weekly
  `rsync` copy of the tree — then we have two machines, real redundancy.

## CI

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml), three jobs:

- **dotnet** — restore, build, `dotnet test Sonarr.slnx`. The engine behavior catalog and
  the persona lint are the gate and both live inside that one command: the catalog is
  `BehaviorCatalogTests`, and `SeedPersonaTests` runs the full `PersonaValidator` against
  the real `persona/` directory off disk, so a persona edit that breaks an intent fails CI
  with the validator's own report.
- **web** — `npm ci`, `tsc --noEmit` (Turbopack does not typecheck, so a type error would
  otherwise ship), lint, build.
- **images** — both Dockerfiles → GHCR, tagged with the branch, the full SHA, and `latest`
  on the default branch. Skipped on pull requests so a fork cannot write to the registry.

Deploy is then `BOT_IMAGE`/`WEB_IMAGE` in the server `.env` pointing at those tags plus
`sh deploy.sh --local --pull`. Rolling back is repointing to a `sha-…` tag and re-running.
`--build` remains the default in `deploy/deploy.sh`: it needs no registry auth and still
works when GitHub is having a day.

Server SSH is key-only as of 2026-07-28 (`/etc/ssh/sshd_config.d/10-key-only.conf`:
`PasswordAuthentication no`, `KbdInteractiveAuthentication no`, `PermitRootLogin
prohibit-password`). It replaced root/password `12345`, which was the worst thing about
this deployment — that password is now dead, but treat it as burned rather than secret.
`deploy.sh` uses `BatchMode=yes`, so it fails loudly rather than prompting.

Reverting is deleting that one file and `systemctl reload ssh`; a mistake there is not a
lockout, since the Proxmox host always has console access (`pct enter 103`). Add a key
with `ssh-copy-id` from a box that already has one, not by re-enabling passwords.

## Host slimming (2026-07-28)

The CT was at 87% of a 20G disk with `apt autoremove` finding nothing — every package was
marked manual, so apt could never reclaim on its own. 604 → 545 packages, 87% → 64%.

Purged: `reportbug python3-reportbug python3-debianbts debian-faq doc-debian
apt-listchanges wamerican manpages-dev build-essential gcc-14 g++-14 dpkg-dev python3-dev
python3.13-dev libpython3-dev mesa-vulkan-drivers inetutils-telnet traceroute dhcpcd-base`,
then `apt-get autoremove --purge` took the 38-package toolchain tail (cpp, libc6-dev, the
sanitizers, `libpython3.13`).

The two removals that needed proving rather than assuming:

- **`dhcpcd-base`** — safe only because `/etc/network/interfaces` is `iface eth0 inet
  static` and no dhcp client was running. On a DHCP host this is how you lose the network.
- **`libpython3.13`** — the live bot runs `/root/sonarr/venv/bin/python` → `/usr/bin/
  python3.13`, which is built static (`ldd` shows no libpython). Checked all 308 `.so`
  files in the venv with `objdump -p | grep NEEDED` too: none link it. Re-verify both if
  the venv is ever rebuilt.

Kept deliberately: **`ffmpeg` and its whole dependency tree** (GTK, mesa-libgallium,
pocketsphinx, the va-drivers — ~250MB of apparent desktop cruft on a headless box). It is
not cruft: `discord.py`'s voice client shells out to the `ffmpeg` binary, so the legacy bot
needs it through the rollback window. `openjdk-21-jre-headless` (199MB, the largest package
on the box) is Lavalink's, which is never touched. `postfix` listens on loopback only and
`cron` mail goes through it — the nightly backup would go silent without it.

Docker was the bigger win: `docker image prune -f` + `docker builder prune -f` freed
2.9GB of a dangling build and stale cache. `sonarr-bot:rollback` (2.03GB, 2026-05-31) stays
until the cutover rollback window closes.

`/root/dpkg-selections-before-cleanup.txt` is the pre-cleanup `dpkg --get-selections`
snapshot; `apt-get install $(...)` off it restores the old set if something surfaces later.
Verified after each step: all six services active, containers healthy, Lavalink still on
2333, venv imports `discord/wavelink/transformers/numpy`, `docker compose config -q` clean.

## Migration & cutover (end of phase 2)

Prep that needs no freeze (done on the CT 2026-07-28): `/root/sonarr-net/` holds the
compose file, a 0600 `.env` generated on the box, `persona/`, `models/minilm-l6-v2` and a
`repo/` tree synced from `/root/sonarr-rewrite` (a clone of the branch, so git is the
record of what is deployed). Both images build there. The stack has never been started.

1. Freeze: stop old bot (`systemctl stop sonarr`), WAL-checkpoint SQLite.
2. Copy data **from `/root/sonarr/`** (the live systemd deployment — NOT
   `/root/sonarr-data/`, which is stale; verified 2026-07-26).
3. Create the schema, then import. The Migrator ships inside the bot image at
   `/app/migrator` (there is no .NET SDK on the server, and nothing migrates at boot on
   purpose), and inherits the bot's `.env`, so neither needs a connection string:

   ```sh
   cd /root/sonarr-net
   docker compose run --rm --entrypoint dotnet bot /app/migrator/Sonarr.Migrator.dll migrate
   docker compose run --rm --entrypoint dotnet bot /app/migrator/Sonarr.Migrator.dll import --source /root/sonarr-cutover
   ```

   `import` is idempotent and re-runnable, and prints a row-count reconciliation table.
   Point `--source` at the frozen copy from step 2, not at `/root/sonarr` itself.

   Two things about the snapshot, both learned by rehearsing this on 2026-07-28:

   - **`bot_data.db` is in WAL mode**, and SQLite cannot open a WAL database read-only
     without creating a `-shm` file beside it — impossible on a `:ro` mount, and it fails
     as `SQLite Error 14: unable to open database file`. Copy the `-wal` and `-shm`
     sidecars along with the `.db` (`cp -a` all three, or the copy is a torn read: the WAL
     was 762 KB against a 155 KB main file, so most of the recent data lives there), then
     on the **snapshot only**, never the live file:

     ```sh
     python3 -c "import sqlite3; c=sqlite3.connect('bot_data.db'); \
       c.execute('pragma wal_checkpoint(TRUNCATE)'); c.execute('pragma journal_mode=delete')"
     ```

     Verify with `pragma integrity_check` and by diffing per-table `count(*)` against the
     live file opened read-only (`sqlite3.connect('file:...?mode=ro', uri=True)`), which is
     how the 2026-07-28 rehearsal confirmed all 10 tables matched. Note there is no
     `sqlite3` CLI on the box — the stdlib module through `python3` is the tool.
   - **The container runs as `uid=1654(app)`**, so `chown -R 1654:1654` the snapshot.
     `playlists.json` is mode 0600 root-only on the live box, and the import dies on it
     with `import failed: Access to the path '/legacy/playlists.json' is denied.` — after
     the guild/levels steps have already run. It is re-runnable, so this costs a retry
     rather than a bad state, but it burns freeze minutes. Keep the snapshot mode 0600
     (`chmod -R u=rX,go=`); this is live user data.

   Full dry run against a throwaway `sonarr_rehearsal` database, 2026-07-28 — no freeze,
   live bot never stopped, snapshot row counts verified equal to the live DB across all
   10 SQLite tables first. This is the reconciliation table to expect:

   ```
   core.guild                        3        3        0  existing rows left alone
   levels.progress                  14       14        0  voice/streak start 0
   core.guild_config                 7        3        4  non-catalog keys dropped
   chat.person                       5        5        0  fired_log/stack fresh
   chat.episode                      1        1        0  embeddings backfilled later
   chat.guild_state                  2        2        0
   music.playlist                    0        0        6  0 track(s)
   28 row(s) written.
   ```

   A second run wrote 24 rows and left every count identical, so "safe to re-run" is
   tested, not asserted — a mid-freeze retry converges.

   **The 6 skipped playlists are not a data loss.** `playlists.json` is in the pre-guild
   flat format (`{name: [{title, url}]}`, 87 tracks), and the old bot's own loader
   (`cogs/music/cog.py:145-155`) files a flat file under a reserved `_legacy` bucket and
   explicitly does not serve it to any guild. Those playlists are already unreachable in
   production today; there is no guild id to attach them to and inventing one would put
   someone else's tracks in a server. Recreate per server after cutover.

   `core.member` stays empty after the import, which is correct. It is a runtime identity
   cache, nothing FKs to it, and it fills in as people speak — the 60 s activity flush
   upserts the row. `first_seen_at` takes Discord's own join date rather than the time of
   that first message, so `/anniversary` is right from the start and does not reset to
   cutover day. The legacy DB has no join date worth importing: `economy.created_at` is
   the only per-user timestamp, covers 7 of 13 known users, carries no guild id, and is
   two months old. Discord has the real one for free.
4. Backfill episode embeddings (batch job, minutes).
5. Register slash commands, run SelfTest + `/checkperms`, smoke-test music
   (including the VC-drag case) on the test guild, then the real one.
6. `systemctl disable sonarr` (old service stays on disk as rollback), new stack
   `restart: unless-stopped`.
7. Rollback window: 2 weeks — old venv + DB untouched; rollback = disable new,
   re-enable old (accepting loss of anything learned since cutover).
8. After the window: archive `/root/sonarr` to the backup tree, remove the old
   docker experiment dirs (`sonarr-docker*`, `sonarr-data`, backups of backups) —
   the CT's 20GB is 64% full and most of it is this archaeology.

## September J2900 move

1. Install Debian + Docker on J2900; copy compose dir + backup tree.
2. Move Lavalink + yt-cipher first (same versions, same `application.yml`), verify
   with the still-running CT bot pointing at the new LAVALINK_URI.
3. `pg_dump` on CT → restore on J2900 → `docker compose up` → repoint the tunnel.
4. CT demotes to backup-rsync target (see above).

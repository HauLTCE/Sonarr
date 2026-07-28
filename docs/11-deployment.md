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

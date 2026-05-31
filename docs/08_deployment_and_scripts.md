# 08 — Deployment & The Scripts Drawer

Deployment has two eras living side by side: the original **SSH-and-pray**
Python scripts (`deploy.py`, `boost_player.py`) and the newer **Docker**
setup. The scripts still work and still ship secrets in plaintext.

## deploy.py — the nuke-and-reupload

`deploy.py` is a `paramiko` + `scp` script that:

1. Backs up `data/` and the DBs on the remote.
2. **Nukes the entire remote code directory.**
3. Re-uploads all code fresh (minus an `EXCLUDE` set: `.git`, venvs,
   `__pycache__`, local dev DBs, `docs/`, `scripts/`).
4. Restores the preserved data.
5. Clears stale ML model cache.
6. Reinstalls requirements and restarts the service.

It's a sledgehammer, but a *careful* sledgehammer — the `EXCLUDE` /
`PRESERVE_DATA` lists are thought-through, and it explicitly never uploads
local dev databases over production data. The "nuke then restore data" shape
is crude but reliable: it guarantees the remote code is exactly the local
code, no drift from half-failed partial uploads.

## The secrets problem

Both `deploy.py` and `boost_player.py` open with:

```python
HOST = '100.108.202.81'
USER = 'root'
PASSWORD = '12345'
PORT = 22
```

Root SSH credentials, hardcoded, committed to the repo, in two files. The
password is `12345`. This is the single worst thing in the codebase from a
security standpoint. It's a private hobby project on what's presumably a
Tailscale IP (`100.x`), so the blast radius is small — but it's committed
history now, and "small blast radius" is doing a lot of load-bearing work.
If this repo ever goes public or the box ever gets a public IP, that's an
instant root compromise.

> **Note:** the deploy `HOST` (`100.108.202.81`) and the Docker server in
> memory (`192.168.1.101`) don't match, and `REMOTE_DIR` here is `/root/sonarr/`
> — which memory records as the *old, rollback-only* code path. So `deploy.py`
> itself is now partly stale: it points at the pre-Docker layout. Treat it as
> legacy, not the current deploy path.

## boost_player.py — the cheat button

`boost_player.py` is exactly what it sounds like: an admin script that SSHes
in and runs raw SQL to make one hardcoded user ID into a god — HP 10000, all
stats 1000, every legendary equipped, 100 of every consumable. It's the
developer giving a specific player (`TARGET_USER_ID = '594006...'`) max gear.

Mechanically it's fine — `INSERT ... ON CONFLICT DO UPDATE` upserts, clears
inventory for a clean slate, equips one legendary per slot. But:

- It builds SQL by **f-string interpolation** of values
  ([boost_player.py:72](../boost_player.py#L72)). The values are all
  developer-controlled constants here, so it's not an injection vector in
  practice — but it's the bad pattern, and the bot's *real* code uses
  parameterized queries everywhere else. This script is the one place that
  doesn't.
- It points at the same stale `/root/sonarr/` path as `deploy.py`, so it too
  predates the Docker move.

## The Docker era

Per project memory, the bot now runs as a Docker container (`network_mode:
host` so it reaches the separate Lavalink stack), with models mounted
read-only rather than baked into the image, and data bind-mounted from
`/root/sonarr-data/`. The Dockerfile and `docker-compose.yml` are new and
uncommitted. The music stack (Lavalink + yt-cipher) is deliberately kept
*outside* the container and must never be touched.

Hard-won ops gotchas already recorded: don't stream a backgrounded `nohup &`
build over the paramiko channel (it hangs); don't pipe Docker's ANSI/TTY
output to Windows cp1252 stdout (UnicodeEncodeError) — use `--progress=plain`;
and only copy a live SQLite file after stopping the bot and running
`PRAGMA wal_checkpoint(TRUNCATE)`.

## scripts/

`scripts/` holds `fix_nltk.py`, `fix_spacy.py`, `install_lavalink.py`,
`patch_lavalink.py` — i.e. "the NLP data dependencies and Lavalink have each
broken often enough to deserve a dedicated repair script." That folder is a
fossil record of every environment headache this project has had.

## Verdict

The deploy strategy is crude-but-safe. The credential handling is the one
genuinely alarming thing in the repo. And the scripts drawer quietly documents
which dependencies have caused the most pain.

#!/bin/sh
# install-host.sh — put the bot, the migrator and the `sonarr` CLI on the host, as a systemd
# service and a binary on PATH (goal 5: "not in docker no more unless docker is really needed").
#
# Run FROM THE REPO ROOT on your dev box:
#     DEPLOY_HOST=your.server sh deploy/install-host.sh
# or, if you are already on the server with a built tree in ./app:
#     sh install-host.sh --local
#
# Idempotent: safe to re-run, and re-running is the upgrade path.
#
# ── What this replaces, and what it does not ──────────────────────────────────
#
# The bot leaves Docker. It is a gateway client with no listening socket, so the image was
# never buying network isolation — it was buying a pinned runtime, and dotnet-runtime-10.0 from
# packages.microsoft.com is the same pin for 80 MB instead of 880, with no image rebuild
# between a one-line fix and a running process.
#
# Postgres, Redis, Lavalink and yt-cipher stay in compose, and that is the "unless docker is
# really needed" half:
#
#   postgres    pgvector is a compiled extension matched to the server major, and the data is
#               in a named volume. Moving it means a dump/restore for no gain.
#   redis       trivial to host-install, but it is one line of compose next to Postgres and
#               nothing about it is our code.
#   lavalink    a JVM app that downloads its own plugin tree at boot; the retired host install
#               (/root/lavalink, a 100 MB jar in a directory nobody could rebuild) is exactly
#               what the image fixed.
#   yt-cipher   published only as an image. There is no host artifact to install.
#
# So Docker stays installed and those four keep running. What goes away is our own code being
# in it — which is what made every deploy a 3 GB build on a 20 GB disk.
set -eu

usage() {
    cat <<'EOF'
Usage: install-host.sh [--local] [--no-restart] [--no-migrate]

  --local        install from ./app in the current directory (you are on the server)
  --no-restart   install the files and the unit, but do not restart the bot
  --no-migrate   skip `Sonarr.Migrator migrate` (it is idempotent; skip it to stage a rollback)

Remote mode publishes the three apps locally, ships them over ssh, and re-invokes itself
with --local on the far end. It needs the .NET SDK here and nothing but ssh+tar there.
EOF
}

MODE=remote
RESTART=yes
MIGRATE=yes

for arg in "$@"; do
    case "$arg" in
        --local)      MODE=local ;;
        --no-restart) RESTART=no ;;
        --no-migrate) MIGRATE=no ;;
        --unpack)     MODE=unpack ;;
        -h|--help)    usage; exit 0 ;;
        *) echo "unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

SERVICE_USER=sonarr
PREFIX=/opt/sonarr

# ── unpack mode: internal, run BY remote mode over ssh with a tar stream on stdin ──
#
# Same shape and same reason as deploy.sh's --unpack: the logic lives in this file, which the
# remote step has already copied to the server, rather than in a quoted ssh argument where every
# $, backtick and `#` needs hand-escaping. A heredoc cannot replace it — the tar stream owns stdin.
#
# app.new is staged whole and swapped in one `mv`, so a transfer that dies partway leaves the
# running bot's tree untouched. Unlike the persona directory (deploy.sh --unpack-persona), no
# bind mount resolves through here, so replacing the directory is safe.
#
# --no-same-owner: the sending tar runs on Git Bash for Windows and stamps the archive with the
# dev box's own uid/gid (197609/197121, from the Windows SID mapping). Extracting as root honours
# those by default and dies with "Cannot change ownership to uid 197609: Invalid argument" — the
# whole transfer fails, and the message points at the *server* while the cause is the client. The
# ownership here is set explicitly by the chown below anyway, so what the archive claims is noise.
if [ "$MODE" = unpack ]; then
    rm -rf app.new
    tar x --no-same-owner
    for f in app.new/Sonarr.Bot.dll app.new/Sonarr.Migrator.dll app.new/sonarr.dll; do
        [ -e "$f" ] || { echo "!! $f missing after transfer — keeping the old tree" >&2; exit 1; }
    done
    rm -rf app.old
    if [ -d app ]; then mv app app.old; fi
    mv app.new app
    exit 0
fi

# ── remote mode: publish here, ship, re-invoke ────────────────────────────────
if [ "$MODE" = remote ]; then
    : "${DEPLOY_HOST:?set DEPLOY_HOST (hostname or IP of the target box)}"
    DEPLOY_USER="${DEPLOY_USER:-root}"
    TARGET="${DEPLOY_USER}@${DEPLOY_HOST}"

    [ -f Sonarr.slnx ] || { echo "run this from the repo root" >&2; exit 1; }
    command -v dotnet >/dev/null || { echo "!! remote mode publishes locally and needs the .NET SDK" >&2; exit 1; }

    STAGE=.install-host-stage
    rm -rf "$STAGE"

    # All three into ONE directory, framework-dependent, RID-specific.
    #
    # One directory because they share every dependency but their own entry assembly: three
    # separate publishes cost 3x67 MB and can drift apart, which is the drift the Dockerfile's
    # /app/migrator comment was already worried about. `dotnet publish` merges into an existing
    # output directory, so the later two only add their own .dll/.deps.json/apphost.
    #
    # Framework-dependent (--self-contained false) because the runtime is an apt package on the
    # server: a self-contained publish would ship a private copy of CoreCLR per app and stop
    # getting security updates from apt. RID-specific (-r linux-x64) anyway, because that is what
    # picks the Linux native assets — a portable publish drops a `runtimes/` tree with every
    # platform's copy of libonnxruntime.so, which is 209 MB of which 24 are useful.
    echo "==> publishing bot + migrator + cli (linux-x64, framework-dependent)"
    for project in src/Sonarr.Bot src/Sonarr.Migrator src/Sonarr.Cli; do
        dotnet publish "$project" -c Release -r linux-x64 --self-contained false \
            -p:DebugType=none -o "$STAGE/app.new" --nologo -v quiet
    done

    echo "==> ${TARGET}:${PREFIX}  ($(du -sh "$STAGE/app.new" | cut -f1))"

    ssh -o BatchMode=yes "$TARGET" "mkdir -p '${PREFIX}'"

    # This script and the unit file first, so the far end has both before it is asked to run one.
    # --no-same-owner for the same reason as the unpack step: this tar stream is written by Git
    # Bash on Windows and carries a uid the server has never heard of.
    tar c -C deploy install-host.sh sonarr.service \
        | ssh -o BatchMode=yes "$TARGET" "tar x --no-same-owner -C '${PREFIX}'"

    tar c -C "$STAGE" app.new \
        | ssh -o BatchMode=yes "$TARGET" "cd '${PREFIX}' && sh install-host.sh --unpack"

    rm -rf "$STAGE"

    FLAGS="--local"
    [ "$RESTART" = no ] && FLAGS="$FLAGS --no-restart"
    [ "$MIGRATE" = no ] && FLAGS="$FLAGS --no-migrate"
    # shellcheck disable=SC2029  # intentional client-side expansion
    exec ssh -o BatchMode=yes "$TARGET" "cd '${PREFIX}' && sh install-host.sh $FLAGS"
fi

# ── local mode: we are on the box ─────────────────────────────────────────────
[ -d app ] || { echo "no ./app here — run remote mode from the repo root, or publish into ./app" >&2; exit 1; }
[ -f sonarr.service ] || { echo "sonarr.service missing next to this script" >&2; exit 1; }

echo "==> prerequisites"
command -v dotnet >/dev/null || {
    cat >&2 <<'EOF'
!! the .NET 10 runtime is not installed. On Debian 13:

     curl -sSL -o /tmp/ms.deb https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb
     dpkg -i /tmp/ms.deb && apt-get update && apt-get install -y dotnet-runtime-10.0

   Deliberately not done by this script: adding a third-party apt repo and a trust root to a
   machine is a decision, not a deploy step.
EOF
    exit 1
}
# BackupProbe goes red at boot without this, and the nightly dump fails at 03:30 while the
# service still looks healthy. 17+ because pg_dump refuses outright against a newer server and
# the compose Postgres is pg17.
command -v pg_dump >/dev/null || {
    echo "!! pg_dump not on PATH — apt-get install postgresql-client-17 (the compose DB is pg17)" >&2
    exit 1
}

echo "==> service user"
# System account, no login shell, no home: it owns the tree and connects to two loopback ports.
# Created before the chown below, obviously, but also before the unit is written — a unit
# referencing a missing User= fails to start with a message that does not say so.
if ! getent passwd "$SERVICE_USER" >/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

echo "==> layout"
# /opt/sonarr becomes the app root, and the three things the bot reads move here rather than
# being read across a symlink into /root: a service user cannot traverse /root at all (0700
# root-owned), whatever the unit's ProtectHome says. The alternatives were chmod o+x on /root
# (loosening a system directory for one service) or a second copy of .env that can drift from the
# one compose reads. Moving the file and leaving a symlink behind is neither: there is exactly one
# .env, and both readers find it.
#
# logs/ is Serilog's. backups/ is deliberately NOT created: the unit bind-mounts
# /root/backups/sonarr onto /opt/sonarr/backups, systemd creates that mount point inside the
# namespace, and a real directory left here would only be shadowed by it. RESTORE.md's drill and
# the weekly config archive still name the host path, which has not moved.
mkdir -p "$PREFIX/logs"

for item in .env persona models; do
    if [ -e "$PREFIX/$item" ] && [ ! -L "$PREFIX/$item" ]; then
        continue                                    # already here, nothing to do
    fi

    old="/root/sonarr-net/$item"
    if [ -L "$PREFIX/$item" ]; then
        rm -f "$PREFIX/$item"                       # an earlier run's symlink; replace it
    fi

    if [ -e "$old" ] && [ ! -L "$old" ]; then
        cp -a "$old" "$old.bak-host-install"
        mv "$old" "$PREFIX/$item"
        ln -s "$PREFIX/$item" "$old"
        echo "    $item moved to $PREFIX/$item (symlink left at $old)"
    elif [ -L "$old" ]; then
        echo "!!  $old is a symlink but $PREFIX/$item is missing — the move was interrupted." >&2
        echo "    Restore from $old.bak-host-install before re-running." >&2
        exit 1
    else
        echo "!!  neither $PREFIX/$item nor $old exists." >&2
        case "$item" in
            .env)    echo "    The bot cannot start without it; copy the template and fill it in." >&2 ;;
            persona) echo "    The bot refuses to boot without a valid persona/ (docs/10)." >&2 ;;
            models)  echo "    Fetch it with scripts/fetch-model.sh, or unset MODEL_PATH to run lexical-only." >&2 ;;
        esac
        exit 1
    fi
done

# .env holds the token and both passwords. root keeps write, the service reads via its group.
chown root:"$SERVICE_USER" "$PREFIX/.env"
chmod 640 "$PREFIX/.env"
chown -R "$SERVICE_USER:$SERVICE_USER" "$PREFIX/logs" "$PREFIX/app" "$PREFIX/persona"
# The dump directory predates this script and is already 1654:1654 0700 from deploy.sh — the
# uid the *container* ran as. Nothing runs as 1654 any more, so hand it to the service user.
if [ -d /root/backups/sonarr ]; then
    chown -R "$SERVICE_USER:$SERVICE_USER" /root/backups/sonarr
fi

echo "==> unit"
install -m 644 sonarr.service /etc/systemd/system/sonarr.service
systemctl daemon-reload

if [ "$MIGRATE" = yes ]; then
    # Idempotent by contract, and run as the service user so a migration cannot leave root-owned
    # anything behind. Before the restart, so a schema the new code needs is there when it boots.
    echo "==> migrations"
    ( cd "$PREFIX" && setpriv --reuid "$SERVICE_USER" --regid "$SERVICE_USER" --clear-groups \
        /usr/bin/dotnet app/Sonarr.Migrator.dll migrate )
fi

echo "==> sonarr on PATH"
# A wrapper, not a symlink to the apphost: the CLI resolves .env by walking up from the working
# directory, and a person running `sonarr get` from their home directory is nowhere near it.
# SONARR_ENV names the file outright so the answer does not depend on where they stood.
cat > /usr/local/bin/sonarr <<EOF
#!/bin/sh
# Installed by deploy/install-host.sh. Edit the repo, not this.
SONARR_ENV="\${SONARR_ENV:-$PREFIX/.env}" exec /usr/bin/dotnet $PREFIX/app/sonarr.dll "\$@"
EOF
chmod 755 /usr/local/bin/sonarr

if [ "$RESTART" = yes ]; then
    echo "==> stopping the containerised bot"
    # The gateway allows one session per token: with two bots connected Discord disconnects the
    # first, which looks exactly like the bot randomly dying. So the container goes down before
    # the service comes up, and it is removed rather than merely stopped so it cannot be left
    # sitting there looking like it is meant to run.
    #
    # The `bot` service stays in docker-compose.yml on purpose — it is the rollback — and
    # deploy.sh refuses to touch it while sonarr.service is enabled, which is what stops the
    # next `deploy.sh` from starting a second session behind your back.
    if [ -f /root/sonarr-net/docker-compose.yml ]; then
        docker compose -f /root/sonarr-net/docker-compose.yml stop bot 2>/dev/null || true
        docker compose -f /root/sonarr-net/docker-compose.yml rm -f bot 2>/dev/null || true
    fi

    echo "==> starting sonarr.service"
    # enable, then restart — NOT `enable --now`. `--now` starts a stopped unit and does nothing at
    # all to a running one, so on the second run (which is the upgrade path this script advertises)
    # it left the old process alive on the old binaries and the old unit file, and printed
    # "active (running)" with a start timestamp from the previous deploy. The status block below
    # looked perfect. `restart` is unconditional and works from either state.
    systemctl enable sonarr
    was=$(systemctl show sonarr -p MainPID --value)
    systemctl restart sonarr

    # A unit that fails after 3 s of "activating" reports active(activating) to a naive check,
    # so this waits for it to settle rather than asking once.
    i=0
    while [ "$i" -lt 20 ]; do
        state=$(systemctl is-active sonarr || true)
        [ "$state" = active ] && sleep 2 && state=$(systemctl is-active sonarr || true)
        case "$state" in
            active)  break ;;
            failed)  break ;;
        esac
        i=$((i + 1))
        sleep 1
    done

    if [ "$(systemctl is-active sonarr || true)" != active ]; then
        echo "!! sonarr.service did not come up:" >&2
        journalctl -u sonarr -n 40 --no-pager >&2
        echo >&2
        echo "   Roll back: systemctl disable --now sonarr && docker compose -f /root/sonarr-net/docker-compose.yml up -d bot" >&2
        exit 1
    fi

    # `active` alone does not prove this run replaced anything — that is exactly what the
    # `enable --now` bug produced. A changed main PID does.
    now=$(systemctl show sonarr -p MainPID --value)
    if [ "$now" = "$was" ] && [ "$was" != 0 ]; then
        echo "!! sonarr.service is active but still on PID $was — the restart did not take." >&2
        exit 1
    fi
    echo "    running as PID $now (was ${was:-none})"
fi

echo
echo "==> status"
systemctl --no-pager --lines 0 status sonarr || true
echo
echo "==> log tail"
journalctl -u sonarr -n 15 --no-pager || true
echo
cat <<'EOF'
Done. The bot is a host service now:

  journalctl -u sonarr -f          logs (what `docker logs -f` was)
  systemctl restart sonarr         restart
  sonarr get                       the CLI, from anywhere

Postgres, Redis, Lavalink and yt-cipher are still in compose and were not touched.
Rolling back: systemctl disable --now sonarr, then `up -d bot` in /root/sonarr-net.
EOF

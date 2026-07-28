#!/bin/sh
# Interim manual deploy (docs/11-deployment.md "CI"): until GitHub Actions exists, this
# does by hand what CI will do — sync the compose dir, build/pull, up -d, wait for health.
#
# Run FROM THE REPO ROOT on your dev box:
#     DEPLOY_HOST=192.168.1.101 DEPLOY_USER=root sh deploy/deploy.sh
# or, if you are already on the server, in /root/sonarr-net:
#     sh deploy.sh --local
#
# Idempotent: safe to re-run. Nothing is deleted without --prune.
#
# SSH auth: key-only. If this prompts for a password, fix that first — the server is
# still root/password today (docs/11) and switching to key-only auth is a first-deploy
# task, not something this script should paper over by embedding a password.
set -eu
# POSIX sh has no pipefail; the one pipeline below is guarded explicitly.

usage() {
    cat <<'EOF'
Usage: deploy.sh [--local] [--build|--pull] [--prune] [--no-wait]

  --local     run against the compose file in the current directory (you are on the server)
  --build     build the bot image from source on the target (default)
  --pull      pull images instead of building (use once CI publishes to GHCR)
  --prune     ALSO remove dangling images afterwards (destructive, opt-in)
  --no-wait   do not block waiting for healthchecks
EOF
}

MODE=remote
ACTION=build
PRUNE=no
WAIT=yes

for arg in "$@"; do
    case "$arg" in
        --local)   MODE=local ;;
        --build)   ACTION=build ;;
        --pull)    ACTION=pull ;;
        --prune)   PRUNE=yes ;;
        --no-wait) WAIT=no ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

# ── remote mode: rsync the deploy dir + sources, then re-invoke ourselves over ssh ──
if [ "$MODE" = remote ]; then
    : "${DEPLOY_HOST:?set DEPLOY_HOST (e.g. 192.168.1.101)}"
    DEPLOY_USER="${DEPLOY_USER:-root}"
    DEPLOY_REMOTE_DIR="${DEPLOY_REMOTE_DIR:-/root/sonarr-net}"
    TARGET="${DEPLOY_USER}@${DEPLOY_HOST}"

    [ -f deploy/docker-compose.yml ] || { echo "run this from the repo root" >&2; exit 1; }

    echo "==> ${TARGET}:${DEPLOY_REMOTE_DIR}"
    ssh -o BatchMode=yes "$TARGET" "mkdir -p '${DEPLOY_REMOTE_DIR}' '${DEPLOY_REMOTE_DIR}/persona' /root/backups/sonarr"

    # Compose file + deploy script. The remote .env is NOT overwritten — it holds the
    # only copy of the prod secrets.
    rsync -az --info=stats0 deploy/docker-compose.yml deploy/deploy.sh \
        "$TARGET:${DEPLOY_REMOTE_DIR}/"
    # Persona YAML: repo-root persona/ is the source of truth (docs/10), mounted at
    # /app/persona and hot-reloaded. --delete so a removed file is removed there too.
    if [ -d persona ]; then
        rsync -az --delete --info=stats0 persona/ "$TARGET:${DEPLOY_REMOTE_DIR}/persona/"
    else
        echo "!!  ./persona is missing — the bot refuses to boot without it. Aborting." >&2
        exit 1
    fi

    if [ "$ACTION" = build ]; then
        echo "==> syncing sources for the on-server image build"
        # node_modules/.next are host artefacts: npm ci in the image installs the right
        # platform binaries, and shipping ~400MB over this link to be ignored is waste.
        rsync -az --delete --info=stats0 \
            --exclude 'bin/' --exclude 'obj/' --exclude '.env' \
            --exclude 'node_modules/' --exclude '.next/' --exclude '*.tsbuildinfo' \
            Directory.Build.props Directory.Packages.props Sonarr.slnx src tests web \
            "$TARGET:${DEPLOY_REMOTE_DIR}/repo/"
    fi

    FLAGS="--local --$ACTION"
    [ "$PRUNE" = yes ] && FLAGS="$FLAGS --prune"
    [ "$WAIT" = no ] && FLAGS="$FLAGS --no-wait"
    # shellcheck disable=SC2029  # intentional client-side expansion
    exec ssh -o BatchMode=yes "$TARGET" "cd '${DEPLOY_REMOTE_DIR}' && sh deploy.sh $FLAGS"
fi

# ── local mode: we are on the box, in the compose dir ──
COMPOSE_FILE=docker-compose.yml
[ -f "$COMPOSE_FILE" ] || { echo "no $COMPOSE_FILE here (expected /root/sonarr-net)" >&2; exit 1; }
[ -f .env ] || { echo ".env missing — copy .env.example and fill it in" >&2; exit 1; }

echo "==> validating compose file"
docker compose -f "$COMPOSE_FILE" config -q

if [ "$ACTION" = build ]; then
    echo "==> building bot + web images"
    docker compose -f "$COMPOSE_FILE" build bot web
else
    echo "==> pulling images"
    docker compose -f "$COMPOSE_FILE" pull
fi

echo "==> up -d"
docker compose -f "$COMPOSE_FILE" up -d --remove-orphans

if [ "$WAIT" = yes ]; then
    echo "==> waiting for postgres + redis health (120s budget)"
    i=0
    while [ "$i" -lt 60 ]; do
        unhealthy=$(docker compose -f "$COMPOSE_FILE" ps --format '{{.Service}} {{.Health}}' \
            | grep -E ' (starting|unhealthy)$' || true)
        [ -z "$unhealthy" ] && break
        i=$((i + 1))
        sleep 2
    done
    if [ -n "${unhealthy:-}" ]; then
        echo "!! still not healthy after 120s:" >&2
        echo "$unhealthy" >&2
        docker compose -f "$COMPOSE_FILE" logs --tail 40 >&2
        exit 1
    fi
fi

if [ "$PRUNE" = yes ]; then
    echo "==> pruning dangling images (volumes untouched)"
    docker image prune -f
fi

echo
echo "==> status"
docker compose -f "$COMPOSE_FILE" ps
echo
echo "==> bot log tail"
docker compose -f "$COMPOSE_FILE" logs --tail 20 bot || true
echo
echo "Done. Untouched as always: lavalink.service, yt-cipher, /root/lavalink/*."

#!/bin/sh
# Manual deploy (docs/11-deployment.md "CI"): sync the compose dir, build or pull, up -d,
# wait for health.
#
# CI (.github/workflows/ci.yml) now publishes both images to GHCR on every push, so the
# short path is: set BOT_IMAGE/WEB_IMAGE in the server's .env to the ghcr.io tags and run
# with --pull. --build stays the default because it needs no registry auth and still works
# when GitHub is having a day.
#
# Run FROM THE REPO ROOT on your dev box:
#     DEPLOY_HOST=your.server DEPLOY_USER=root sh deploy/deploy.sh
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
  --build     build the bot + web images from source on the target (default)
  --pull      pull the images CI published to GHCR (needs BOT_IMAGE/WEB_IMAGE in .env)
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

# ── remote mode: ship the deploy dir + sources, then re-invoke ourselves over ssh ──
#
# Transport is `git archive | ssh tar x`, not rsync. rsync has to exist on BOTH ends, and the
# dev box here is Git Bash on Windows, which ships no rsync at all — the old version died with
# a bare "rsync: command not found" and no hint that the fix was on the client. tar and ssh are
# already required for everything else this script does.
#
# git archive rather than plain tar also means the tree that lands is exactly what is committed:
# no bin/obj, no node_modules, no .env, no stale file left behind by a deleted source. That was
# the point of --delete, and gitignore already encodes the exclude list, so there is no second
# list to keep in sync.
if [ "$MODE" = remote ]; then
    : "${DEPLOY_HOST:?set DEPLOY_HOST (hostname or IP of the target box)}"
    DEPLOY_USER="${DEPLOY_USER:-root}"
    DEPLOY_REMOTE_DIR="${DEPLOY_REMOTE_DIR:-/root/sonarr-net}"
    TARGET="${DEPLOY_USER}@${DEPLOY_HOST}"

    [ -f deploy/docker-compose.yml ] || { echo "run this from the repo root" >&2; exit 1; }
    git rev-parse --git-dir >/dev/null 2>&1 || {
        echo "!! not a git checkout — remote mode ships the committed tree" >&2; exit 1; }

    # Uncommitted work would be silently left behind, and the deploy would look like it worked.
    # Warn rather than abort: deploying HEAD while mid-edit is a legitimate thing to want.
    if [ -n "$(git status --porcelain -- persona src tests web deploy Directory.Build.props \
                                          Directory.Packages.props Sonarr.slnx)" ]; then
        echo "!!  uncommitted changes in the deployed paths — shipping HEAD, not your worktree:" >&2
        git status --short -- persona src tests web deploy >&2
    fi
    echo "==> ${TARGET}:${DEPLOY_REMOTE_DIR}  (HEAD $(git rev-parse --short HEAD))"

    ssh -o BatchMode=yes "$TARGET" "mkdir -p '${DEPLOY_REMOTE_DIR}'"

    # Compose file + deploy script. The remote .env is NOT overwritten — it holds the
    # only copy of the prod secrets. --strip-components=1 drops the leading deploy/.
    git archive HEAD deploy/docker-compose.yml deploy/deploy.sh \
        | ssh -o BatchMode=yes "$TARGET" \
              "tar x --strip-components=1 -C '${DEPLOY_REMOTE_DIR}'"

    # Persona YAML: repo-root persona/ is the source of truth (docs/10), mounted at
    # /app/persona and hot-reloaded. The rm makes a removed file disappear there too, which
    # is what rsync --delete did; persona/ is data the bot reads, never data it writes.
    git ls-tree --name-only HEAD persona >/dev/null 2>&1 && [ -d persona ] || {
        echo "!!  ./persona is missing — the bot refuses to boot without it. Aborting." >&2
        exit 1
    }
    git archive HEAD persona \
        | ssh -o BatchMode=yes "$TARGET" \
              "rm -rf '${DEPLOY_REMOTE_DIR}/persona' && tar x -C '${DEPLOY_REMOTE_DIR}'"

    if [ "$ACTION" = build ]; then
        echo "==> syncing sources for the on-server image build"
        # Into a fresh dir, swapped in only once the whole stream has landed: a link that drops
        # halfway through leaves the previous build tree usable rather than a half-tree that
        # builds into a broken image. repo/ may be a git checkout put there by hand — replacing
        # it wholesale is fine and keeps one code path.
        git archive --prefix=repo.new/ HEAD \
                Directory.Build.props Directory.Packages.props Sonarr.slnx src tests web \
            | ssh -o BatchMode=yes "$TARGET" "set -e
                cd '${DEPLOY_REMOTE_DIR}'
                rm -rf repo.new
                tar x
                for p in repo.new/src repo.new/web/Dockerfile; do
                    [ -e \"\$p\" ] || { echo \"!! \$p missing after transfer — keeping old tree\" >&2; exit 1; }
                done
                rm -rf repo.old
                # `if`, not \`[ -d repo ] && mv\`: under set -e a failing && list that is not in a
                # condition position exits the shell, so on a first deploy — where repo/ does not
                # exist yet — the one-liner aborts here, after transferring everything.
                if [ -d repo ]; then mv repo repo.old; fi
                mv repo.new repo"
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

# The bot image runs as uid 1654, and a bind mount carries host ownership straight through.
# Left root-owned, every nightly pg_dump fails with EACCES at 03:30 while the container
# itself looks perfectly healthy — the only way to catch it is to probe the mount as that
# user. 0700: a dump holds every user's data, and the weekly config archive holds the token.
# Done here rather than in the remote branch so `deploy.sh --local` gets it too.
echo "==> backup dir ownership"
mkdir -p /root/backups/sonarr
chown 1654:1654 /root/backups/sonarr
chmod 700 /root/backups/sonarr

echo "==> validating compose file"
docker compose -f "$COMPOSE_FILE" config -q

if [ "$ACTION" = build ]; then
    echo "==> building bot + web images"
    docker compose -f "$COMPOSE_FILE" build bot web
else
    # Without these the compose defaults are `sonarr-bot:local`, and `pull` would go ask
    # Docker Hub for an image that only ever existed on this box. Fail with the reason
    # instead of with a 404.
    if ! grep -qE '^BOT_IMAGE=.+' .env || ! grep -qE '^WEB_IMAGE=.+' .env; then
        echo "!! --pull needs BOT_IMAGE and WEB_IMAGE set in .env (see .env.example)" >&2
        echo "   or use --build to build from source on this box." >&2
        exit 1
    fi
    # Every service, not just bot+web: postgres, redis and lavalink are pinned tags and
    # yt-cipher is pinned by digest, so this is a no-op for them unless a pin moved, and
    # then it should move here too.
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
echo "Done. Music is in this stack now (lavalink + yt-cipher) — a 'compose down' takes it"
echo "down too. Prefer 'up -d <service>' / 'restart <service>' for routine work."

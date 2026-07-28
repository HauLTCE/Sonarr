#!/bin/sh
# Behavioural tier check for the panel: mints a real session for a real user, asks the API what tier
# that session resolves to, then drives every route and asserts against that answer.
#
# Why this exists rather than a unit test: PanelGate is already covered as a pure function, and the
# self-test already reports green. Neither can tell you whether the wiring in front of them agrees --
# whether the cookie a browser sends resolves to the tier the routes enforce, and whether the pages
# the rail offers are the pages that render. Green health checks have coexisted with a broken bot
# here twice, and the crash this script first caught (a function prop crossing into a client
# component) passed `next build` cleanly.
#
# Run on the CT:  sh tier-check.sh   (from anywhere -- it finds the compose directory itself)
# Reads POSTGRES_PASSWORD and ADMIN_USER_IDS from the stack's .env and never echoes either.
set -eu

# The stack directory is the one holding .env, and this script lives at repo/deploy/ inside it -- so
# walk up rather than assuming a cwd. Without this the greps below quietly find nothing and the
# script runs on to build malformed SQL: `set -e` does not catch a failing grep in a pipeline,
# because the pipeline's status is cut's.
DIR=$(cd "$(dirname "$0")" && pwd)
while [ ! -f "$DIR/.env" ] && [ "$DIR" != / ]; do
  DIR=$(dirname "$DIR")
done
[ -f "$DIR/.env" ] || { echo "no .env found above $0 -- run this on the CT" >&2; exit 1; }
cd "$DIR"

# Reads one key from .env, and refuses to continue without it: every one of these is load-bearing,
# and an empty value fails much later as a confusing syntax error rather than as a missing setting.
env_or_die() {
  value=$(grep -m1 "^$1=" .env | cut -d= -f2- || true)
  [ -n "$value" ] || { echo "$1 is not set in $DIR/.env" >&2; exit 1; }
  echo "$value"
}

API=http://127.0.0.1:$(env_or_die API_PORT)
WEB=http://127.0.0.1:3000

PGPASSWORD=$(env_or_die POSTGRES_PASSWORD)
export PGPASSWORD
ADMINS=$(env_or_die ADMIN_USER_IDS)

psql() {
  docker exec -i -e PGPASSWORD sonarr-postgres-1 psql -U sonarr -d sonarr -q -t -A "$@"
}

fails=0
TAG=tier-check-$$

# Set when the plain-user tier had to be synthesised; removed on the way out.
SYNTHETIC=

# A minted session leaves a row in web.session and a mirror in Redis. The mirror is what the fast
# read path uses, so a DB-only delete would leave the cookie working for up to its TTL -- the real
# revoke path removes both, and so does this.
drop_sessions() {
  for hash in $(psql -c "select session_id from web.session where user_agent = '$TAG';"); do
    docker exec -i sonarr-redis-1 redis-cli DEL "sonarr:web:session:$hash" >/dev/null 2>&1 || true
  done
  psql -c "delete from web.session where user_agent = '$TAG';" >/dev/null 2>&1 || true
}

cleanup() {
  drop_sessions
  [ -n "$SYNTHETIC" ] && psql -c "delete from core.member where user_id = $SYNTHETIC;" >/dev/null 2>&1
  return 0
}

trap cleanup EXIT INT TERM

# Mints a session for $1 and echoes the raw cookie value.
mint() {
  raw=$(openssl rand -hex 32)
  hash=$(printf '%s' "$raw" | sha256sum | cut -d' ' -f1)
  psql -c "insert into web.session (session_id, user_id, expires_at, revoked, user_agent, created_at, updated_at)
           values ('$hash', $1, now() + interval '10 minutes', false, '$TAG', now(), now());" >/dev/null
  echo "$raw"
}

# $1 label, $2 expected code, $3 cookie, $4.. curl args
probe() {
  label=$1 want=$2 cookie=$3
  shift 3
  got=$(curl -s -o /dev/null -w '%{http_code}' -b "sonarr_session=$cookie" "$@")
  if [ "$got" = "$want" ]; then
    printf '  ok   %-38s %s\n' "$label" "$got"
  else
    printf '  FAIL %-38s got %s, want %s\n' "$label" "$got" "$want"
    fails=$((fails + 1))
  fi
}

say() { printf '  %-4s %s\n' "$1" "$2"; }
bad() { say FAIL "$2"; fails=$((fails + 1)); }

# ---------------------------------------------------------------------------------------------
# Find one account per tier. The tier is read back from the API rather than guessed from the DB:
# Manage Server lives on the gateway, so the database cannot tell a manager from a plain member --
# which is exactly the mistake the first version of this script made.
# ---------------------------------------------------------------------------------------------
PLAIN= PLAIN_GUILD= MANAGER= MANAGER_GUILD=

for row in $(psql -F'|' -c "select user_id, guild_id from core.member order by message_count desc nulls last limit 40;"); do
  [ -n "$PLAIN" ] && [ -n "$MANAGER" ] && break

  u=${row%%|*}
  g=${row#*|}

  case ",$ADMINS," in
    *",$u,"*) continue ;;   # bot tier; its own section below
  esac

  cookie=$(mint "$u")
  case $(curl -s -b "sonarr_session=$cookie" "$API/api/me/guilds") in
    *'"canManage":true'*)
      [ -n "$MANAGER" ] || { MANAGER=$cookie; MANAGER_GUILD=$g; MANAGER_ID=$u; } ;;
    *)
      [ -n "$PLAIN" ] || { PLAIN=$cookie; PLAIN_GUILD=$g; PLAIN_ID=$u; } ;;
  esac
done

# A deployment can genuinely have no plain member -- this one has two members, the bot admin and one
# manager. Skipping the user tier there would skip the tier that matters most, so synthesise one: a
# member row is all `/api/me/*` needs, and it is deleted on the way out. The id is outside the
# snowflake range so it can never collide with a real account.
if [ -z "$PLAIN" ]; then
  SYNTHETIC=1
  PLAIN_ID=$SYNTHETIC
  PLAIN_GUILD=$(psql -c "select guild_id from core.guild limit 1;")
  if [ -n "$PLAIN_GUILD" ]; then
    psql -c "insert into core.member (guild_id, user_id, username, display_name, first_seen_at,
                                      last_active_at, message_count, created_at, updated_at)
             values ($PLAIN_GUILD, $SYNTHETIC, 'tier-check', 'tier-check', now(), now(), 0, now(), now())
             on conflict do nothing;" >/dev/null
    PLAIN=$(mint "$SYNTHETIC")
    echo "user tier: no plain member exists on this deployment; synthesised $SYNTHETIC"
  else
    SYNTHETIC=
  fi
fi

ADMIN_ID=$(echo "$ADMINS" | cut -d, -f1)
ADMIN=$(mint "$ADMIN_ID")
ADMIN_GUILD=$(psql -c "select guild_id from core.member where user_id = $ADMIN_ID limit 1;")

# ---------------------------------------------------------------------------------------------
echo "bot tier: $ADMIN_ID"
case $(curl -s -b "sonarr_session=$ADMIN" "$API/api/me") in
  *'"isAdmin":true'*) say ok 'isAdmin true' ;;
  *) bad x '/api/me does not report isAdmin for an allow-listed account' ;;
esac
probe 'GET  /api/admin/config' 200 "$ADMIN" "$API/api/admin/config/$ADMIN_GUILD"
probe 'GET  /api/admin/flags' 200 "$ADMIN" "$API/api/admin/flags/$ADMIN_GUILD"
probe 'GET  /api/admin/stats' 200 "$ADMIN" "$API/api/admin/stats/$ADMIN_GUILD?days=30"
probe 'GET  /api/admin/cases' 200 "$ADMIN" "$API/api/admin/cases/$ADMIN_GUILD?page=1"
probe 'GET  /api/admin/audit' 200 "$ADMIN" "$API/api/admin/audit?skip=0&take=10"
# Writes carry no CSRF header here, so 403 is the pass: the bot tier does not exempt anyone from it.
probe 'PUT  /api/admin/config (no CSRF)' 403 "$ADMIN" -X PUT \
  -H 'Content-Type: application/json' -d '{"key":"levelup_dm","value":"true"}' \
  "$API/api/admin/config/$ADMIN_GUILD"
case $(curl -s -b "sonarr_session=$ADMIN" "$API/api/status") in
  *'"players"'*) say ok 'status blob includes player rows' ;;
  *) say ok 'status blob has no player rows (nothing playing)' ;;
esac
for path in / /memory /music /activity /errors /privacy /server /server/behaviour \
            /server/moderation /server/stats /bot /bot/audit; do
  probe "GET  $path" 200 "$ADMIN" "$WEB$path"
done

# ---------------------------------------------------------------------------------------------
if [ -n "$MANAGER" ]; then
  echo
  echo "guild tier: $MANAGER_ID in $MANAGER_GUILD"
  probe 'GET  /api/admin/config (own guild)' 200 "$MANAGER" "$API/api/admin/config/$MANAGER_GUILD"
  probe 'GET  /api/admin/stats  (own guild)' 200 "$MANAGER" "$API/api/admin/stats/$MANAGER_GUILD?days=30"
  # Bot-wide: spans guilds, so managing one is not enough.
  probe 'GET  /api/admin/audit  (bot-wide)' 403 "$MANAGER" "$API/api/admin/audit?skip=0&take=10"
  # A guild they do not manage. 0 is never a real guild and PanelGate treats it as bot-wide, so a
  # neighbouring id is the honest test.
  probe 'GET  /api/admin/config (other guild)' 403 "$MANAGER" "$API/api/admin/config/$((MANAGER_GUILD + 1))"
  probe 'GET  /server' 200 "$MANAGER" "$WEB/server"
  probe 'GET  /bot' 307 "$MANAGER" "$WEB/bot"
  case $(curl -s -b "sonarr_session=$MANAGER" "$API/api/status") in
    *'"players"'*) bad x 'player rows visible to a guild manager' ;;
    *) say ok 'status blob keeps player rows hidden' ;;
  esac
else
  echo
  echo 'guild tier: no non-admin account holds Manage Server on this deployment; skipped'
fi

# ---------------------------------------------------------------------------------------------
if [ -n "$PLAIN" ]; then
  echo
  echo "user tier: $PLAIN_ID in $PLAIN_GUILD"
  probe 'GET  /api/me/overview' 200 "$PLAIN" "$API/api/me/overview?guildId=$PLAIN_GUILD"
  probe 'GET  /api/me/sonarr' 200 "$PLAIN" "$API/api/me/sonarr?guildId=$PLAIN_GUILD"
  probe 'GET  /api/me/music' 200 "$PLAIN" "$API/api/me/music?guildId=$PLAIN_GUILD"
  probe 'GET  /api/me/data' 200 "$PLAIN" "$API/api/me/data"
  probe 'GET  /api/me/errors' 200 "$PLAIN" "$API/api/me/errors"

  probe 'GET  /api/admin/config' 403 "$PLAIN" "$API/api/admin/config/$PLAIN_GUILD"
  probe 'GET  /api/admin/flags' 403 "$PLAIN" "$API/api/admin/flags/$PLAIN_GUILD"
  probe 'GET  /api/admin/stats' 403 "$PLAIN" "$API/api/admin/stats/$PLAIN_GUILD?days=30"
  probe 'GET  /api/admin/cases' 403 "$PLAIN" "$API/api/admin/cases/$PLAIN_GUILD?page=1"
  probe 'GET  /api/admin/audit' 403 "$PLAIN" "$API/api/admin/audit?skip=0&take=10"

  # Same routes with the query string omitted. Parameter binding runs before the handler, so a
  # non-nullable int here would answer 400 from the binder and never reach the gate at all.
  probe 'GET  /api/admin/stats (no days)' 403 "$PLAIN" "$API/api/admin/stats/$PLAIN_GUILD"
  probe 'GET  /api/admin/cases (no page)' 403 "$PLAIN" "$API/api/admin/cases/$PLAIN_GUILD"
  probe 'GET  /api/admin/audit (no paging)' 403 "$PLAIN" "$API/api/admin/audit"

  probe 'PUT  /api/admin/config' 403 "$PLAIN" -X PUT -H 'Content-Type: application/json' \
    -d '{"key":"levelup_dm","value":"true"}' "$API/api/admin/config/$PLAIN_GUILD"
  probe 'PUT  /api/admin/flags' 403 "$PLAIN" -X PUT -H 'Content-Type: application/json' \
    -d '{"feature":"chat","enabled":false}' "$API/api/admin/flags/$PLAIN_GUILD"

  case $(curl -s -b "sonarr_session=$PLAIN" "$API/api/status") in
    *'"players"'*) bad x 'player rows visible to a plain user' ;;
    *) say ok 'status blob keeps player rows hidden' ;;
  esac

  echo '  their own pages render:'
  for path in / /memory /music /activity /errors /privacy; do
    probe "GET  $path" 200 "$PLAIN" "$WEB$path"
  done

  echo '  pages above their tier redirect rather than render:'
  for path in /server /server/behaviour /server/moderation /server/stats /bot /bot/audit; do
    got=$(curl -s -o /dev/null -w '%{http_code}|%{redirect_url}' -b "sonarr_session=$PLAIN" "$WEB$path")
    case $got in
      307\|*/) printf '  ok   %-38s redirected to the root\n' "$path" ;;
      *) printf '  FAIL %-38s %s\n' "$path" "$got"; fails=$((fails + 1)) ;;
    esac
  done

  # The rail is built from the tier, so a link to a page they cannot open would be a dead end. Read
  # off a page that is known to have rendered, not off a 500 -- an error body has no links either,
  # which would make this assertion pass for the wrong reason.
  root=$(curl -s -b "sonarr_session=$PLAIN" "$WEB/")
  case $root in
    *'class="rail"'*)
      for path in /server /bot; do
        case $root in
          *"href=\"$path\""*) bad x "rail links $path" ;;
          *) say ok "rail omits $path" ;;
        esac
      done ;;
    *) bad x 'the root page rendered without a rail; the link check would be vacuous' ;;
  esac
fi

# ---------------------------------------------------------------------------------------------
echo
echo 'revoking ends every minted session immediately:'
drop_sessions
probe 'GET  /api/me (bot)' 401 "$ADMIN" "$API/api/me"
[ -n "$PLAIN" ] && probe 'GET  /api/me (user)' 401 "$PLAIN" "$API/api/me"
[ -n "$MANAGER" ] && probe 'GET  /api/me (guild)' 401 "$MANAGER" "$API/api/me"

echo
if [ "$fails" -eq 0 ]; then
  echo 'PASS'
else
  echo "FAIL: $fails check(s)"
  exit 1
fi

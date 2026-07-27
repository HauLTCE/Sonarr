# 09 — Web Panels & DM-Token Login

Next.js app (`web/`, container `sonarr-web`) serving two panels on
`sonarr.hault.io.vn`. HTTPS terminated by the user's tunnel; we serve HTTP.
All data comes from the bot's REST API (port 5088); the web app has no database
access and no Discord token.

## Login: DM-token flow (user's design)

```
1. Visitor enters their Discord handle (@username) on /login
2. API: resolves the username → must share a guild with Sonarr
3. Sonarr DMs that user: "Someone is logging into sonarr.hault.io.vn as you.
   Your code: ABCD-1234 (valid 10 min). Not you? Ignore this."
4. Visitor pastes the code (+ optional "remember me")
5. API verifies → session cookie → in
```

Security properties (all enforced server-side in the Auth module):

- **Token**: 8 chars from an unambiguous alphabet (no 0/O/1/I), generated CSPRNG,
  stored **hashed** (web.login_token), **single-use**, 10-minute expiry.
- **Rate limits** (Redis, 05): max 3 token requests / 15 min per target username AND
  per source IP — the endpoint cannot be used to DM-spam someone. Same neutral
  response whether the username resolved or not (no user enumeration).
- **Verification attempts**: 5 tries per token, then the token dies.
- **Failure modes surfaced honestly**: user shares no guild with Sonarr → generic
  "if this account is known, a DM was sent"; DMs closed → the DM simply doesn't
  arrive; the login page shows a help box explaining the DMs-open requirement.
- **Session**: 256-bit random id, hashed at rest (web.session), cookie is
  `HttpOnly; Secure; SameSite=Lax`. 24 h default, **90 d with "remember me"**.
  Sliding renewal on use. Panel has a "log out everywhere" (revokes all sessions).
- Admin-panel routes additionally require the user id to be on the admin allow-list
  (guild owner + configured ids) — checked per request against Postgres, not the
  cached session.

## User panel (any logged-in user)

| Page | Shows |
|---|---|
| **Overview** | my level/XP/rank/streak, message count, join date, my active reminders |
| **My data** | everything 06 lists, live: facts the chat engine holds, my quotes, music prefs/history, sessions. The transparency page. |
| **Sonarr & me** | in-character relationship status (same source as `/relationship`), assigned nickname, memory list with per-fact "ask her to forget" |
| **My errors** | recent errors from *my* commands (case ids + friendly text) — the "don't need an errors channel on Discord" requirement |
| **Music** | my track history, my ratings, server top tracks |
| **Privacy actions** | export JSON, delete chat memory, delete everything (06 rules; confirmation + cooling-off) |

## Admin panel (allow-listed)

| Page | Shows / does |
|---|---|
| **Status** | live: gateway latency, Lavalink health, DB/Redis, uptime, per-service heartbeats, current queue per guild (from `web:live_status`) |
| **Config** | full guild_config editor with validation, export/import — same service layer the `/config` command uses |
| **Feature flags** | kill switches per module per guild, instant |
| **Mod log** | searchable mod.case browser (the DB view of the CLI log) |
| **Stats** | command usage, activity charts, member growth (from stats.*) |
| **Audit** | web.audit — who changed what from the panel |

Admin *writes* go through the same Application services as Discord commands — one
code path, one validation, one audit trail.

## API surface (bot process, Kestrel :5088)

```
POST /api/auth/request-token     { username }            → 202 (always)
POST /api/auth/verify            { username, code, remember } → session cookie
POST /api/auth/logout | logout-all
GET  /api/me                     profile + panel overview data
GET  /api/me/data                the full transparency payload
GET  /api/me/export              JSON download
DELETE /api/me/chat-memory | /api/me/everything
GET  /api/status                 public-safe status blob (no auth)
GET  /api/admin/*                config, flags, cases, stats, audit  [admin]
PUT  /api/admin/config | flags   …                                   [admin]
```

Versioned under `/api`; CORS locked to `sonarr.hault.io.vn`; every admin write
requires a CSRF token (double-submit) on top of the session cookie.

## Next.js notes

- App Router, TypeScript, server components for data pages (the J2900 serves HTML,
  not a SPA bundle megabyte).
- No client-side Discord anything — the API is the only backend.
- Dark theme first (it's a Discord crowd), Sonarr branding only (naming rule).
- i18n-ready strings from day one (Vietnamese later, per the locale groundwork item).

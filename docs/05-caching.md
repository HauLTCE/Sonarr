# 05 — Caching & Transient State (Redis 7)

Redis holds state that is **meant to expire or meant to survive only between restarts**
— never the only copy of anything durable. Everything durable lives in Postgres (04).
Access is always through repository-shaped interfaces (`ISessionCache`, `ICooldownStore`,
…), never `IDatabase` directly in services.

Key prefix convention: `sonarr:{area}:{...}`. All keys carry a TTL — a key without a
TTL is a design bug.

## Chat engine (area `chat`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `chat:session:{guild}:{user}` | hash | 10 min, sliding | live conversation session: message count, last turn at, feeds "message 6 of a back-and-forth" vs "first contact in days" |
| `chat:pending_q:{guild}:{user}` | string (question id) | 10 min | she asked something; expiry-unanswered → authored needle ("you just… weren't going to answer me.") |
| `chat:replied:{channel}:{message_id}` | string (state hash ref) | 1 h | messages she replied to → edit detection window ("nice stealth edit") |
| `chat:ring:{channel}` | list (capped 10) | 15 min | recent message metadata (author, ts, mentioned-her?) — rapid-fire detection, two-people-talking detection |
| `chat:budget:{channel}` | counter | 1 h window | engagement budget: cap replies per channel per window |
| `chat:lastping:{guild}:{user}` | string (content hash) | 5 min | pinged twice with nothing new → call-out instead of repeat |
| `chat:hot:{guild}:{user}` | string (serialized person) | 5 min, sliding | hot cache over chat.person to skip a DB read mid-conversation; write-through on save |

## Music (area `music`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `music:session:{guild}` | string (queue + position snapshot) | 24 h, refreshed every 30 s | crash-resume: `/resume` restores queue and position |
| `music:voteskip:{guild}` | set (user ids) | until track ends | vote-skip tally |
| `music:undo_skip:{guild}` | string (last track) | 10 s | the "wait, that was a banger" undo window |
| `music:np_msg:{guild}` | string (message id) | session | which now-playing embed to keep edited |

## Rate limiting & cooldowns (area `rl`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `rl:cmd:{user}` | sliding counter | 10 s | global command flood guard |
| `rl:spam:{guild}:{user}` | hash (recent msg hashes) | 5 min | anti-spam v2: identical-message / mass-mention / invite-link detection state |
| `rl:login:{ip}` and `rl:login:{username}` | counter | 15 min | web DM-token endpoint: max 3 requests per window per ip AND per target user (prevents DM-spam abuse) |
| `rl:xp:{guild}:{user}` | string | 60 s | XP-per-message cooldown |

## Sessions & presence (area `presence`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `presence:voice:{guild}:{user}` | hash (joined_at, channel, alone?, muted?) | until leave + sweep | voice-XP accrual (anti-AFK: only counted while others present & unmuted) |
| `presence:online_sample:{guild}` | string | 10 min | latest online-count sample before batch write to stats.activity_sample |

## Web (area `web`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `web:session:{hash}` | hash (user_id, expiry, remember) | mirrors web.session | fast session validation without a DB hit per request; Postgres row is the authority (revocation checks DB on sensitive routes) |
| `web:live_status` | string (json) | 5 s | cached live-status blob (gateway latency, Lavalink health, queue lengths) for the status page — computed at most once per 5 s regardless of visitors |

## Config & flags (area `cfg`)

| Key | Type | TTL | Purpose |
|---|---|---|---|
| `cfg:guild:{guild}` | string (json of guild_config) | 10 min + explicit invalidation on write | the "changes apply immediately" mechanism |
| `cfg:flags` | string (json) | 1 min + invalidation | kill-switch states |

## What Redis is explicitly NOT used for

- Chat engine durable memory (facts, episodes, relationships) — Postgres only.
- Job scheduling — the core.job table is the authority (survives Redis flush).
- Anything whose loss would be user-visible data loss. A full `FLUSHALL` must cost at
  most: active conversations feel "reset", an in-flight music session can't resume,
  users re-log into the panel. Nothing else.

## Sizing

At ~14 active users and one busy guild: well under 10MB. `maxmemory 256mb` +
`allkeys-lru` as a safety net; if we ever approach that, something is leaking keys.

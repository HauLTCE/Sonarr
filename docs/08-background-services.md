# 08 — Background Services (everything running)

Everything the bot process runs besides answering commands and mentions. All are
.NET `BackgroundService`s in Sonarr.Bot, individually kill-switchable, each logging a
heartbeat at debug level.

## Always running

| Service | Interval | Does |
|---|---|---|
| **JobScheduler** | 15 s poll | claims due core.job rows (`FOR UPDATE SKIP LOCKED`): reminders (incl. recurring), tempban lifts, scheduled/recurring announcements, capsule deliveries, season closes. The reason reminders survive restarts. |
| **PresenceSampler** | 5 min | samples online count + voice occupancy per guild → stats.activity_sample; updates Redis presence keys. Fuels server stats, "remembers server events", inactivity signals. |
| **VoiceXpAccrual** | 60 s | walks `presence:voice:*`; grants voice XP when ≥2 humans present and user unmuted (anti-AFK). |
| **ActivityFlusher** | 60 s | batches message-count/last-active updates from Redis to core.member (avoids a DB write per message). |
| **MusicSessionSnapshotter** | 30 s | while playing: snapshots queue + position to `music:session:{guild}` for `/resume`. |
| **StatusPagePusher** | 5 s (lazy) | computes `web:live_status` blob when stale and a panel is watching. |
| **SelfTest** | on boot + hourly | verifies Postgres, Redis, Lavalink, Discord perms per guild; posts one green/red line to log channel on change; feeds `/status` and `/checkperms`. |

## Daily / scheduled

| Service | When | Does |
|---|---|---|
| **DailyTick** | per-guild midnight (config timezone) | streak evaluation, first-message-bonus reset, birthday + anniversary announcements, chat engine "mood of the day" reseed, seasonal persona overlay check (Oct/Dec…). |
| **RetentionPruner** | 04:00 | chat.episode caps, dead login tokens/sessions, expired Redis orphans, stats older than 400 d. |
| **BackupRunner** | 03:30 | `pg_dump --format=custom` → `/root/backups/sonarr/YYYY/MM/sonarr-YYYY-MM-DD.dump` (no `.gz` — see 11); weekly config archive on Monday; prunes both sets per retention (11). Verifies the dump is restorable-shaped (non-zero, `PGDMP` header) before the `.partial` file earns its real name; failure reports through the `Backups` self-test check, which is what emits the red line and feeds `/status`. |
| **SeasonRoller** | month boundary | closes levels season, writes results, announces "top chatter of the month", opens next. |

## Event-driven (gateway handlers, not timers)

| Handler | Trigger | Does |
|---|---|---|
| **ChatPipeline** | mention/reply to Sonarr | the whole engine flow (10): session load → understand → respond → persist. Includes typing-delay pacing and engagement budget. |
| **EditWatcher** | message edit | if `chat:replied:{channel}:{msg}` hits → authored "stealth edit" reaction. |
| **XpOnMessage** | message | XP with 60 s cooldown; streak day-touch; first-message bonus. |
| **AntiSpam** | message | identical-flood / mass-mention / invite-link detection → configured action + mod case. |
| **WelcomeFlow** | member join/leave | welcome/farewell embeds, autorole grant. |
| **VoiceStateWatcher** | voice join/leave/move | presence keys; music auto-pause on empty channel + 300 s delayed disconnect; **handles bot being dragged between channels** (reconnect player, don't crash — regression test required). |
| **LoginTokenSender** | API request | generates token, DMs the user, records hash (09). |
| **CommandUsageCounter** | any interaction | increments stats.command_usage. |

## Inside the bot but not services

- **Kestrel API** — the panels' REST backend (02, 09), same process.
- **ONNX embedding session** — lazy-loaded singleton, used by the chat pipeline's
  semantic tier and `/opinion`.
- **Persona hot-reload watcher** — watches the persona directory; validates with the
  full checker; atomically swaps only if valid; log line either way.

## Out-of-process (on the host, not ours to manage)

Lavalink 4.2.2 (systemd) · yt-cipher (container) · postgres:17 + redis:7 (compose,
ours to *run* but they're infrastructure) · the user's HTTPS tunnel for
`sonarr.hault.io.vn`.

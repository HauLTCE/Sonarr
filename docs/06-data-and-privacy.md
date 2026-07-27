# 06 — Data & Privacy

What Sonarr collects, why, where it lives, how long it lives, and how a user gets rid
of it. This doc is the source for the `/privacy` command and the user panel's
"your data" page — the three must never disagree.

## What we collect, per user

| Data | Where | Why | Source |
|---|---|---|---|
| Discord ids, username, display name | core.member | addressing you, panels, logs | Discord events |
| Message **counts & activity times** | core.member, stats.* | levels, streaks, server stats | counted, content not stored |
| XP, level, voice time, streaks | levels.progress | the levels feature | derived |
| Things you told the chat engine | chat.fact | she remembers your name/job/pets/birthday | **only what you said to her directly** |
| Short quotes from conversations **with her** | chat.episode (+ embedding) | callbacks ("weren't you complaining about this last month?") | mention/reply interactions only |
| Her state about you (moods, trust, grudge, tier, nickname) | chat.person, chat.relationship_event | the personality | derived |
| Music you queued | music.play_history, ratings, prefs | history, stats, smart autoplay | your commands |
| Mod cases about you | mod.case | moderation audit trail | mod actions |
| Reminders, capsules, saved quotes, tickets | core.job, social.* | features you invoked | your commands |
| Timezone, birthday | core.member | reminders in your time; birthday feature | **opt-in only, via command** |
| Panel sessions, login-token hashes | web.* | web login | login flow |

## Hard rules

1. **No message-content logging.** Sonarr does not store the content of general chat.
   Message *content* is stored only when: (a) you talk **to** Sonarr (chat episodes),
   (b) a user explicitly saves it (`/quote`, capsule, ticket transcript), or
   (c) a mod action captures context (purge reason — not the purged messages).
2. **The chat engine only learns from messages addressed to it** (mention/reply).
   It never mines the channel. The channel ring buffer (05) holds metadata only
   (author, timestamp, mentioned?), TTL 15 min.
3. **DM content is never stored** except the login token we ourselves sent (hashed).
4. **Embeddings are one-way** — vectors can't be reversed into text; the quote they
   index is subject to the same deletion rules as everything else.
5. **No third parties.** No external APIs receive user data (no LLM APIs by design;
   lyrics/weather-style features were not picked, keeping this list empty).

## Retention

| Data | Retention |
|---|---|
| chat.episode | newest N per user (default 200) + anything referenced by a fact; pruned by retention service |
| chat.replied / ring / session (Redis) | minutes-to-an-hour TTLs, see 05 |
| stats.activity_sample | aggregated per hour, kept 400 days, then dropped |
| mod.case | indefinite (audit trail; guild owners may purge via admin panel) |
| web.login_token | 10 min, single-use, then dead rows purged daily |
| web.session | until expiry/revocation; dead rows purged daily |
| Postgres backups | `/root/backups/sonarr/YYYY/MM/`, pruned per 11-deployment.md |

## User rights (self-service)

- **See:** `/privacy` (ephemeral summary + panel link) and the user panel "your data"
  page — everything above, live, including what the chat engine believes about you
  (`/memories` is the in-character version of the same data).
- **Delete:**
  - `/memories forget <fact>` — the chat engine drops a specific fact (in character).
  - Panel → "delete my chat memory" — wipes chat.person/fact/episode/relationship_event
    for you (she genuinely forgets you; tier resets to stranger).
  - Panel → "delete everything" — the above plus levels progress, music history/prefs,
    quotes you saved, reminders. **Not deletable by self-service:** mod.case rows about
    you (audit integrity — guild-owner decision), aggregated anonymous stats.
  - Deletions cascade to backups only as backups age out; the panel says so honestly.
- **Export:** panel → JSON download of all rows keyed to your user id.

## Access control

- User panel: you see only your own data (session → user_id scoping in every query).
- Admin panel: guild owner + allow-listed admin ids; sees config, cases, flags,
  status — **not** other users' chat memories or facts (those are the user's and the
  engine's business; admins get counts, not contents).
- The bot process itself is the only DB client; the web app has no DB credentials.

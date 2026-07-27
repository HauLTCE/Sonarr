# 04 — Database (PostgreSQL 17 + pgvector)

One database `sonarr`, schema-per-area. All IDs from Discord are `bigint` (snowflakes).
All timestamps `timestamptz`. Every table gets `created_at`/`updated_at` unless noted.
EF Core migrations own the DDL; this doc is the design reference.

## Schema `core` — identity & config

### core.guild
| column | type | notes |
|---|---|---|
| guild_id | bigint PK | Discord guild |
| name | text | cached for panel display |
| joined_at | timestamptz | |

### core.guild_config
| column | type | notes |
|---|---|---|
| guild_id | bigint PK part | |
| key | text PK part | e.g. `welcome_channel`, `log_channel`, `autorole_id`, `music_channel`, `levelup_channel`, `dj_role`, `timezone` |
| value | jsonb | typed payload, validated by the Config service |
| updated_by | bigint | audit |

One config system, hot: writes invalidate the Redis config cache (05) → applies immediately.

### core.member
| column | type | notes |
|---|---|---|
| guild_id + user_id | bigint PK | |
| username / display_name | text | cached; refreshed on events |
| first_seen_at / last_active_at | timestamptz | last_active batched from Redis |
| message_count | bigint | running total (stats, `/userstats`) |
| timezone | text null | IANA id, set via `/timezone` |
| birthday | date null | month+day used; year optional |
| locale | text null | future i18n |

### core.feature_flag
| guild_id (0 = global) + feature | state bool, changed_by, changed_at | kill switches |

### core.job  — durable scheduler (reminders, tempbans, capsules, scheduled announces)
| column | type | notes |
|---|---|---|
| job_id | bigserial PK | |
| kind | text | `reminder`, `recurring_reminder`, `tempban_lift`, `announce`, `season_close`, … |
| run_at | timestamptz indexed | poller claims due rows `FOR UPDATE SKIP LOCKED` |
| recurrence | text null | cron-ish, for recurring kinds |
| payload | jsonb | kind-specific |
| status | text | pending / done / failed(+error) |

## Schema `levels`

### levels.progress
| guild_id + user_id PK | xp bigint, level int, last_message_xp_at (cooldown), voice_seconds bigint, streak_days int, streak_last_day date, first_msg_bonus_day date |

### levels.reward — role rewards
| guild_id + level PK | role_id bigint |

### levels.season / levels.season_result
Seasons: id, guild, starts/ends, status. Results: season_id + user_id, xp_earned, rank — filled at close; feeds "top chatter of the month".

## Schema `mod`

### mod.case  — every action, case-numbered (user requirement: DB + CLI log)
| column | type | notes |
|---|---|---|
| case_id | bigserial PK | shown to mods (`/case 123`) |
| guild_id, target_id, actor_id | bigint | |
| action | text | warn / kick / ban / tempban / timeout / untimeout / unban / purge / slowmode / note |
| reason | text | |
| expires_at | timestamptz null | tempban/timeout → paired core.job |
| context | jsonb | purge filters + count, etc. |

Also logged to console via Serilog (`docker logs` = the CLI view) and surfaced in the admin panel.

### mod.infraction_summary (view) — per user: warn/kick/ban counts for `/userinfo`.

## Schema `music`

### music.playlist / music.playlist_track
Playlists: id, guild_id, name (unique per guild), owner_id. Tracks: playlist_id, position, title, uri, duration_ms, added_by.

### music.play_history
| id, guild_id, requester_id, title, uri, played_at, duration_ms | feeds `/musicstats`, `/mytracks`, smart autoplay |

### music.track_rating
| guild_id + uri + user_id PK | vote smallint (+1/−1) | `/toptracks`; autoplay avoids net-negative tracks |

### music.user_prefs
| guild_id + user_id PK | volume int null, favorites jsonb | survives restarts (old bot lost these) |

## Schema `chat` — the engine's memory (internal name: Elaine)

### chat.person — per (guild,user) engine state
| column | type | notes |
|---|---|---|
| guild_id + user_id PK | | |
| dialogue_state | text | current FSM/activity state |
| registers | jsonb | anger, boredom, fondness, trust, grudge, … |
| slots | jsonb | short-term slots incl. TTL envelope |
| logical_clock | bigint | determinism contract |
| relationship_tier | text | stranger → … → inner_circle (authored tiers) |
| assigned_nickname | text null | the name she picked for you |
| fired_log | jsonb | persisted once:/cooldown: firing log (fixes old amnesia bug) |
| activity_stack | jsonb | persisted push/pop stack |

### chat.fact — first-class facts with confidence
| id PK | guild_id, user_id, predicate text (`job`, `pet`, `favorite_food`, `birthday`…), value text, confidence real (reinforced on repeat mention → hedging behavior), learned_at_turn bigint, learned_at timestamptz, active bool |

### chat.episode — episodic memory, semantically searchable
| id PK | guild_id, user_id, quote text, sentiment_tag text, turn bigint, **embedding vector(384)** (pgvector, ivfflat index), happened_at |

Capped per user by a retention service (keep newest N + all referenced-by-facts).

### chat.relationship_event — trajectory, not just level
| id PK | guild_id, user_id, delta jsonb (register changes), cause text (intent id), turn, at | enables `#trend#` ("you've been almost tolerable this week") and multi-day grudge decay |

### chat.stance — her opinions registry
| topic text PK | stance text, pool_ref text | + chat.stance_agreement (guild,user,topic → agreed bool) — she remembers whose side you took |

### chat.guild_state
| guild_id PK | room_mood jsonb, global_clock bigint, event_log jsonb (server events: "movie night", online-count records) |

### chat.intent_embedding — cache for the semantic matcher
| content_hash text PK | intent_id text, example text, embedding vector(384) | rebuilt only when persona file changes |

## Schema `social`

### social.quote_board — saved quotes (`/quote`), which the chat engine can also recall
### social.capsule — time-capsule messages (delivery via core.job)
### social.ship_seed — nothing stored; seeded from user-id pair (doc note: deterministic, no table needed)
### social.event / social.event_rsvp — scheduled events + opt-in role pings
### social.ticket — ticket threads: id, guild, opener, thread_id, status, transcript_ref, closed_by

## Schema `web`

### web.login_token
| token_hash text PK | user_id bigint, purpose text (`login`), expires_at (10 min), used bool, requested_ip inet |

Single-use, stored **hashed** (SHA-256); raw token only ever exists in the DM.

### web.session
| session_id (random 256-bit, hashed) PK | user_id, created_at, expires_at (24h; 90d if remember_me), revoked bool, user_agent text |

### web.audit — admin-panel actions: who toggled/edited what, when, from which session.

## Schema `stats`

### stats.command_usage — command name, guild, day, count (private analytics; “what to cut next time”)
### stats.activity_sample — per guild: hour bucket, messages, voice_users, online_estimate (fuels server stats, "remembers server events", inactivity signals)

## Data-migration map (Sonarr.Migrator, one-shot)

| Old (SQLite/JSON) | New |
|---|---|
| `bot_data.db` → `levels` | levels.progress (xp, level; voice/streak fields start 0) |
| `bot_data.db` → `guild_config` | core.guild_config (drop games/dungeon/economy keys) |
| `data/elaine.db` → user_state | chat.person (blob split: registers/slots/state; fired_log & stack start fresh) |
| `data/elaine.db` → episodic | chat.episode (embeddings backfilled by a batch job) |
| `data/elaine.db` → global_state | chat.guild_state |
| `playlists.json` | music.playlist + playlist_track |
| everything economy/games/adventure + dead AI-cache tables | **not migrated** |

Source of truth at cutover: **`/root/sonarr/` (systemd deployment), not `/root/sonarr-data/`** — the latter is stale (verified 2026-07-26).

# 07 — Slash Commands

Complete command surface. All slash commands (no `!` prefix). Discord's native command
picker is the help system. `⌘` = also available as right-click context-menu command.
Perms column = default required permission (all remappable in Discord's integration
settings).

## Chat / personality

The main interface is not a command: **@mention or reply to Sonarr** and she answers.
Commands around it:

| Command | Args | Perms | Does |
|---|---|---|---|
| `/relationship` | `[user]` (self default) | everyone | how she feels about you — authored descriptions, not numbers |
| `/memories` | — | everyone | what she remembers about you, in character |
| `/memories forget` | `fact` (autocomplete from your facts) | everyone | she drops that fact |
| `/opinion` | — | everyone | she reads the current conversation's topic (embeddings) and drops an authored take |
| `/ship` | `user_a user_b` | everyone | seeded compatibility meter with authored snark |

## Music

| Command | Args | Perms | Does |
|---|---|---|---|
| `/play` | `query_or_url` | everyone | play/queue; playlist links ask confirmation ("adds 47 tracks — sure?") |
| `/pause`, `/resume-playback` | — | everyone | |
| `/skip` | — | everyone | vote-skip at 8+ listeners; DJ role bypasses |
| `/undo-skip` | — | everyone | 10-second window |
| `/stop` | — | DJ/manage | stop + clear queue |
| `/queue` | `[page]` | everyone | view queue |
| `/remove` | `position` | everyone (own) / DJ (any) | |
| `/duplicate-cleanup` | — | DJ | remove duplicate tracks |
| `/shuffle`, `/playnext`, `/loop` | `track\|queue\|off` | everyone | |
| `/fairqueue` | `on\|off` | DJ | round-robin across requesters |
| `/seek` | `timestamp` | everyone | |
| `/replay` | — | everyone | restart current track |
| `/previous` | — | everyone | from history |
| `/nowplaying` | — | everyone | embed with buttons, requester, tracks-until-yours |
| `/volume` | `0–150` | DJ | per-user preference remembered |
| `/filter` | `bassboost\|nightcore\|karaoke\|speed\|clear` | DJ | Lavalink filters |
| `/grab` | — ⌘ | everyone | DM me the current track |
| `/playlist save\|load\|list\|delete` | `name` | everyone (own) | per-guild named playlists |
| `/resume` | — | everyone | restore crashed session from snapshot |
| `/musicstats` | — | everyone | most-played, top requesters, listening hours |
| `/mytracks` | — | everyone | my recent queue history, replayable |
| `/toptracks` | — | everyone | crowd favorites from 👍/👎 ratings (buttons on nowplaying) |
| `/autoplay` | `on\|off\|smart` | DJ | radio mode; smart = seeded from requester history |

## Levels

| Command | Args | Perms | Does |
|---|---|---|---|
| `/level` | `[user]` | everyone | level/XP |
| `/rank` | `[user]` | everyone | progress-bar card, streak shown |
| `/leaderboard` | `[season]` | everyone | |
| `/compare` | `user` | everyone | side-by-side |
| `/userstats` | `[user=me]` | everyone | message count, join date, activity summary |
| `/config levels …` | subcommands | admin | rewards add/remove, levelup channel, XP event multiplier, decay toggle, channel weights |

## Moderation

| Command | Args | Perms | Does |
|---|---|---|---|
| `/warn` | `user reason` (reason autocompletes from templates) | mod | case-numbered |
| `/kick`, `/ban`, `/unban` | `user [reason]` | mod | |
| `/tempban` | `user duration [reason]` | mod | auto-lift via job table, survives restarts |
| `/timeout`, `/untimeout` | `user [duration]` | mod | |
| `/purge` | `count [from:@user] [contains:text] [bots:true] [preview:true]` | mod | preview = dry-run list, nothing deleted |
| `/slowmode` | `duration\|off` | mod | logged as a case |
| `/case` | `id` | mod | look up any past action |
| `/userinfo` | `user` ⌘ | mod | profile + infraction history |
| `/modlog` | `[user] [page]` | mod | browse cases |

Anti-spam v2 (identical-message / mass-mention / invite-link) acts automatically per
configured policy and files cases — no command needed beyond `/config moderation …`.

## Server management

| Command | Args | Perms | Does |
|---|---|---|---|
| `/config` | grouped subcommands with autocomplete | admin | channels (welcome/log/music/levelup/announce), autorole, DJ role, toggles, timezone; export / import (JSON) |
| `/announce` | `channel message [schedule]` | admin | immediate or scheduled/recurring |
| `/event create\|list\|cancel` | native Discord events + opt-in role pings | manage events | |
| `/ticket` | — | everyone | opens private thread with mod team; close button saves transcript |
| `/roleinfo`, `/serverinfo` | — | everyone | |
| `/avatar` | `[user]` ⌘ | everyone | |
| `/checkperms` | — | admin | bot audits its own permissions per feature |
| `/feature` | `module on\|off` | admin | kill switches (also on admin panel) |

## Utility

| Command | Args | Perms | Does |
|---|---|---|---|
| `/ping` | — | everyone | latency |
| `/status` | — | everyone | uptime, gateway, Lavalink, DB health |
| `/remind` | `when what` | everyone | durable; timezone-aware if `/timezone` set |
| `/reminders list\|cancel` | — | everyone | includes recurring ("every monday 9am") |
| `/timezone` | `zone` (autocomplete) | everyone | |
| `/timestamp` | `time [format]` | everyone | builds `<t:…>` snippets |
| `/choose` | `options (a \| b \| c)` | everyone | |
| `/roll` | `dice (2d6)` | everyone | |
| `/quote save` ⌘ / `/quote random` | — | everyone | server quote board (chat engine can call these back) |
| `/capsule write` | `when message` | everyone | time-capsule delivery to channel |
| `/color` | `#hex` | everyone | swatch preview |
| `/enlarge` | `emoji` | everyone | full-size emoji/sticker |
| `/privacy` | — | everyone | what's stored about me + panel link (ephemeral) |
| `/anniversary` | auto-announced; command shows yours | everyone | join anniversaries |

## Design rules

- Every command: input validated at the controller, friendly error + case-id on
  failure, ephemeral replies wherever the answer is personal (`/privacy`, `/memories`,
  errors), and a kill-switch check on the module.
- Autocomplete everywhere an argument has known values (config keys, playlists,
  facts, reason templates, timezones, seasons).
- Total: ~60 commands/subcommand groups. Registered per-guild in dev (instant
  updates), global in prod.

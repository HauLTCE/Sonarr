# What Users Need — Sonarr Bot Capability List

Everything the (new) bot should do, written as user needs. Edit freely: delete lines you
don't want, add new ones, mark uncertain ones with `?`. Then hand it back for a
feasibility check.

Legend: `[KEEP]` exists in the old bot · `[NEW]` proposed improvement · `[REMOVED]` old
feature that will NOT be rebuilt (listed so nothing disappears silently).

---

## 1. Chat — Elaine (the personality)

- [KEEP] I can @mention or reply to Sonarr and get an in-character response (rude persona), instantly.
- [KEEP] She remembers me across conversations: my name, facts I told her (job, pets, favorites), and how she feels about me (trust, grudges, moods).
- [KEEP] Her mood shifts based on how I treat her (anger, boredom, fondness) and she can refuse to talk to me if I push her too far (cooldown).
- [KEEP] I can play mini-games with her in chat (rock-paper-scissors, guessing game).
- [KEEP] She resists jailbreak/manipulation attempts in character.
- [NEW] She understands paraphrases and off-script phrasing, not just exact keywords ("my manager is driving me up the wall" works, not just "I hate my boss").
- [NEW] She reacts to the *whole* message, not just the first thing it matches ("hi, I'm Sam and why do you hate me" → both parts acknowledged).
- [NEW] She brings up relevant past conversations on her own ("weren't you complaining about this exact thing last month?").
- [NEW] She notices real time passing ("long time no see" after a week away; notices when I ignore her question).
- [NEW] She paces herself: typing indicator proportional to reply length, doesn't spam-reply to rapid-fire mentions.
- [NEW] Her personality file can be edited and hot-reloaded without restarting the bot.

## 2. Music

- [KEEP] I can play music in a voice channel by search query or link (`/play`).
- [KEEP] I can manage a queue: view, skip, remove, shuffle, play-next, loop (track/queue).
- [KEEP] Skip is democratic in a crowded voice channel (vote-skip at 8+ listeners).
- [KEEP] I can see what's playing with interactive buttons (pause, skip, loop, etc.).
- [KEEP] I can go back: track history and `/previous`.
- [KEEP] I can save and load named playlists (per server).
- [KEEP] Autoplay "radio mode" keeps music going when the queue empties.
- [KEEP] The bot auto-pauses when the voice channel empties and leaves after a few minutes.
- [KEEP] I can adjust volume.
- [NEW] My favorites and volume preference survive bot restarts.
- [NEW] An interrupted music session (bot restart/crash) can be resumed.
- I can drag the bot around the VC channels as Admin and it will still play instead of crashing.

## 3. Levels

- [KEEP] I earn XP by chatting and level up.
- [KEEP] I can check my level/XP (`/level`) and the server leaderboard (`/leaderboard`).
- [NEW] Level-ups are announced (configurable channel or off).
- [NEW] Role rewards at level milestones.

## 4. Moderation

- [KEEP] Mods can kick, ban, mute (timeout), unmute, and purge messages.
- [KEEP] Mods can see user info (`/userinfo`).
- [NEW] Every mod action is logged to a database table and also show on CLI.
- [NEW] `/userinfo` shows a user's past infractions; repeat offenses are visible.
- [KEEP] Anti-spam: users flooding commands get rate-limited automatically.

## 5. Server management

- [KEEP] New members get a welcome message; leavers get a farewell (configurable channels).
- [KEEP] New members automatically receive a configured role (autorole).
- [KEEP] Admins can configure everything per server (`/config`): channels for welcome, music, announcements, logs; autorole; toggles.
- [KEEP] Admins can send announcements through the bot.
- [NEW] All configuration is one system, changes apply immediately, no restart needed.

## 6. Utility

- [KEEP] `/ping` and `/status` — is the bot alive and healthy (uptime, latency, Lavalink status).
- [KEEP] `/remind` — set a reminder; the bot pings me later.
- [NEW] Reminders survive bot restarts (currently they die).

## 7. Platform (invisible to users, but promised)

- [NEW] All commands are slash commands with autocomplete and proper error messages; help is Discord's native command picker.
- [KEEP] The bot recovers from crashes/restarts without losing durable data.
- [NEW] Nothing durable is lost on restart, ever (reminders, sessions, config, memories).

---

## REMOVED — will not be rebuilt (confirm)

- [REMOVED] Economy: wallet/bank, daily, work, pay, cashout, baltop, gems, streaks.
- [REMOVED] Shop: buy/sell, titles, prestige, consumables.
- [REMOVED] Achievements & title roles.
- [REMOVED] Gambling/games: coinflip, blackjack, highlow, roulette, crash, mines, rob, duel, heist, arena, tictactoe, connect-four, word-chain.
- [REMOVED] Adventure/dungeon RPG (characters, inventory, items).
- [REMOVED] `!` prefix commands, command chaining (`&&`), fuzzy "did you mean", custom help system (replaced by slash commands).
- [REMOVED] Sleep-mode / midday-break restrictions (only ever gated the economy).

## ADD - add these in

- [S] She comments when you edit a message she already replied to ("nice stealth edit. I saw it.").
- [S] She occasionally uses your server nickname instead of the name you told her — and notices when they differ.
- [M] She has favorites and least-favorites among server members and will admit it if asked ("who do you like most here?").
- [M] Relationship levels are visible: `/relationship` shows how she feels about me (with authored, in-character descriptions, not raw numbers).
- [M] She holds multi-day grudges with decay: being awful to her on Monday is still felt on Wednesday, but forgiven by next week.
- [M] She remembers server events: "last time this many people were online was movie night".
- [M] She has a daily "mood of the day" (seeded from date) that colors all her replies that day — some days she wakes up petty.
- [M] She notices message *style*: ALL CAPS ("stop yelling"), walls of text ("I'm not reading all that. okay I read it."), excessive emoji.
- [M] She can address two people at once when they're both talking to her ("you two are exhausting *together* now").
- [M] Birthday memory: tell her your birthday once, she remembers and acknowledges it (her way: "apparently you were born today. congrats on surviving.").
- [L] Long-term character arcs: her baseline attitude toward a user genuinely evolves through authored relationship tiers (stranger → nuisance → tolerated → grudging friend → inner circle), each tier unlocking different response pools and privileges (inner circle can ask things strangers get refused).
- [L] `/elaine memories` — she tells you what she remembers about you, in character, with the option to ask her to forget something (privacy + fun in one).
- [S] Queue a whole playlist/album link at once with a confirmation ("this adds 47 tracks — sure?").
- [S] `/seek` and `/replay` (jump to timestamp, restart current track).
- [M] Audio filters: bass boost, nightcore, karaoke, speed — Lavalink supports these natively.
- [M] `/grab` — DM me the current track so I can find it later.
- [M] Server music stats: most-played tracks, top requesters, total listening hours (`/musicstats`).
- [M] Personal music history: `/mytracks` — what I've queued recently, replay from it.
- [M] Smart autoplay: radio mode seeds from *my* listening history instead of just the last track.
- [M] Queue saving on crash: if the bot dies mid-session, `/resume` restores queue + position (Redis snapshot every 30s).
- [L] Listening parties: synchronized "now playing" embed with live progress bar, reactions, and a session recap at the end (top track, who queued most).
- [S] `/rank` card with a progress bar (text/embed based — no image rendering needed, though an image card is [M] and looks better).
- [S] XP for voice-channel time, not just messages (anti-AFK: only while others are present and unmuted).
- [M] Role rewards at level milestones, configurable per server (`/config levels addreward`).
- [M] Weekly/monthly leaderboard seasons with a "top chatter of the month" announcement (no prizes needed — recognition is the reward).
- [M] XP decay for total inactivity (optional, off by default) — keeps leaderboards alive on slow servers.
- [M] `/compare @user` — side-by-side level/XP/activity comparison.
- [S] `/slowmode` set channel slowmode through the bot (logged like other mod actions).
- [S] Bulk-purge filters: `/purge from:@user`, `/purge contains:word`, `/purge bots`.
- [M] Anti-spam v2: detect repeated identical messages / mass-mention / invite-link spam with configurable auto-actions (delete, warn, timeout) and mod-log receipts.
- [S] `/roleinfo`, `/serverinfo`, `/avatar` — the classic info commands.
- [M] Event scheduler integrated with Discord's native events + reminder pings to an opt-in role.
- [L] Ticket system: `/ticket` opens a private support thread with the mod team, with transcript saved to the ticket log on close.
- [S] `/timestamp` — build Discord-native timestamps from human times ("friday 8pm" → `<t:...>` everyone sees in their timezone).
- [S] `/choose a | b | c` and `/roll 2d6` — decision helpers (deterministic fun, no economy).
- [S] `/userstats me` — my message count, join date, activity summary.
- [M] Recurring reminders ("every monday 9am: trash day") with `/reminders list|cancel`.
- [M] Timezone-aware reminders: set my timezone once (`/timezone`), all my reminders use it.
- [S] Every error shown to users is friendly + actionable, never a stack trace; full detail goes to logs.
- [M] Per-server locale groundwork: user-facing strings in resource files so Vietnamese (your fallback role name suggests it) can be a first-class language later.
- [M] `/privacy` — what the bot stores about me, and self-service data delete (GDPR-style; pairs with Elaine's forget-me).
- [M] Nightly automated Postgres backup to a second disk/host with retention (deploy-time task, not a feature — but write it down so it happens).
- [S] `/ship @a @b` — compatibility meter with authored snark (seeded = stable per pair, so it's "canon").
- [M] Server anniversary tracker: "you joined 2 years ago today" announcements.
- [?] Admin web panel to control the bot.
- [?] User sided web panel to show some basic info. Example: instead of showing errors and stuffs on discord, you can show it here. So we don't have to make new channel on discord aside from some really needed ones.
- [S] She reacts to being thanked — genuinely thrown off by politeness ("...what do you want.").
- [S] Sleep-schedule flavor: at 3–6am her replies come from a "why are you awake, why am *I* awake" pool (flavor only — she still works).
- [S] If you ping her twice in a row with nothing new to say, she calls it out instead of repeating herself.
- [M] Nicknames she assigns YOU: at certain relationship points she starts calling you something she picked (from authored pools, stable per user) — and refuses to explain it.
- [M] She takes sides: when two users argue in front of her, she picks whoever has higher trust and says so.
- [M] Apology mechanics: a genuine-looking apology (semantic match) actually reduces grudge — but a lazy "sorry lol" makes it worse.
- [M] She has authored opinions on topics (pineapple pizza, tabs vs spaces, anime) with a stance registry per topic — consistent forever, and she remembers if you agreed or disagreed with her.
- [M] She can be "summoned" into a topic: `/sonarr opinion` on the current conversation — she reads the last few messages' *topics* (embeddings, not content parroting) and drops an authored take.
- [M] Rivalry tracking between users: she notices two people who always argue and starts commentating ("oh good, round 12").
- [M] Her memory has *confidence*: facts she heard once vs facts confirmed repeatedly — she hedges the shaky ones ("you said you were a nurse? or was it a vet. whatever.").
- [M] Seasonal skins for her persona: October/December/etc. pool overlays (spooky/festive flavor) that auto-activate by date, authored once, forever.
- [S] `/nowplaying` shows requester avatar + how many tracks until yours plays.
- [M] Track ratings: 👍/👎 on the now-playing embed; `/toptracks` = the server's actual crowd favorites; autoplay learns to avoid the 👎 pile.
- [M] `/duplicate` cleanup — one command removes duplicate tracks from the queue.
- [M] Fair-queue mode: round-robins tracks across requesters instead of first-come-first-served, so one person can't wall the queue.
- [S] Streaks: consecutive active days tracked and shown on `/rank` ("14-day streak").
- [S] First-message-of-the-day bonus XP (encourages the morning hello).
- [S] `/color #hex` — preview a color swatch embed (role-color shopping).
- [S] `/enlarge :emoji:` — full-size emoji/sticker image.
- [S] Startup self-test: on boot the bot verifies Postgres, Redis, Lavalink, Discord perms in each guild — and posts one green/red line to the log channel.
- [S] Command usage analytics (private): which commands actually get used — data for deciding what to cut next time.
- [M] Config export/import: `/config export` gives a JSON snapshot; import restores it (server cloning, disaster recovery, test-server sync).
- [M] Dry-run mode for destructive commands: `/purge ... preview:true` shows what *would* be deleted.
- [M] Per-feature kill switches: every module (music, elaine, levels…) can be disabled per-server or globally at runtime — bad day in one module never takes the bot down.
- [M] Audit own permissions: `/checkperms` — bot lists what it's missing per feature ("can't manage roles → autorole disabled") instead of failing mysteriously later.
- [L] Time-travel debugging for Elaine: record (opt-in) full interaction traces so any weird reply can be replayed step-by-step locally with the exact state — we designed her deterministic; this cashes that in.


* For two web things, you just need to make it run and accept the domain "sonarr.hault.io.vn" and I'll get it on, no need for nginx or anything.
** The bot name is Sonarr, Elaine is the chat module and is not the bot, no one knows Elaine, the bot and people will refer to the bot as "Sonarr".
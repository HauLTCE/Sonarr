# Sonarr

Sonarr is a Discord bot built for one community. It holds a conversation in a consistent
personality, plays music in voice channels, tracks activity levels, and gives moderators
the usual tools. It is written in C# on .NET 10.

## The unusual part: nothing it says is written by an AI

Every single line Sonarr sends was typed out by a person ahead of time and stored in a file.
There are about 3,800 of these lines. When you say something to it, the bot works out which
situation you are in and picks one of the lines written for that situation. It never invents
a sentence.

This is a deliberate design choice, not a shortcut, and it buys three things:

- **The personality cannot drift.** A bot that generates its replies says something slightly
  different every week as its model changes. This one says exactly what somebody decided it
  should say, and a test suite fails the build if a code change alters any of those replies.
- **It costs nothing to run.** No per-message API bill, no rate limits, no outage somewhere
  else taking the bot down with it.
- **It runs on modest hardware.** No GPU, and no need for a machine with modern vector
  instructions — the whole thing is comfortable on a low-power four-core box with 8 GB of RAM.

### So where is the AI, then?

There is machine learning in here, but only for *understanding* what you said — never for
deciding what to say back.

A small language model converts a sentence into a list of numbers that represents its
meaning. Sentences that mean similar things end up with similar numbers. That is what lets
"I'm knackered" find the same authored reply as "I'm exhausted", even though the bot was
never shown the word "knackered". The same trick powers long-term recall: it can find
something you mentioned three months ago because the meaning matches, not because you used
the same words.

The model reads. It never writes. Every word that reaches Discord came out of a file a human
wrote.

## What it can do

| | |
|---|---|
| **Chat** | Mention it and it replies in character. It tracks its own mood, remembers facts about you between conversations, recalls things you said long ago, and treats you differently depending on how much history you two have. |
| **Music** | A full player backed by Lavalink: queue, playlists, audio filters, now-playing, per-server stats. It copes with being dragged between voice channels mid-song. |
| **Levels** | Experience points for chatting, with anti-spam cooldowns, streaks, role rewards and leaderboards. |
| **Moderation** | Warn, mute, kick and ban with a searchable case log; temporary bans that survive a restart; bulk message purge; an audit trail; a permission pre-flight check; and private moderator threads. |
| **Utility** | Reminders, birthdays, events with RSVP, messages scheduled far into the future, a quote board, milestones. |
| **Privacy** | One command shows you everything stored about you, exports it, or deletes it. The bot does not log what people say — only counts and timestamps — and a test inspects every column in the database to prove it. |

That comes to 98 slash commands (86 distinct names, since groups reuse words like `list` and
`set`) across 28 command modules. Discord is the only way in: there is no website and no web
API, and an architecture test fails the build if anyone adds one.

## How the code is arranged

It is one program. When it runs, that single process talks to Discord, holds the
conversations, plays the music and runs the scheduled jobs. Alongside it run four supporting
services in Docker containers: PostgreSQL (the database, with a `pgvector` extension for the
meaning-matching described above), Redis (a short-lived cache), Lavalink (the audio engine)
and a small helper for YouTube playback.

The source is split into layers so that the interesting parts can be tested without needing
a Discord account or a database:

```
src/Sonarr.Domain          the vocabulary: what a user, a case, an episode is
src/Sonarr.Elaine          the chat engine — pure logic, no Discord, no database, no network
src/Sonarr.Application     what each feature actually does
src/Sonarr.Infrastructure  the plumbing: database, cache, the meaning model, audio
src/Sonarr.Bot             the program itself: Discord commands and background jobs
src/Sonarr.Iris            a command-line tool for whoever runs the server
src/Sonarr.Migrator        a one-off importer from the bot's previous life
persona/                   the authored replies, as YAML files
tests/                     1,340 automated tests
deploy/                    everything needed to put it on a server
```

Two rules are enforced by tests rather than by good intentions. Dependencies only ever point
one direction — the Discord layer may call the feature layer, never the reverse. And
`Sonarr.Elaine`, the chat engine, is allowed to reference nothing but the standard library, so
it cannot even accidentally reach a database or the network. That is why the entire
personality can be tested in about two seconds with no server running anywhere.

**A note on the names.** The bot is called Sonarr and that is the only name a user ever sees.
Internally the chat engine is called Elaine and the command-line tool is called Iris. If you
read the source and wonder who they are, that is the answer — they are parts, not products.

## Trying it yourself

You need the .NET 10 SDK, Docker, and a Discord bot token from the
[Discord developer portal](https://discord.com/developers/applications).

```sh
cp .env.example .env          # then fill it in — see below
docker compose up -d          # starts PostgreSQL and Redis, on your machine only
sh scripts/fetch-model.sh     # downloads the meaning model (~90 MB, one time)
dotnet run --project src/Sonarr.Bot
```

Six values in `.env` have to be filled in, and the bot refuses to start without them rather
than failing confusingly later on:

| | |
|---|---|
| `DISCORD_TOKEN` | from the developer portal above |
| `PG_CONNECTION` and `POSTGRES_PASSWORD` | the database; keep the password identical in both |
| `LAVALINK_PASSWORD` and `LAVALINK_URI` | the audio engine — required even for a run with no music, because the bot checks its configuration all at once at startup |
| `ADMIN_USER_IDS` | your own Discord user ID. This is who may change settings that apply to every server, so an empty list means nobody can |

`.env.example` lists every key with a comment explaining what it is for. To find your Discord
user ID: turn on Developer Mode in Discord's settings under Advanced, then right-click your
own name and choose Copy User ID.

Two things about the commands above. The database and cache are deliberately reachable only
from your own machine and never from the network. And the model download is optional — skip
it and the bot still runs, but it will only recognise phrasings somebody anticipated, losing
the "I'm knackered" trick described above.

`docker compose up -d` starts the database and cache only, not the audio engine, so music will
not work in a plain development run. That is intentional: it keeps first-time setup to two
containers. The full set of services for a real server lives in `deploy/`.

Set `DISCORD_DEV_GUILD_ID` to your own test server. Commands then appear immediately instead
of taking up to an hour to propagate across Discord.

```sh
dotnet test                   # all 1,340 tests; takes a few seconds
```

Nothing secret is in this repository. `.env` holds the bot token and the database passwords
and is excluded from Git; `.env.example` lists the same keys with the values left blank.

## Health checks that repair rather than complain

There is a command-line tool for whoever runs the server. Its most useful command runs ten
checks — disk space, configuration, database, database schema, cache, audio engine, the
authored replies, the meaning model, backups, and whether the bot itself is running:

```sh
dotnet run --project src/Sonarr.Iris -- health     # on a development machine
sonarr health                                      # on a server, where it is installed
```

Most tools like this tell you something is wrong and leave. This one tries to fix it: it
starts a container that has stopped, applies a database update the code is waiting on,
downloads a model that went missing, takes a fresh backup, reclaims disk space. Each line
reports what was broken, what was attempted, and whether it worked. When it genuinely cannot
help, it says what is broken, what it tried, and what you should do about it.

Four things it refuses to do, on purpose:

- **Rewrite the authored replies.** A mistake in those files is a human's writing error, and
  a machine editing the bot's personality to make an error go away is worse than the error.
- **Restart a service that keeps crashing.** Restarting a service that is *stopped* is
  helpful; restarting one that starts and immediately dies just hides the reason it died.
  The report points at the log instead.
- **Delete a backup or a log to free up space.** It clears rebuildable caches only. Deleting
  a backup to free disk would destroy the exact thing you came to protect.
- **Guess a password.** A rejected credential is reported, never worked around.

Add `--check` to make it examine and report without changing anything, which is what you want
from an automated monitor.

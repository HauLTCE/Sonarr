# sonarr-bot — the Discord gateway (docs/02-architecture.md). No HTTP surface: the panel and
# its API are gone (docs/06), so this image listens on nothing.
# Build from the REPO ROOT: docker build -f src/Sonarr.Bot/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Manifests first so `restore` is cached until a csproj or the lock/props files change.
COPY Directory.Build.props Directory.Packages.props Sonarr.slnx ./
COPY src/Sonarr.Domain/Sonarr.Domain.csproj                   src/Sonarr.Domain/
COPY src/Sonarr.Elaine/Sonarr.Elaine.csproj                   src/Sonarr.Elaine/
COPY src/Sonarr.Application/Sonarr.Application.csproj         src/Sonarr.Application/
COPY src/Sonarr.Infrastructure/Sonarr.Infrastructure.csproj   src/Sonarr.Infrastructure/
COPY src/Sonarr.Bot/Sonarr.Bot.csproj                         src/Sonarr.Bot/
COPY src/Sonarr.Migrator/Sonarr.Migrator.csproj               src/Sonarr.Migrator/
COPY tests/Sonarr.Elaine.Tests/Sonarr.Elaine.Tests.csproj     tests/Sonarr.Elaine.Tests/
COPY tests/Sonarr.Application.Tests/Sonarr.Application.Tests.csproj tests/Sonarr.Application.Tests/
RUN dotnet restore src/Sonarr.Bot/Sonarr.Bot.csproj \
    && dotnet restore src/Sonarr.Migrator/Sonarr.Migrator.csproj

COPY src/ src/
RUN dotnet publish src/Sonarr.Bot/Sonarr.Bot.csproj -c Release --no-restore -o /app

# The Migrator rides along in /app/migrator instead of getting its own image. Nothing
# applies migrations at boot on purpose (a gateway that silently reshapes the DB on
# restart is how you lose data), so `migrate` has to be runnable in prod — and the server
# has no .NET SDK. One image, two entrypoints:
#     docker compose run --rm --entrypoint dotnet bot /app/migrator/Sonarr.Migrator.dll migrate
# It shares the bot's .env, so it needs no connection string of its own. Costs ~15MB of
# shared-runtime assemblies; a second image would cost 200MB and drift from the bot.
RUN dotnet publish src/Sonarr.Migrator/Sonarr.Migrator.csproj -c Release --no-restore -o /app/migrator

# --- runtime: the plain .NET runtime, no SDK and no ASP.NET Core ---
# Was `aspnet:10.0`, which carries the ASP.NET Core shared framework (~20 MB) for a Kestrel
# that no longer exists. `runtime:10.0` is the same Ubuntu 24.04 base with the same tzdata and
# the same `app` uid 1654, so nothing below changes.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# BackupRunner shells out to pg_dump (docs/08) — nothing in .NET writes a pg_restore-compatible
# archive, and a restore drill that needs a custom tool is a drill that fails.
#
# It has to be client 17+, because pg_dump refuses outright against a newer server and the
# compose file pins pgvector/pgvector:pg17. This base is Ubuntu 24.04 (noble), whose
# `postgresql-client` is 16 — verified by installing it and getting 16.14 — so the version
# comes from PGDG, pinned explicitly rather than taken from whatever the base happens to
# ship. The signing key is COPYied from the build context, so this needs no curl/gnupg in the
# final image. `BackupProbe` goes red at boot if pg_dump ever disappears from PATH.
#
# It was an `ADD --checksum=… https://www.postgresql.org/media/keys/ACCC4CF8.asc`, and that kept
# failing on GitHub Actions with `NO_PUBKEY 7FCC7D46ACCC4CF8` while the identical Dockerfile built
# fine on the server. The checksum was not the fix it looked like. Two probes against this base:
#
#   1. A checksummed ADD reuses a layer cached by a NON-checksummed ADD of the same URL — so the
#      checksum is not part of the cache key. GHA's type=gha cache still held the entry from before
#      the checksum existed, matched it on the URL string alone, and replayed it.
#   2. A cache hit does NOT bypass verification: the same URL with a deliberately wrong hash fails
#      `digest mismatch` off a warm cache.
#
# So the cached bytes were valid and the checksum was enforced — the fetch was never the broken
# part, and pinning it harder could not have helped. What breaks is depending on a remote signed
# repo mid-build at all: the failure is remote, intermittent, and reproduces on neither machine on
# demand. (`apt-get update` still talks to apt.postgresql.org for the package itself; only the trust
# root is now local, which is the half that was failing.)
#
# COPY has none of that: the bytes are in git, the cache key is their content, and there is no URL
# to key a stale layer on. Verified before committing — sha256
# 0144068502a1eddd2a0280ede10ef607d1ec592ce819940991203941564e8e76, and `gpg --show-keys`
# fingerprint B97B 0AFC AA1A 47F0 44F2 44A0 7FCC 7D46 ACCC 4CF8, the same key id apt said was
# missing. Marked `*.asc binary` in .gitattributes so no eol filter can rewrite what apt verifies.
#
# To rotate: fetch the new key, check its fingerprint by hand, replace the file. No hash to update.
COPY --chmod=644 src/Sonarr.Bot/pgdg-ACCC4CF8.asc \
    /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
RUN echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-17 \
    && rm -rf /var/lib/apt/lists/* \
    && pg_dump --version | grep -qE ' 1[7-9]| [2-9][0-9]'

# `app` (uid 1654) ships with the runtime image. Mounted dirs must be writable by it:
# /backups (pg_dump output) is created here so a fresh host bind mount has sane ownership.
RUN mkdir -p /backups /app/persona && chown -R app:app /backups /app/persona
USER app

# `InvariantGlobalization=true` (Directory.Build.props) drops ICU, which breaks IANA
# timezone lookups on Windows dev boxes — but not here: this base image ships `tzdata`
# (verified 2026-07-27), and on Linux TimeZoneInfo reads /usr/share/zoneinfo, not ICU.
# So `/timezone` works in the container. Don't switch to a base without tzdata.
#
# The other half of dropping ICU is culture *creation*: Discord.Net builds a CultureInfo from
# each guild's PreferredLocale, which throws CultureNotFoundException unless
# PredefinedCulturesOnly=false is also set. It is (same props file) — don't remove one without
# the other, or GUILD_AVAILABLE starts failing and guilds silently never populate.

ENV DOTNET_gcServer=0 \
    TZ=Asia/Ho_Chi_Minh
# ^ TZ, because the container is otherwise UTC under a +07 host and every log line plus every
# sleep-window boundary reads seven hours off. compose can override it; this is the default so
# a bare `docker run` is right too. It resolves out of /usr/share/zoneinfo (tzdata, above) —
# ICU is stripped, and that only affects cultures, not zones.

ENTRYPOINT ["dotnet", "Sonarr.Bot.dll"]

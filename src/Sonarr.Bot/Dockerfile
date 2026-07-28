# sonarr-bot — Discord gateway + panel API in one process (docs/02-architecture.md).
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

# --- runtime: aspnet only, no SDK ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# BackupRunner shells out to pg_dump (docs/08) — nothing in .NET writes a pg_restore-compatible
# archive, and a restore drill that needs a custom tool is a drill that fails. Debian 13 ships
# postgresql-client 17, matching the postgres:17 in the compose file; a client older than the
# server refuses to dump at all. `BackupProbe` goes red at boot if this ever disappears.
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*

# `app` (uid 1654) ships with the aspnet image. Mounted dirs must be writable by it:
# /backups (pg_dump output) is created here so a fresh host bind mount has sane ownership.
RUN mkdir -p /backups /app/persona && chown -R app:app /backups /app/persona
USER app

# `InvariantGlobalization=true` (Directory.Build.props) drops ICU, which breaks IANA
# timezone lookups on Windows dev boxes — but not here: this base image ships `tzdata`
# (verified 2026-07-27), and on Linux TimeZoneInfo reads /usr/share/zoneinfo, not ICU.
# So `/timezone` works in the container. Don't switch to a base without tzdata.

ENV ASPNETCORE_URLS=http://0.0.0.0:5088 \
    DOTNET_gcServer=0

# Dev convenience only. Prod uses `network_mode: host` (docs/11) so this mapping is
# ignored there and the process binds :5088 on the host directly.
EXPOSE 5088

ENTRYPOINT ["dotnet", "Sonarr.Bot.dll"]

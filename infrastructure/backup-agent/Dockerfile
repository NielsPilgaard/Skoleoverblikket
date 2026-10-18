# Backup agent (task 60 D8): Postgres binaries (pg_receivewal, pg_ctl for the drill and restore
# checks), pgBackRest, rclone and the .NET console, in one image. The same image runs the weekly
# drill, the console, and a rebuild on a fresh VPS. Build context: the repo root.
#
# PG_MAJOR must match infrastructure/postgres: a physical restore needs the same major.
ARG PG_MAJOR=17

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY infrastructure/backup-agent/Skoleoverblikket.BackupAgent.csproj infrastructure/backup-agent/
RUN dotnet restore infrastructure/backup-agent/Skoleoverblikket.BackupAgent.csproj --runtime linux-x64
COPY infrastructure/backup-agent/ infrastructure/backup-agent/
RUN dotnet publish infrastructure/backup-agent/Skoleoverblikket.BackupAgent.csproj \
    --no-restore --configuration Release --runtime linux-x64 --self-contained \
    --output /app

FROM postgres:${PG_MAJOR} AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends pgbackrest rclone ca-certificates curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /pgdata/a /pgdata/b /pg-control /var/lib/backup-agent /wal-receive /drill \
 && chown -R postgres:postgres /pgdata /pg-control /var/lib/backup-agent /wal-receive /drill \
 && chmod 0700 /pgdata/a /pgdata/b /drill

COPY infrastructure/backup-agent/pgbackrest.conf /etc/pgbackrest/pgbackrest.conf
COPY --from=build /app /app

# Runs as the Postgres uid (999), so it can read the live data volume (0700) and own what it restores.
USER postgres
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:9090 \
    DOTNET_ENVIRONMENT=Production \
    PGBACKREST_CONFIG=/etc/pgbackrest/pgbackrest.conf
EXPOSE 9090
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD curl -fsS http://127.0.0.1:9090/healthz || exit 1
ENTRYPOINT ["/app/Skoleoverblikket.BackupAgent"]
# Clear the base image's CMD ("postgres"), or it would be passed to the agent as an argument.
CMD []

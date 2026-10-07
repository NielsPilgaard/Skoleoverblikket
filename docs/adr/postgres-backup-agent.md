---
title: 'ADR: Postgres in compose, backed up by a separate backup agent'
status: 'Accepted'
date: '2026-10-07'
authors: 'Niels Pilgaard Grøndahl'
tags: ['infrastructure', 'database', 'backup']
supersedes: 'self-hosted-postgres-backups (backup part)'
superseded_by: ''
description: >-
  One Postgres cluster in docker compose (app + Keycloak) with no backup tooling
  and no archive_command. A backup-agent container streams WAL over a shared
  socket with pg_receivewal into a persistent volume, pushes it to pgBackRest,
  runs backups, drills and restores, and restores only into a spare A/B volume.
  15-minute RPO, 14-day retention. Phase 0 spike results included.
---

# ADR: Postgres in compose, backed up by a separate backup agent

## TL;DR

Postgres runs as one compose service holding both databases (task [54](../../tasks/54-move-vps.md)). It has no `archive_command` and no pgBackRest. A `backup-agent` container ([task 60](../../tasks/60-backup-console.md)) shares its socket and reads its data volume read-only, streams WAL with `pg_receivewal` into a persistent `wal-receive` volume, pushes it with `pgbackrest archive-push`, and runs backups, the weekly drill and restores. A restore goes into a spare volume (`pgdata-a`/`pgdata-b`) and goes live when a human swaps `PG_VOLUME`. A broken backup setup becomes a WAL gap and an alert, never an outage.

## Status

**Accepted** 2026-10-07, after the task 60 Phase 0 spike (below). Goes live on prod with the task 54 cutover. Supersedes the backup part of [self-hosted-postgres-backups](self-hosted-postgres-backups.md); self-hosting itself is unchanged.

## Context

- Dokploy's daily `pg_dump` gives a 24h RPO, keeps 30 dumps against the 14 days the DPA promises, and nobody has restored one.
- With `archive_command`, Postgres calls the archiver itself. A broken pgBackRest config, binary or S3 key makes archiving fail, `pg_wal` fills the disk and prod stops.
- After task 54 one cluster holds the app and Keycloak databases. When it is down nobody can log in, so a restore UI behind Keycloak is down exactly when it's needed.
- Restoring over the live data directory destroys the evidence and leaves no way back.

## Decision

- **D1: Separate container, shared socket.** The agent mounts the socket volume and the live data volume read-only and runs as uid 999. The Postgres image carries no backup tooling.
- **D2: WAL by streaming, not `archive_command`.** `pg_receivewal --slot=agent --synchronous` over the socket. `max_slot_wal_keep_size = 4GB` caps what Postgres holds for the agent; past it Postgres drops the slot and keeps running. The agent commits a heartbeat row and calls `pg_switch_wal()` every 5 minutes, so the 15-minute RPO holds on quiet nights and every time target has a commit after it.
- **D2a: Received WAL lives on a persistent `wal-receive` volume** until `archive-push` succeeds, because the slot moves on as soon as `pg_receivewal` flushes. Segments are deleted only after a successful push, leftovers are pushed on start, and streaming pauses at 4 GB unpushed so the agent can't fill the disk either.
- **D5: Restore into an A/B volume.** Compose declares `pgdata-a` and `pgdata-b`. Postgres mounts `${PG_VOLUME}`, the agent mounts it read-only and `${PG_SPARE_VOLUME}` read-write. Going live = swap the two variables in Dokploy and redeploy. The agent has no docker socket.

The console, the ops bucket and the backoffice card are task 60 D3, D4, D6 and stay specified there.

### Phase 0 spike results (2026-10-07, Postgres 17.6, pgBackRest 2.59.3)

| Check | Result |
|---|---|
| `pg_receivewal` over the shared socket, peer auth mapped to `backup_agent` | Works. The agent's uid 999 can't log in as `postgres` (scram on the socket) |
| `archive-push` from the receive directory | Works, re-push is idempotent. `.partial` is never pushed while streaming |
| `pgbackrest backup` with a read-only `pg1-path` | Works, **but only with `archive-check=n`**: without `archive_mode`, pgBackRest refuses (`[087] archive_mode must be enabled`) |
| `pg1-path` mounted elsewhere than `data_directory` | Refused (`[058]`). The agent mounts the live volume at `/var/lib/postgresql/data` |
| Restore into the spare volume, PITR to a time | Recovery stopped 3 s after the target |
| Slot past `max_slot_wal_keep_size` with the agent stopped | Postgres kept serving, slot `lost`, `pg_receivewal` fails with "requested WAL segment … has already been removed" |

**Streamed WAL is the design. The spool-copy fallback isn't needed.**

## Consequences

### Positive

- **POS-001**: A broken pgBackRest, S3 key or agent can't stop Postgres. Worst case is a WAL gap that the console and the heartbeat report.
- **POS-002**: App and Keycloak restore to the same point in time, so the Keycloak/app reconcile step in the old runbook is gone.
- **POS-003**: A restore never touches the live volume, and the old volume stays for forensics until it is deleted on purpose.
- **POS-004**: RPO goes from 24h to 15 minutes, with point-in-time restore to "just before migration X".

### Negative

- **NEG-001**: `archive-check=n` rules out pgBackRest's `archive-copy`, so a backup isn't self-contained: it needs the archive. The agent checks after each backup that its stop segment reached the repo and fails the backup otherwise.
- **NEG-002**: The agent is our own code (~3,700 lines of C#, a third of it console HTML) where Dokploy's backup feature was none. It's proven by hand, not by tests ([TESTING.md](../TESTING.md)).
- **NEG-003**: Two volume variables (`PG_VOLUME`, `PG_SPARE_VOLUME`) must be swapped together. Compose can't derive one from the other. The agent refuses to touch a spare that is the live volume (same device and inode).
- **NEG-004**: Docker network separation alone did not keep other containers out on Docker Desktop: a container on another network reached the agent by IP, masqueraded to the same gateway address as the SSH tunnel. The console therefore also needs a key that only `docker exec` on the host can read.

## Alternatives Considered

### `archive_command` running pgBackRest inside the Postgres image

- **ALT-001**: **Description**: The standard pgBackRest setup.
- **ALT-002**: **Rejection Reason**: a broken archiver fills `pg_wal` and stops prod.

### Capped spool copy as `archive_command`

- **ALT-003**: **Description**: `[ spool < 4GB ] && cp %p /spool/%f || exit 0`, the agent pushes from the spool.
- **ALT-004**: **Rejection Reason**: not needed, Phase 0 passed with streaming. Kept as the fallback if `pg_receivewal` ever misbehaves.

### Restore in place, or a docker socket for the agent

- **ALT-005**: **Description**: Stop Postgres and restore over its volume, or let the agent recreate containers.
- **ALT-006**: **Rejection Reason**: no way back from a bad restore, and the docker socket is root on the host.

### OVH managed Postgres

- **ALT-007**: **Description**: PITR built in, ~€44/month.
- **ALT-008**: **Rejection Reason**: still too expensive at our size ([self-hosted-postgres-backups](self-hosted-postgres-backups.md) ALT-001), and it would hold Keycloak's database outside our compose.

## Implementation Notes

- **IMP-001**: `infrastructure/postgres/` (image, `postgresql.conf`, `pg_hba.conf`, roles) and `infrastructure/backup-agent/` (.NET agent, `pgbackrest.conf`, Dockerfile, `docker-compose.dev.yml` for the local proof).
- **IMP-002**: In `docker-compose.prod.yml` both services sit behind the `selfhosted-db` profile until the task 54 cutover sets `COMPOSE_PROFILES=selfhosted-db`.
- **IMP-003**: The agent reads no school data. `backup_agent` can stream WAL, run backups, write its own heartbeat row and `SELECT` two app tables (`SchoolDeletionRecords`, `__EFMigrationsHistory`), granted by a migration. A database restored with `pg_restore` (the 54 cutover) needs that `GRANT` run by hand; the console shows it.
- **IMP-004**: Retention is time-based (`repo1-retention-full=13`) with daily fulls, per the task 54 retention note. The agent flags any backup older than 14 days.
- **IMP-005**: Runbook: [RESTORE.md](../RESTORE.md). Environment: [DEPLOYMENT.md](../DEPLOYMENT.md).

## Related Decisions

- [self-hosted-postgres-backups](self-hosted-postgres-backups.md) — self-hosting stays; its Dokploy `pg_dump` backup part is superseded here
- [file-storage-approach](file-storage-approach.md) — the ops bucket and the pgBackRest repo are separate OVH buckets next to the files bucket
- [ai-data-boundary](ai-data-boundary.md) — drills and restores run on the VPS; no personal data passes through GitHub

---
title: 'Backup agent and break-glass backup console'
purpose: 'Spec for the backup-agent container that owns WAL streaming, pgBackRest backups, drills and restores, and the SSH-only console that shows backup health and runs restores even when Postgres, Keycloak or the API are down.'
description: >-
  A backup-agent container next to Postgres (shared socket, PGDATA read-only)
  streams WAL with pg_receivewal, runs pgBackRest backups, drills and restores,
  and writes a PII-free status.json to a separate OVH S3 ops bucket. Its web
  console is published on 127.0.0.1 only and reached over an SSH tunnel, so it
  works without Keycloak or the app DB and is the one place with a restore
  button. Restores go to a spare A/B volume, never over the live one. The
  backoffice gets a read-only status card from S3.
status: 'Proposed'
---

# Backup agent and break-glass backup console

## TL;DR

Task [54](54-move-vps.md) moves Postgres into compose with pgBackRest, so Dokploy's backup UI (list, manual backup, restore button) goes away. This task replaces it. A `backup-agent` container sits next to Postgres and does everything backup-related: it streams WAL with `pg_receivewal` (Postgres has no `archive_command`, so pgBackRest problems never become prod problems), runs pgBackRest backups, the [53](53-restore-drill.md) drill and restores, and writes a PII-free `status.json` to a separate OVH S3 bucket. Its console is published on `127.0.0.1:9090` only and opened with `ssh -L`, so it works when Postgres, Keycloak or the API are down. It's the only place with a restore button. A restore goes into a spare volume (`pgdata-b`), gets checked, and goes live when you change one env var in Dokploy. The backoffice gets a read-only status card read from S3, with no actions.

## Context

- After [54](54-move-vps.md), one Postgres cluster in compose holds the app DB **and** the Keycloak DB. If that cluster is down, nobody can log in, so any UI behind Keycloak (the backoffice) is down exactly when a restore is needed.
- With `archive_command`, Postgres calls the archiver itself. A broken pgBackRest config or binary makes it fail, `pg_wal` grows until the disk is full, and prod stops. Moving pgBackRest to another container doesn't help as long as `archive_command` depends on it.
- A restore button behind a superadmin JWT means a stolen token can roll every school back. A full-dump download puts every school's personal data on a laptop.
- The API must never hold backup credentials, the docker socket or a Dokploy API key. Dokploy keys can't be scoped, so they can read every env secret.

Constraints: data in the EU, no personal data through GitHub ([ai-data-boundary](../docs/adr/ai-data-boundary.md)), new infra < $5/month.

## Decisions (proposed)

- **D1: Separate container, shared socket.** `backup-agent` mounts the Postgres socket volume (`/var/run/postgresql`) and the prod `pgdata` volume **read-only**, and runs as the Postgres uid (999). The Postgres image has no pgBackRest. pgBackRest problems can't take Postgres down.
- **D2: WAL by streaming, not `archive_command`.** The agent runs `pg_receivewal` against a physical replication slot over the socket and pushes finished segments with `pgbackrest archive-push`. `max_slot_wal_keep_size = 4GB` caps what Postgres holds for the agent. If the agent is down longer than that, Postgres drops the slot and keeps running: a gap in the WAL chain and an alert, never a prod outage. The agent runs `SELECT pg_switch_wal()` every 5 minutes so the 15-minute RPO holds on quiet nights.
- **D3: Status lives in OVH S3, not the app DB.** A separate bucket `skoleoverblikket-ops` (not the backup repo, not the files bucket) holds `status.json` and `history/*.json`. It survives a broken DB and a restore that rewinds the DB. The agent's key is read-write on it. The API gets a key that can only **read** this bucket. OVH S3 is our most durable storage, but it's the same vendor as the VPS, so the backup repo still gets the Scaleway copy from 53 Phase 2.
- **D4: All actions live in the agent console, reached only via SSH tunnel.** The console listens on `127.0.0.1:9090` on the host (`ports: ["127.0.0.1:9090:9090"]`). It isn't reachable from the internet, and the agent isn't on `dokploy-network`, so other containers, including the API, can't reach it. Auth is the SSH key. The console has no login of its own and doesn't depend on Keycloak or the app DB. Anyone with a shell on the host is already root, so a console login would add nothing.
- **D5: Restore into an A/B volume, without a docker socket.** Compose declares `pgdata-a` and `pgdata-b`. Postgres mounts `${PG_VOLUME:-pgdata-a}`. The agent mounts the live one read-only and the spare one read-write. A restore writes into the spare volume, the agent checks it with a temporary Postgres process inside its own container, and a human flips `PG_VOLUME` in Dokploy and redeploys. The old volume stays untouched for forensics. The agent never gets the docker socket.
- **D6: The backoffice is read-only.** It shows one status card (from `status.json`) and an "open the console via SSH" hint. No buttons, no job queue, no API write path to anything backup-related. A stolen superadmin token gains nothing.
- **D7: No full-dump download.** A school's data from a past point in time goes through Phase D (a per-school export from a restore), never as a whole-cluster dump.
- **D8: Agent stack.** A .NET minimal API with server-rendered HTML (no React, no build step, no CDN). The image is `postgres:<prod major>` binaries + pgbackrest + rclone + the .NET runtime. Same image for the drill (53), the console and a rebuild on a fresh VPS.

## Which UI works when

| Situation | Status | Actions |
|---|---|---|
| Normal | Backoffice card + console | Console |
| App DB or Keycloak broken | Console, elmah.io heartbeats | Console |
| API down | Console | Console |
| Agent down | Backoffice card goes stale (shows age), elmah.io heartbeat alerts | SSH + Dokploy, fix the agent |
| VPS gone | elmah.io | `docker run -p 127.0.0.1:9090:9090 --env-file …` the agent image on a new box, keys from the password manager. Same console, same restore. |

## Scope

### Phase 0: Spike (do first, ½ day)

Prove D1 and D2 before building on them. On a scratch compose with the 54 Postgres image:

- [ ] `pg_receivewal --slot=agent --synchronous` over the shared socket (`pg_hba`: `local replication backup_agent peer` or scram). Confirm segments arrive.
- [ ] `pgbackrest archive-push` accepts finished segments from `pg_receivewal`'s directory (not `pg_wal`). Check the `.partial` file is never pushed.
- [ ] `pgbackrest backup --type=full` works with `pg1-path` mounted **read-only** and `pg1-socket-path` on the shared volume.
- [ ] `pgbackrest restore` into the spare volume, then a PITR to a target time, lands within 5 minutes of it.
- [ ] Stop the agent and fill WAL past `max_slot_wal_keep_size`: Postgres keeps running, the slot shows `wal_status = lost`, and the agent detects it on restart.

**Fallback if archive-push or read-only backup fails:** `archive_command` becomes a capped copy to a `wal-spool` volume (`[ spool < 4GB ] && cp %p /spool/%f || exit 0`, a few lines of shell, no pgBackRest in the Postgres image), and the agent pushes from the spool. Same separation, a bit more of our own code. Record which one we picked in the 54 ADR.

### Phase A: Agent core (absorbs most of 53 Phase 1)

New project `infrastructure/backup-agent/` (Dockerfile + .NET minimal API). Background loops:

- [ ] **WAL**: supervise `pg_receivewal`, push finished segments, `pg_switch_wal()` every 5 min. Restart on crash. Detect a lost slot, recreate it, mark a gap and go `Unhealthy`.
- [ ] **Backups**: daily full at 02:00 Europe/Copenhagen, `expire` after each, weekly `verify`. Retention rules from [54](54-move-vps.md) §1 (retention note) apply unchanged.
- [ ] **Drill**: the weekly [53](53-restore-drill.md) drill (restore into tmpfs, checks, PII-free summary). The bash `drill.sh` from 53 becomes agent code.
- [ ] **Status**: every 5 min write `status.json` to the ops bucket: newest full backup, newest WAL in the repo (the real RPO), oldest restorable point vs. 14 days, slot `wal_status` and retained WAL, disk %, last drill result and duration, last verify, schedule of the next runs. Append each run (backup, drill, verify, restore) to `history/YYYY-MM.json`.
- [ ] **Heartbeats**: elmah.io heartbeats per 53 D3 (WAL every 30 min, backup daily, drill weekly). A stale `status.json` is itself caught by the WAL heartbeat.
- [ ] Compose ([docker-compose.prod.yml](../infrastructure/docker/docker-compose.prod.yml)): `backup-agent` service, own network (not `dokploy-network`), `ports: ["127.0.0.1:9090:9090"]`, volumes: `pg-socket` (shared), live `pgdata-*` read-only, spare `pgdata-*` read-write, a tmpfs for the drill. Memory limit so a drill can't starve Postgres.
- [ ] Postgres: `pg-socket` volume for `/var/run/postgresql`, `wal_level = replica`, `max_wal_senders`, `max_replication_slots`, `max_slot_wal_keep_size = 4GB`, a `backup_agent` role with `REPLICATION` + `pg_read_all_settings` + `pg_checkpoint` (only what pgBackRest needs).
- [ ] Ops bucket `skoleoverblikket-ops` at OVH (same region as the repo). Two users: agent (RW) and api (read-only). Lifecycle: delete `history/` after 400 days (PII-free, but no reason to keep it forever).
- [ ] CI: build and push `ghcr.io/nielspilgaard/skoleoverblikket-backup-agent` like the other `publish-*` jobs. Pin a tag in compose.

### Phase B: Console (read + safe actions)

Server-rendered pages on `:9090`, in Danish like the backoffice:

- [ ] **Overview**: big "Data sikret for X min siden" (newest WAL in repo), last full backup, oldest restorable point with a red flag if older than 14 days (DPA, `BACKUP_RETENTION_DAYS` in [dataProcessing.ts](../web/src/content/dataProcessing.ts)), slot health and retained WAL vs. the 4 GB cap, disk %, heartbeat states.
- [ ] **Backups**: list of full backups (time, size, duration) and the continuous WAL range, from `pgbackrest info --output=json`.
- [ ] **Drills**: history with each check's result, and restore duration as a trend (the measured RTO). A form to log the quarterly manual drill from 53 Phase 3 (date, RTO, what broke), with the next due date.
- [ ] **Deleted-schools ledger**: schools deleted in the last 14 days that are still in backups, and the date each one ages out. There is no deletion record today ([SchoolDeletionService](../api/Skoleoverblikket.Api/Services/SchoolDeletionService.cs) only logs). Add a non-tenant `SchoolDeletionRecords` table (school ID, name, deleted at), written by `SchoolDeletionService` in the same transaction as the delete. The agent reads it over the socket (`SELECT` on that table only) and mirrors it to `ledger.json` in the ops bucket, because a restore rewinds the table but not the bucket. School name and dates only, no personal data. Rows older than 14 days are deleted by `SchoolRetentionJob`.
- [ ] **Actions**: "Tag backup nu" (full or incremental, at most once per hour, warns between 07 and 16 because of I/O), "Kør drill nu", "Kør verify nu". Each shows live log output and lands in history.
- [ ] **Audit**: every action is written to `history/` with time and the action. No user name (SSH is the identity). The SSH login itself is in the host's `auth.log`.

### Phase C: Restore wizard + backoffice card

- [ ] **Pick a point**: a time (Europe/Copenhagen), "just before migration X" (from `__EFMigrationsHistory` times in the newest backup), or a full backup. Points outside the restorable range are disabled.
- [ ] **Restore to the spare volume**: wipe the spare volume (confirm by typing its name), `pgbackrest restore --type=time --target=…` into it, start a temporary Postgres on it inside the agent, run the drill checks, show a summary: per-table counts vs. live, latest migration, Keycloak realm + user count, reached recovery time.
- [ ] **Resurrected schools**: list schools that exist in the restore but were deleted later (compare the restored `Tenants` with `ledger.json` in S3, not with the restored table). Show that `SchoolRetentionJob` will re-delete them on startup, or that they must be deleted by hand (answer from 53's open question).
- [ ] **Go live** (human step, the UI only shows it): "Skaler `api` og `keycloak` til 0 i Dokploy → sæt `PG_VOLUME=pgdata-b` → redeploy." The agent then detects the new live volume, mounts are swapped on redeploy, and it takes a full backup right away (a restore starts a new timeline).
- [ ] **Post-restore checklist** in the console: Stripe webhook resend since the target time, re-delete resurrected schools, smoke test with the smoke tenant, check elmah.io, GDPR breach assessment within 72h (53 runbook §6). Tick-offs go to `history/`.
- [ ] **Keep the old volume** until you click "Slet gammel volume" (typed confirmation), at most 14 days (DPA). The console shows its age and nags after 7.
- [ ] **Backoffice card** on `/backoffice`: API endpoint `GET /api/v1/superadmin/backup-status` reads `status.json` with the read-only key and returns it. The card shows the RPO, last full, last drill, retention flag, and how old the status is ("opdateret for 4 min siden", red after 15). Below: `ssh -L 9090:127.0.0.1:9090 <vps>` to copy. No buttons.

### Phase D (later, only when a school asks): per-school export from a point in time

Restore to the spare volume (Phase C), then run the task-51 export filtered on one tenant against it and hand out the ZIP via the existing single-use download link. Needs the export code to accept another connection string. Not in this task.

### Docs

- [ ] `docs/RESTORE.md` (from 53): the console is step 1 of every incident. Add how to reach it, the A/B volume flip, and "VPS gone: run the agent image on a new box".
- [ ] The new ADR from 54 §7 records D1, D2 and D5 (topology, streaming WAL, A/B volumes).
- [ ] [DEPLOYMENT.md](../docs/DEPLOYMENT.md): ops bucket keys (agent RW, API read-only), `PG_VOLUME`.

## Testing

`GET /api/v1/superadmin/backup-status` gets one tUnit test: superadmin gets 200, admin gets 403. Use a fake S3 status object, not real OVH. Extend the existing school deletion test: a deleted school leaves one `SchoolDeletionRecords` row. The agent is infrastructure ([TESTING.md](../docs/TESTING.md)), so prove it by hand, breaking each part on purpose:

- [ ] Kill the agent for longer than the slot cap: Postgres keeps serving, the WAL heartbeat goes `Unhealthy`, the console shows the gap after restart, and the next full backup clears it.
- [ ] Wrong S3 key: archive-push fails, retained WAL grows in the console, the heartbeat alerts before the cap.
- [ ] Stop Postgres: the console still loads, shows the last status and the restore wizard.
- [ ] From another container on `dokploy-network` and from outside: `curl <host>:9090` fails.
- [ ] Full restore on staging through the wizard: restore to `pgdata-b`, flip `PG_VOLUME`, app comes up on the target time, flip back works.
- [ ] Ops bucket key from the API container can't write or delete (`rclone touch` fails).

## Done when

- Phase 0 decided and written into the 54 ADR.
- Phases A–C live on prod. One full restore through the wizard done on staging, with the measured time logged in the console.
- `docs/RESTORE.md` starts with the console.

## Open questions

- **Agent memory**: drill restore + temporary Postgres + .NET in one container. Size the limit from the first drill (54 picks the VPS size).
- **Staging**: same agent with archiving off and no ops bucket, or a staging ops bucket? Leaning: a staging bucket, so the wizard can be tested end to end on staging.
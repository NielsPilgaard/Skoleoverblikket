---
title: 'Postgres into compose on the current VPS, with pgBackRest'
purpose: 'Plan and checklist for moving prod Postgres from Dokploy Database resources into the compose app on the current OVH VPS, and everything that is easy to forget when doing it.'
description: >-
  Postgres moves from Dokploy Database resources into docker-compose.prod.yml
  as one cluster (app + Keycloak DBs), on the current OVH VPS. A separate
  backup-agent container (task 60) streams WAL and runs pgBackRest to Object
  Storage (15-min RPO, point-in-time restore), so backup problems can't take
  Postgres down. Migrations run as a one-shot compose service, so Postgres has
  no public port. Room comes from removing an unrelated Dokploy stack. Cutover
  is a dump/restore with a short maintenance window. Pulls Phase 4 and 5 of
  task 53 forward. A bigger VPS waits until we scale.
status: 'Proposed'
---

# Postgres into compose on the current VPS, with pgBackRest

## TL;DR

**Decided 2026-10-31: no VPS move.** First remove the unrelated Dokploy stack from the VPS (frees 2–3 GB RAM). Then, on the same box, Postgres becomes a service in `docker-compose.prod.yml`: one cluster holding both the app DB and the Keycloak DB. The Postgres image stays free of backup tooling. A separate `backup-agent` container ([task 60](60-backup-console.md)) streams WAL over a shared socket and runs **pgBackRest** to OVH Object Storage, encrypted. Migrations become a one-shot `migrate` service, so CI no longer talks to the DB and port 5432 closes. Rehearse on a scratch stanza and temporary hostname first. Then cut over: stop app → dump the Dokploy DBs → restore into the compose Postgres → start. Keep the old Dokploy Database resources stopped but intact for 7 days, then delete them. Moving to a bigger VPS is [later](#later-bigger-vps).

## Switching to the backup agent, step by step

The short version of everything below that concerns Postgres and the backup agent. Secrets go in the password manager, never the repo. Variables: [DEPLOYMENT.md](../docs/DEPLOYMENT.md#self-hosted-postgres-and-the-backup-agent). Fits into the cutover runbook in §6.

**Prepare (any time)**

1. OVH: create the pgBackRest repo bucket with its own read-write key, and `skoleoverblikket-ops` with an agent key (read-write) and an API key (read-only). Lifecycle rule on the ops bucket: delete `history/` after 400 days.
2. Heartbeats: create four (WAL every 30 min, backup daily, drill and verify weekly) and a key that can only write heartbeats.
3. Generate `PGBACKREST_REPO1_CIPHER_PASS` (`openssl rand -base64 48`) and the three Postgres passwords.

**Pin the images (any time, one PR)**

4. In [docker-compose.prod.yml](../infrastructure/docker/docker-compose.prod.yml), set the `postgres` and `backup-agent` image tags to `sha-<commit>` of a `main` commit whose publish jobs passed, merge. Prod doesn't change: the profile is still off there. **Done 2026-10-19:** both pinned to the PR #35 merge commit `af85693`. Move a pin only when the Postgres image or the agent changes.

**On the VPS, before the cutover (prod keeps running on the Dokploy DBs)**

5. Remove the unrelated Dokploy stack. Then check headroom with `free -h` and `df -h`: disk must fit three copies of the database (`pgdata-a`, `pgdata-b`, and the `drill` volume while a drill runs) plus 4 GB for `wal-receive`. Set `POSTGRES_MEMORY`, `PG_SHARED_BUFFERS`, `PG_EFFECTIVE_CACHE_SIZE` and `BACKUP_AGENT_MEMORY` (default 1g) so Postgres, the agent, the app and the Dokploy DBs fit at the same time during the cutover.
6. In Dokploy, check that the compose type is **Docker Compose**, not Stack (Stack ignores profiles).
7. `pwsh scripts/create-prod-volumes.ps1 ubuntu@<vps>`
8. Dokploy env: `COMPOSE_PROFILES=selfhosted-db` and the variables from step 1–3. Leave `DATABASE_URL` and `KEYCLOAK_DB_URL` pointing at the Dokploy DBs.
9. Deploy. Postgres starts empty and creates its roles and databases; the agent creates the pgBackRest stanza by itself. `api` and `keycloak` don't depend on it, so prod is unaffected.
10. Rehearse (§5).

**Cutover (§6)**

11. Restore the dump, then run the `GRANT` from [RESTORE.md](../docs/RESTORE.md#13-after-a-pg_restore-into-a-new-cluster) §13. Switch `DATABASE_URL` and `KEYCLOAK_DB_URL` to host `postgres` and redeploy.
12. `pwsh scripts/backup-console.ps1 ubuntu@<vps>`, then **Back up now**. Done when the console is green, WAL is less than 15 minutes old and all four heartbeats have checked in.

**After the cutover**

13. Remove the `selfhosted-db` profile from docker-compose.prod.yml and drop the profile from DEPLOYMENT.md, RESTORE.md §10 and the [ADR](../docs/adr/postgres-backup-agent.md). Merge, then delete `COMPOSE_PROFILES` in Dokploy.

## Context

Today ([phase-0-vps](completed/phase-0-vps.md), [self-hosted-postgres-backups](../docs/adr/self-hosted-postgres-backups.md)):

- One OVH VPS runs Dokploy, Traefik and the `skoleoverblikket` compose app (`keycloak`, `api`, `web`).
- Postgres runs as two Dokploy Database resources (app DB and Keycloak DB). Backups are a daily Dokploy `pg_dump` to OVH Object Storage, so RPO is 24h.
- CI's `migrate` job runs `psql "$DATABASE_URL"` against prod from GitHub runners, which means Postgres must be reachable from the internet.
- Uploaded files live in OVH Object Storage, not on the VPS. They don't move.
- The DPA lists OVH with location **EU (Frankrig)** ([dataProcessing.ts](../web/src/content/dataProcessing.ts)).

The VPS is tight on RAM, and [task 53](53-restore-drill.md) already wants WAL archiving (Phase 4) and migrations out of CI (Phase 5). The first plan was a move to a bigger VPS, doing both on an empty server. **Changed 2026-10-31:** an unrelated Dokploy stack on the same VPS uses 2–3 GB RAM. Removing it gives enough room for now, so we stay on the current box and move to a bigger one when we scale ([Later](#later-bigger-vps)). The cost: this is surgery on the live box instead of a clean build, and we don't get the full-rebuild proof (53 Phase 3 stays open).

Constraints ([vendor rules](../docs/adr/ai-data-boundary.md)): data stays in the EU, no personal data through GitHub. Same VPS, same vendor and location, so the DPA and sub-processor list don't change.

## Decisions (proposed)

- ~~**D1: Fresh Dokploy install, not a Dokploy restore.**~~ Dropped 2026-10-31: no new VPS, the existing Dokploy instance stays.
- **D2: One Postgres cluster, two databases.** `skoleoverblikket` and `keycloak` live in the same cluster with separate roles. One pgBackRest stanza and one WAL stream means a restore brings app and Keycloak back to **the same point in time**. That removes the "user exists in Keycloak but not the app DB" reconcile step from the 53 runbook.
- **D3: pgBackRest instead of WAL-G** (supersedes the WAL-G suggestion in 53 Phase 4). It supports several repos natively (OVH now, Scaleway as `repo2` for 53 Phase 2), and has built-in AES-256 encryption and `verify` and `check` commands.
- **D3a: pgBackRest runs in a separate `backup-agent` container, and WAL is streamed, not archived** ([task 60](60-backup-console.md) D1–D2). Postgres has no `archive_command`. With one, a broken pgBackRest makes archiving fail, `pg_wal` fills the disk and prod stops. Instead the agent runs `pg_receivewal` against a replication slot over a shared socket volume, with `max_slot_wal_keep_size = 4GB`. If the agent is down too long, Postgres drops the slot and keeps running: a WAL gap and an alert, not an outage. Task 60 Phase 0 is a spike that proves this before §1 is built, with a capped spool-copy `archive_command` as the fallback.
- **D4: Postgres has no published port.** It is only on the compose network. Migrations run in a one-shot `migrate` service (53 Phase 5, done here). Important: Docker-published ports bypass `ufw`, so the only safe port is no port.
- **D5: Same Postgres major as prod, or upgrade during the move.** The cutover uses `pg_dump`/`pg_restore`, which can move to a newer major. pgBackRest physical restores can't. If we want Postgres 17 → 18, this move is the cheap moment to do it.
- **D6: Stay with OVH in France.** Then the DPA and sub-processor list don't change and no 30-day notice is needed. Moving to another country or vendor would require the notice ([task 48](completed/48-databehandleraftale.md)). Staying on the current VPS keeps this automatically; it applies again to the [later](#later-bigger-vps) move.

## Remember when switching Postgres on the live box

This is the "easy to forget" list. Most items cost an outage if missed.

**Inventory first** (into the password manager, not the repo):

- [ ] Every env var of the compose app. Copy the whole env block before editing it. It is the only copy of several secrets (Stripe keys and webhook secret, Keycloak admin client secret, object storage keys, presigned-upload signing key, SMTP, elmah.io, OTEL).
- [ ] Dokploy Database resources: Postgres version (`SELECT version();`), DB names, users, passwords, extensions (`\dx` in both DBs).
- [ ] Dokploy backup schedules and S3 destinations for the old DBs, so you know what to turn off.

**Data and state:**

- [ ] **Never run two `api`s against real data.** The `api` runs background jobs (`SchoolRetentionJob`, `AbsenceRetentionJob`, notification emails). Two at once means double emails, or both deleting data. The cutover stops the app before the dump and starts it once against the new DB.
- [ ] Keycloak keeps its realm signing keys and sessions in its DB. Restoring the Keycloak DB keeps existing logins and tokens valid. A fresh realm import would log everyone out and change the admin client secret. `--import-realm` skips a realm that already exists, so it's safe to leave on.
- [ ] Stripe webhooks that fail during the window are retried automatically for up to 3 days. After cutover, check *Developers → Webhooks* for failed deliveries and resend if needed.
- [ ] Files in Object Storage don't move.
- [ ] **Named volumes are the database now.** Deleting the compose app in Dokploy with "delete volumes" ticked, or `docker compose down -v`, wipes prod. Pin the volume names (`pgdata-a`, `pgdata-b`, `pg-control`) and write this in the runbook. `/pg-control/active` says which one is live, and the other is the restore target (task 60), so never "clean up" the spare one without checking.
- [ ] RAM during the cutover: the Dokploy DBs and the compose Postgres run side by side until the old DBs are stopped. Check `free -h` before restoring.

**Security:**

- [ ] `ufw` allows 22, 80, 443 only. Since Docker bypasses `ufw` for published ports, also use **OVH's Edge Network Firewall** (in front of the VPS) to allow only 22/80/443. That holds even if someone adds a `ports:` line by mistake. Do this once nothing needs the Dokploy DBs' public port (after the cutover, with §2 `migrate` live).
- [ ] Check while you're there: SSH key only, `PermitRootLogin no`, password auth off, unattended-upgrades on, 2FA on the Dokploy admin user, Dokploy dashboard behind HTTPS with port 3000 closed.
- [ ] Rotate every DB password (the new cluster gets new roles anyway). `DATABASE_URL` has lived in GitHub secrets, so treat it as leaked.

**Decommission the old DBs:**

- [ ] The Dokploy Database resources hold personal data. After the 7-day fallback window: delete them and their volumes. Log the date.
- [ ] Old Dokploy `pg_dump` backups in the bucket expire through their retention (14 days, 53 Phase 1). Turn off the old backup schedules at the cutover so nothing writes half-finished dumps.

## Scope

### 1. Postgres image and pgBackRest config

**Do [task 60](60-backup-console.md) Phase 0 (the spike) first.** It decides between streamed WAL (below) and the spool-copy fallback. The fallback only covers WAL delivery. If the read-only base backup fails, this task waits until Phase 0 passes or task 60 defines a separate base-backup fix.

> **Phase 0 done 2026-10-07: streamed WAL passed, no fallback.** Results in [postgres-backup-agent](../docs/adr/postgres-backup-agent.md). The Postgres image, config, roles and both compose services are built (`infrastructure/postgres/`, `infrastructure/backup-agent/`, `selfhosted-db` profile in the prod compose). Two changes to the plan below: pgBackRest runs with `archive-check=n` (it refuses archive checks without `archive_mode`), so `archive-copy=y` is not possible; and the agent mounts the data volumes at the same paths as Postgres (`/pgdata/a`, `/pgdata/b`), because pgBackRest refuses a `pg1-path` that differs from `data_directory`.

New folder `infrastructure/postgres/` (Postgres only, no backup tooling):

- [ ] `Dockerfile`: `FROM postgres:<major>` + config and init script. No pgBackRest: it lives in the `backup-agent` image (task 60).
- [ ] `postgresql.conf` overrides: `wal_level = replica`, `max_wal_senders = 5`, `max_replication_slots = 5`, `max_slot_wal_keep_size = 4GB`, **no `archive_command`**, memory settings sized for what's free on the VPS after the unrelated stack is gone (`shared_buffers` ≈ 25% of the RAM given to Postgres). Socket dir `/var/run/postgresql` on a named volume `pg-socket` shared with the agent.
- [ ] `pg_hba.conf`: `local replication backup_agent` and `local all backup_agent` (socket only, no TCP).
- [ ] `pgbackrest.conf` (in the agent image, task 60): `repo1-type=s3` on a **new, separate** OVH bucket (not the files bucket, not the old dump bucket, not the ops bucket), `repo1-cipher-type=aes-256-cbc`, `repo1-retention-full-type=time`, `repo1-retention-full=13` (see the retention note), ~~`archive-copy=y`~~ not possible with streamed WAL (see the Phase 0 note; the agent checks each backup's WAL reached the repo instead), `start-fast=y`, `compress-type=zst`, `pg1-path` passed per call by the agent (the live one of `/pgdata/a`, `/pgdata/b`), `pg1-socket-path=/var/run/postgresql`. S3 keys and cipher pass come from env vars (`PGBACKREST_REPO1_S3_KEY` etc.), never from the file.
- [ ] Init script (`/docker-entrypoint-initdb.d/`): create roles `skoleoverblikket` (app), `keycloak`, `restore_drill` (only `CONNECT`, for 53) and `backup_agent` (`REPLICATION`, plus what pgBackRest needs for backups), both databases, and the physical replication slot `agent`.
- [ ] CI: build and push `ghcr.io/nielspilgaard/skoleoverblikket-postgres` like the other `publish-*` jobs. Pin it to a tag in compose. Don't use `latest` for the database.
- Retention note: full backups **must** run daily (§3), and no restorable data may be older than 14 days, which is the DPA limit and what the 53 drill enforces. Time retention keeps the newest full that is at least N days old, so N=14 would keep data up to ~15 days. N=13 with daily fulls keeps the oldest full between 13 and 14 days old, **but only while fulls succeed**: expiry runs after a successful backup, and time retention never expires the newest full. If fulls keep failing, the data kept just gets older. Control: the 53 heartbeat goes `Unhealthy` when the newest full is > 26h old, and a daily check fails when the oldest backup in `pgbackrest info` is > 14 days old. If that happens and a new full can't be taken, the DPA limit wins: expire the stale backups by hand (`pgbackrest expire --set=<label>`, or `stanza-delete` if it is the only one) even though that **leaves no restorable backup** until the next full succeeds. Log it. Weekly fulls would keep up to 20 days and break the DPA promise.
- [ ] **Lost slot (WAL gap).** When the agent falls more than `max_slot_wal_keep_size` behind (agent down, S3 down), Postgres invalidates the slot (`pg_replication_slots.wal_status = 'lost'`) and removes the WAL. Postgres keeps running, but the WAL chain now has a gap. PITR can't cross that gap until a new full backup is taken, so the 15-minute RPO no longer holds from the last pushed WAL until that backup finishes.
  - Alert: the task 60 WAL heartbeat goes `Unhealthy` when the newest WAL in the repo is > 15 min old or the slot is `lost`. Also alert when the slot holds 2 GB (`pg_wal_lsn_diff` on `restart_lsn`), before anything is lost.
  - Recovery (goes in `docs/RESTORE.md`, and the console guides it): fix the cause (S3 key, bucket, network, agent), recreate the slot, restart `pg_receivewal`, run `pgbackrest backup --type=full` right away. Log the gap window (last WAL before the gap → end of the new backup). Restores into that window can only go to the last backup before it.

### 2. Compose changes

[docker-compose.prod.yml](../infrastructure/docker/docker-compose.prod.yml):

- [ ] `postgres` service: the custom image, data volumes `pgdata-a` and `pgdata-b` at `/pgdata/a` and `/pgdata/b` plus `pg-control` (the entrypoint starts on the one `/pg-control/active` names, so a restore can go into the spare one, task 60 D5), `pg-socket` volume, healthcheck `pg_isready`, **no `ports:`**, memory limit.
- [ ] `backup-agent` service (task 60): own network (not `dokploy-network`), `ports: ["127.0.0.1:9090:9090"]`, `pg-socket`, both data volumes and `pg-control` at the same paths as Postgres, memory limit so a drill can't starve Postgres. With streamed WAL, also a named volume `wal-receive` mounted at the `pg_receivewal -D` directory: it is the only copy of received WAL until `archive-push` succeeds, so it must survive recreating the agent (task 60 D2a). Pin its name like the `pgdata-*` volumes and never delete it while it holds segments.
- [ ] `migrate` service (53 Phase 5): API image, runs `Skoleoverblikket.Api migrate` (small branch in `Program.cs`: `Database.MigrateAsync()` and exit), `restart: "no"`, `depends_on: postgres (service_healthy)` so it never runs against a database that isn't up yet.
- [ ] `api`: `depends_on: postgres (service_healthy), migrate (service_completed_successfully)`. `keycloak`: `depends_on: postgres (service_healthy)`.
- [ ] Connection strings point at `postgres:5432`. Remove the "Do not add a postgres service here" comment and update the header's env var list.
- [ ] Confirm the Dokploy compose type is **Docker Compose**, not Stack. Swarm ignores `depends_on` conditions and one-shot services, and the `deploy:` blocks only mean anything in Stack mode. Drop or keep them deliberately.
- [ ] Staging compose: use the same `postgres` image, plus the `migrate` service, so e2e tests the migration before prod sees it. Whether staging gets its own agent and ops bucket is open in task 60.
- [ ] `ci.yml`: remove the `migrate` job and `publish-api`'s `needs: migrate`. Delete the `DATABASE_URL` GitHub secret.
- [ ] Coordinate with [task 44](44-auto-rollback.md): its rollback check reads `__EFMigrationsHistory` through `DATABASE_URL` from GitHub. Switch it to the image-label approach in 53 Phase 5, whichever task lands second.

### 3. Backup schedules (in the agent)

- [ ] After first start, from the agent: `pgbackrest --stanza=main stanza-create`, then `pgbackrest check` (proves WAL reaches the repo end to end).
- [ ] The agent's own scheduler runs the daily 02:00 full backup and the weekly `verify` ([task 60](60-backup-console.md) Phase A). No Dokploy schedules for backups.
- [ ] Failures go to elmah.io heartbeats (53 D3).
- Pre-migration backup (53 Phase 1 §2) is no longer needed: with continuous WAL, restore to the second before `migrate` started. The `migrate` service logs its start time, which becomes the `--target` time.

### 4. Free up the VPS

- [ ] Remove the unrelated Dokploy stack (and its volumes, once you're sure nothing in it is needed). Measure RAM and disk after.
- [ ] Size the memory limits from that (step 5 above). Write the numbers in the PR.

### 5. Rehearsal (on the VPS, before cutover)

With the profile on and prod still on the Dokploy DBs (steps 5–9 above):

- [ ] Take a fresh dump of both prod DBs and restore it into the compose Postgres (step 6.3 below). Time every step. Check RAM while it runs.
- [ ] Don't start a second `keycloak` or `api` against it: no room, and a second `api` would run jobs. Check row counts instead (step 6.4).
- [ ] Run `pgbackrest backup`, then do a **point-in-time restore** of it into the spare volume from the console, to prove the new backups work before they're the only ones.
- [ ] Wipe the rehearsal: drop the restored databases (or recreate the volumes so the init script runs again), `stanza-delete`, empty the pgBackRest bucket. It holds a copy of prod that must not stick around until the real cutover.
- [ ] Fix the runbook with whatever went wrong. Note the measured downtime.

### 6. Cutover runbook

Weekend evening, outside school hours, not at the time of a scheduled job. Tell admins the day before (email or banner): "Skoleoverblikket er utilgængeligt lørdag kl. 21–22 pga. vedligeholdelse."

1. Copy the current Dokploy env block to the password manager.
2. Stop `api` and `keycloak` in Dokploy (Stop, not Delete). Turn off the old DB backup schedules. Now nothing writes; the Dokploy DBs keep running for the dump.
3. Final dump: `pg_dump -Fc` for both DBs, on the VPS (never through a laptop or GitHub). Restore into the compose Postgres with `pg_restore --exit-on-error --no-owner --role=<new role>`.
4. Check: row counts per table match (`pg_stat_user_tables` on both sides, after `ANALYZE`), latest `__EFMigrationsHistory` row matches, Keycloak realm and user count match.
5. Switch `DATABASE_URL` and `KEYCLOAK_DB_URL` to host `postgres` and redeploy. Run the `GRANT` from [RESTORE.md](../docs/RESTORE.md) §13 once, or the backup agent can't read the deleted-schools ledger and migration times (`pg_restore` doesn't carry it).
6. Stop the Dokploy Database resources (Stop, not Delete), so nothing can write to them by mistake.
7. Smoke test via the real hostnames: login, schema, file download, a Stripe webhook test event.
8. `pgbackrest backup --type=full` right away, so there is a backup from minute one.
9. Run CD with the current tag via `workflow_dispatch` to prove deploys still work with the new env.
10. Check Stripe for failed webhook deliveries during the window. Check elmah.io for errors.

**Rollback:** until schools have written data, start the Dokploy DBs, point `DATABASE_URL` and `KEYCLOAK_DB_URL` back at them and redeploy. After that, rollback means losing data, so fix forward instead. The Dokploy DBs stay stopped, not deleted, for 7 days.

### 7. Docs

- [ ] New ADR that supersedes the backup part of [self-hosted-postgres-backups](../docs/adr/self-hosted-postgres-backups.md): Postgres in compose, one cluster, pgBackRest, 15-min RPO, 14-day retention. Update [INDEX.md](../docs/adr/INDEX.md).
- [ ] [DEPLOYMENT.md](../docs/DEPLOYMENT.md): new env vars (pgBackRest S3 keys, cipher pass, Postgres role passwords), and remove `DATABASE_URL` as a GitHub secret.
- [ ] Re-check [task 53](53-restore-drill.md) against what was built. It already assumes the drill runs in the `backup-agent` ([task 60](60-backup-console.md)) and restores pgBackRest backups, that Phases 4 and 5 are done here, and that Phase 2 is `repo2` (Scaleway) for the DB with rclone only for files. Its runbook loses the Keycloak/app reconcile step.
- [ ] `docs/RESTORE.md` (53): add the inventory list above as "where everything lives".

## Testing

No tUnit or Playwright tests: this is infrastructure ([TESTING.md](../docs/TESTING.md)). Prove it by hand:

- [ ] `pgbackrest check` passes, and `pgbackrest info` shows WAL archived within the last 5 minutes.
- [ ] Point-in-time restore during the rehearsal lands within 5 minutes of the target time.
- [ ] **Slot loss**: stop the agent long enough for WAL to pass `max_slot_wal_keep_size`. Postgres keeps running, the slot goes `lost`, and the alert fires. Then confirm PITR can't cross the gap (a restore target inside it fails or stops short), follow the recovery procedure, and confirm PITR works again only from the new full backup onwards. Until then the 15-minute RPO is exceeded.
- [ ] **Archive-push failure**: set a wrong S3 key on a scratch stanza with the agent running. `archive-push` fails, unpushed WAL in `wal-receive` grows (the slot keeps advancing, so this is not slot loss), and the WAL heartbeat alerts before the 4 GB cap. Fix the key and confirm the held segments are pushed with no gap.
- [ ] A deliberately failing migration on staging: `migrate` exits non-zero and `api` doesn't start.
- [ ] From outside: `nc -zv <new-ip> 5432` and `nc -zv <new-ip> 3000` fail.
- [ ] After cutover: one CD deploy goes all the way through.

## Done when

- The unrelated stack is off the VPS. Prod runs on the compose Postgres, the old Dokploy DBs are deleted and the date is logged.
- Both DBs are in the compose `postgres` service, WAL archives continuously, and a PITR restore has been done once.
- CI no longer has `DATABASE_URL`, and 5432 is closed from outside.
- Measured downtime is written down, and the ADR, DEPLOYMENT.md and task 53 are updated.

## Open questions

- **Postgres major**: stay on the current prod major or move to 18 during the move? Check what the app's Npgsql/EF Core version supports.
- **Keep a nightly logical dump** next to pgBackRest? It doesn't depend on the WAL chain, but it's a second backup system to watch. Leaning no, because `archive-copy=y` full backups restore on their own.

## Later: bigger VPS

When we scale, move to a bigger OVH VPS in France (D6) or ask OVH to upgrade this one in place. After this task that move is simpler: Dokploy app plus compose volumes, restored through the backup console ([RESTORE.md](../docs/RESTORE.md)). Doing it from backups also counts as the 53 Phase 3 full-rebuild drill. What to recreate on a new box (inventory it into the password manager first):

- [ ] Compose app settings: source type (Git provider + path, or raw), compose type (**Docker Compose vs. Stack**), domains, auto-deploy toggles, the whole env block.
- [ ] Registry credentials (GHCR pull, if the images aren't public), Git provider / GitHub App link, SSH keys, notification channels, Dokploy's own self-backup.
- [ ] Custom Traefik config: anything under `/etc/dokploy/traefik/` that isn't generated by Dokploy, e.g. rules blocking the Keycloak master realm.
- [ ] Host-level things outside Dokploy: `ufw` and OVH Edge Network Firewall rules, `/etc/skoleoverblikket/*` env files (53), crontab, unattended-upgrades, swap, timezone, SSH hardening, 2FA on Dokploy.
- [ ] GitHub `production` secrets: `DOKPLOY_URL`, `DOKPLOY_API_KEY` (new instance = new key), `DOKPLOY_COMPOSE_ID` (new compose = new ID). Without these, CD deploys to the old box or fails.
- [ ] DNS records pointing at the VPS IP (`skoleoverblikket.dk`, `auth.skoleoverblikket.dk`, the Dokploy dashboard). Lower TTL to 300s a day ahead, and copy `acme.json` (`chmod 600`) to avoid cert errors.
- [ ] Never two live `api`s: stop the old stack before the new one starts against real data. Wipe the old VPS after 7 days (OVH wipes the disk on delete) and log the date.

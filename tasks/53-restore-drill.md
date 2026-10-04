---
title: 'Backup restore drill and data safety net'
purpose: 'Prove every week, automatically, that we can get every school''s data back, and make the backups survive the failures that actually happen.'
description: >-
  Weekly automated restore drill on the VPS: download the newest Dokploy
  pg_dump of both databases, restore it into a throwaway Postgres, check it
  against live prod, report to an elmah.io heartbeat. Adds a daily
  backup-freshness check, a backup before every prod migration, retention
  aligned to the 14 days the DPA promises, a restore runbook, an encrypted
  off-site copy (DB and uploaded files), a quarterly manual full-rebuild drill,
  15-minute RPO via WAL archiving once we have 5 schools, and migrations moved
  into docker compose so prod Postgres is no longer reachable from the internet.
status: 'Proposed'
---

# Backup restore drill and data safety net

## TL;DR

A backup nobody has restored is a hope, not a backup. Phase 1: a small `restore-drill` container (built in CI, run by a Dokploy schedule **on the VPS**, never in GitHub Actions) restores the newest dump of the app DB and the Keycloak DB into a tmpfs Postgres every Sunday night, checks the result and reports to an **elmah.io heartbeat**. Elmah.io alerts on an unhealthy result and on a missing one. A `--check-only` mode runs daily to catch a stopped backup within 24h. CI takes a backup before it applies migrations. Retention set to 14 days to match the DPA. A written restore runbook. Phase 2: encrypted off-site copy of DB dumps **and uploaded files** at Scaleway (EU, 75 GB free), write-protected with Object Lock; the drill restores from there. Phase 3: quarterly manual drill that rebuilds prod from nothing and times it. Phase 4 (trigger: 5 paying schools): RPO from 24h down to 15 min with continuous WAL archiving. Phase 5: migrations run as a one-shot service in docker compose, CI's `migrate` job goes away and the Postgres port closes to the internet.

## Context

Today ([self-hosted-postgres-backups](../docs/adr/self-hosted-postgres-backups.md), [phase-0-vps](completed/phase-0-vps.md)):

- Postgres runs as two Dokploy Database resources on the OVH VPS: the app DB and the Keycloak DB (all logins live there).
- Dokploy's built-in backup: daily `pg_dump`, gzip, upload to an OVH Object Storage bucket.
- The ADR says "retention 30 backups" and "monthly restore drill". The DPA page says backups are kept **14 days** (`BACKUP_RETENTION_DAYS` in [dataProcessing.ts](../web/src/content/dataProcessing.ts)) and that deleted schools disappear from backups 14 days after deletion. If Dokploy keeps 30, we break a promise we've made to customers.
- No restore drill has run. Nothing alerts if backups stop.
- Uploaded files (ugeplan attachments, filarkiv, bestyrelse files, avatars) are in OVH Object Storage with **no backup**. An app bug, a bad `SchoolDeletionService` call or a leaked key deletes them for good.
- DB backups, files and the server are all at OVH. The 2021 OVH Strasbourg fire destroyed servers and backups stored in the same site. One vendor or account problem takes everything.
- CI's `migrate` job applies `migration_script.sql` to prod via `psql` **before** deploy, with no backup taken first. A bad migration is the most likely way we lose data, more likely than hardware failure.
- For that `psql` call to work, prod Postgres must accept connections from GitHub runners, i.e. from the internet. A leaked `DATABASE_URL` or a Postgres vulnerability exposes every school directly.
- The prod VPS's Dokploy env vars are the only copy of several secrets. If the VPS is gone, so are they.

Constraints (vendor and cost rules, [ai-data-boundary](../docs/adr/ai-data-boundary.md)): personal data stays in the EU and **never passes through GitHub** (public repo, public logs, US runners). No extra VPS. New infra < $5/month.

## Decisions (proposed)

- **D1 — Drill runs on the VPS**, as a Dokploy scheduled job. Restoring children's fravær data on a GitHub runner would move personal data to the US and into public logs. GitHub only builds the drill image and triggers the pre-migration backup through the Dokploy API. No personal data goes through GitHub.
- **D2 — Weekly automated + quarterly manual.** Weekly proves the dump restores and is complete. Quarterly proves we can rebuild everything with the VPS gone and measures how long it takes. Replaces the ADR's monthly drill.
- **D3 — Fail loud via elmah.io Heartbeats.** Elmah.io (Danish, already our error log) has Heartbeats: the job POSTs `Healthy` or `Unhealthy` with a reason, and elmah.io alerts on `Unhealthy` **and** when no heartbeat arrives within the interval (dead-man's switch). A drill that silently stops running also alerts. No new vendor, no SMTP code in the drill. The `reason` is a PII-free summary (check name, counts), never row content. Heartbeats are included in our elmah.io plan.
- **D4 — Retention 14 days** in Dokploy, matching the DPA. The drill **fails** if a backup is older than 15 days, because then we keep data longer than we've told customers.
- **D5 — Off-site copy is encrypted client-side** (rclone `crypt`) and the bucket uses Object Lock, so neither Scaleway nor someone who takes over the VPS can read or delete it. The crypt password lives in the password manager. The drill decrypts every week, so we find out right away if the key is lost.
- **D6 — Targets.** Now: RPO 24h (daily dumps, plus one before every migration). From 5 paying schools: **RPO 15 min** (Phase 4). RTO 4h for a full rebuild. Phase 3 measures the real RTO.
- **D7 — Migrations run inside docker compose, not from CI** (Phase 5). A one-shot `migrate` service on `dokploy-network` applies migrations before `api` starts. Postgres then only needs to be reachable on the Docker network, and its public port closes.
- **Non-goal: per-school point-in-time restore.** Even with WAL archiving (Phase 4), restoring one school to an earlier time is done in a side DB and copied back by hand (runbook).

## Scope

### Phase 1 — Prove the backups work

#### 1. Fix the backup config (Dokploy dashboard, by hand)

- [ ] Retention 14 on both DB backup schedules (app + Keycloak). Confirm the Keycloak DB is actually backed up.
- [ ] Schedule at 02:00 Europe/Copenhagen, both DBs.
- [ ] Enable Dokploy notifications for failed backups → email. A cheap first alarm before the drill exists.
- [ ] Enable Dokploy's own self-backup (Dokploy config, compose definitions, env vars) to the same bucket.
- [ ] Create an **OVH read-only S3 user** for the backup bucket. The drill never gets Dokploy's write key.
- [ ] Record the prod Postgres major version (`SELECT version();`). The drill image must use the same major or newer.

#### 2. Pre-migration backup (CI)

- [ ] In `ci.yml` `migrate`, before `psql`: call the Dokploy API `backup.manualBackupPostgres` for the app DB backup and wait until a new object shows up in the bucket (list by key prefix, compare timestamps; time out after 10 min → fail the job, don't migrate).
- [ ] Skip the call if `migration_script.sql` applies no new migrations (compare against `__EFMigrationsHistory`), so normal deploys stay fast. Only a timestamp and object key come back to GitHub, no data.
- [ ] Use a **separate Dokploy backup config** for the app DB with prefix `pre-migration/`, no regular schedule, keep latest 14. Dokploy's retention counts backups, so manual dumps in the daily config would push out daily ones.
- [ ] Dokploy API key and backup IDs as `production` environment secrets (`DOKPLOY_PRE_MIGRATION_BACKUP_ID`). Reuse `DOKPLOY_URL` / `DOKPLOY_API_KEY`.
- This is temporary: Phase 5 moves the trigger into `deploy.mjs` once migrations leave CI.

#### 3. Drill image

New folder `infrastructure/restore-drill/`:

- `Dockerfile`: `FROM postgres:<prod major>` + `rclone` (S3 access, later crypt) + `curl` (heartbeat). One image, no other runtime.
- `drill.sh` (bash, `set -euo pipefail`), two modes:
  - `--check-only` (daily, ~seconds): list both DBs' backups → assert newest < 26h old, oldest ≤ 15 days, count ≥ 10. No download.
  - default (weekly): the checks above, then per DB:
    1. Download the newest dump to tmpfs. Check gzip integrity (`gzip -t`).
    2. Start Postgres inside the container on tmpfs (`PGDATA` on `--tmpfs`). Restore with `pg_restore --exit-on-error` (or `psql -v ON_ERROR_STOP=1` if Dokploy writes plain SQL; confirm the format on the first run).
    3. **App DB checks:**
       - Every table in live prod exists in the restore (allow tables from migrations applied after the backup time).
       - Latest `__EFMigrationsHistory` row equals live's, or live is newer only by migrations applied after the dump.
       - Per table: if live has rows, the restore has rows; restored count ≥ 50% of live `pg_stat_user_tables.n_live_tup`. Catches empty or partial dumps.
       - `Tenants` count matches live within ±2 (signups/deletions since the dump).
       - Tenant isolation is intact: no row in a tenant-scoped table has a `TenantId` missing from `Tenants`. One generated query per table with a `TenantId` column.
    4. **Keycloak DB checks:** realm `Skoleoverblikket` exists in `realm`, `user_entity` count within 5% of live, `credential` table non-empty.
    5. Print a **PII-free summary** (table names, counts, dump size, timings, migration id). No row content, ever.
    6. Container exits, tmpfs is gone. **No restored copy survives the drill.** Otherwise a deleted school would live on in a drill artifact.
  - Live comparison uses a dedicated `restore_drill` login with only `CONNECT` (row counts come from `pg_stat_user_tables`, which needs no table privileges). It never reads live rows.
  - Result → elmah.io heartbeat: `POST https://api.elmah.io/v3/heartbeats/{logId}/{heartbeatId}` with `result: Healthy | Unhealthy`, `reason` (failing check + PII-free summary) and `took` (ms). Daily check, weekly drill and Phase 2 off-site copy each get their own heartbeat, with intervals 1 day, 7 days and 1 day plus a grace period. A script crash (`trap ERR`) still sends `Unhealthy`.
  - Heartbeat API key: a separate elmah.io key with only *Heartbeats – Write*, not the app's logging key.
- CI: build and push `ghcr.io/nielspilgaard/skoleoverblikket-restore-drill` on push to `main` when `infrastructure/restore-drill/**` changes. Same pattern as the other `publish-*` jobs.

#### 4. Schedule it (Dokploy, by hand)

- [ ] Env file `/etc/skoleoverblikket/restore-drill.env` (root, `0600`): read-only S3 key, `restore_drill` connection strings (app + Keycloak), elmah.io heartbeat key, log ID and heartbeat IDs.
- [ ] Create the heartbeats in elmah.io and turn on email alerts for them.
- [ ] Dokploy server schedule, Sunday 04:00: `docker pull … && docker run --rm --tmpfs /drill:size=4g --memory=2g --env-file … ghcr.io/…-restore-drill:latest`.
- [ ] Dokploy server schedule, daily 07:00: same with `--check-only`.
- [ ] Check VPS headroom during the first run (RAM, CPU, how long it takes). If the restore puts load on prod, lower Postgres `shared_buffers` in the container or move the drill to 03:00.

#### 5. Restore runbook — `docs/RESTORE.md`

For a real incident, written for 3 a.m. Steps:

1. Decide scope: whole DB, Keycloak, files, or one school's mistake.
2. Maintenance: scale the `api` service to 0 in Dokploy so nobody writes to a DB that's about to be replaced.
3. Restore into a **new** database (never on top of the broken one; keep it for forensics), via Dokploy's restore button or `pg_restore`. Point `DATABASE_URL` at it. Re-run `migration_script.sql` (it is idempotent) if the dump predates the latest migration. After Phase 5, the `migrate` service does this when the compose starts.
4. Reconcile everything that happened after the dump:
   - **School deletions**: a school deleted after the dump comes back. Run `SchoolRetentionJob` right away and check the school is gone again. If a deletion can't be re-derived from Stripe state, delete it by hand from the backoffice log. (Check on the first drill that this works; see open questions.)
   - **Stripe**: resend webhook events since the dump time (`stripe events resend`, or the dashboard). Events are kept 30 days.
   - **Keycloak vs app DB**: users who signed up after the dump exist in only one of them. List them and fix by hand.
   - **Files** uploaded after the dump are orphans in the bucket. Harmless; list them and remove later.
5. Smoke test (smoke tenant from [task 44](44-auto-rollback.md) if it exists), scale `api` back up.
6. **GDPR**: lost data is a personal data breach (availability). Assess within 72h whether Datatilsynet must be notified. As data processor we notify affected schools without undue delay (DPA). Add an email template for this to the runbook.
7. Targeted restore for one school (e.g. secretary deleted a class): restore to a side DB, export the affected rows with `psql \copy` filtered on `TenantId`, insert by hand. Never restore over prod for one school.

Also in the runbook: where every secret lives (password manager entry names), how to recreate the Dokploy env from them, and the Postgres major version.

#### 6. Docs

- [ ] Update [self-hosted-postgres-backups](../docs/adr/self-hosted-postgres-backups.md): retention 14 days, weekly automated + quarterly manual drill, pre-migration backup, off-site copy (Phase 2). Add an update note in the status line and an `IMP-` list. Rewrite NEG-002 (the process dependency is now enforced by alerts).
- [ ] Add `docs/RESTORE.md` to the documentation map in `AGENTS.md`.

### Phase 2 — Survive losing OVH

- [ ] Scaleway Object Storage bucket (fr-par), **versioning + Object Lock** with a default retention of `BACKUP_RETENTION_DAYS − 1` days (compliance mode). Lifecycle: expire current objects under `db/` after 14 days, noncurrent versions after 14 days. That fits the 14-day DPA promise for deleted data.
- [ ] Scaleway API key scoped to that bucket only, with a bucket policy that denies permanent version deletion. Even if someone takes over the VPS, they can't wipe the copy.
- [ ] Nightly Dokploy server schedule (03:00), same drill image, `--offsite` mode:
  - `rclone copy` new DB dumps (both DBs) from OVH → `scaleway-crypt:db/`.
  - `rclone sync` the uploaded-files bucket → `scaleway-crypt:files/`. Sync deletes are only delete markers, and the locked versions survive 14 days.
  - Reports to its own elmah.io heartbeat.
- [ ] Weekly drill restores from **Scaleway (decrypting)** instead of OVH, and checks the newest OVH dump has the same timestamp. This tests the worst-case path every week.
- [ ] Weekly drill also restores 20 random files from `files/` and compares size and hash with OVH (`rclone check --download` on a sample).
- [ ] crypt password + salt in the password manager. Printed copy in a safe place. Without it the off-site copy is useless.
- [ ] **Sub-processor change**: Scaleway goes from "email" to "email + encrypted backups". Send the 30-day notice from the backoffice ([task 48](completed/48-databehandleraftale.md)), update `dataProcessing.ts` purpose text and the DPA security section ("backups are encrypted and stored with a second EU provider"). **The notice goes out 30 days before the first upload.**
- [ ] Cost check: estimate DB dumps × 14 + files total against Scaleway's 75 GB free tier. Alert at 60 GB.

### Phase 3 — Quarterly full-rebuild drill (manual, ~2h)

Pretend the VPS is gone. Use only the runbook, the password manager and the off-site copy:

- [ ] Restore both DBs from Scaleway into temporary Dokploy Database resources on the VPS.
- [ ] Start a temporary compose (`api` + `keycloak`, prod image tags) on an internal hostname, pointed at them.
- [ ] Log in as the smoke user, open schema, ugeplan, a file. Use the smoke tenant only, **never** log in as a real school user.
- [ ] Time every step. Record RTO, problems and runbook fixes in a log table at the bottom of `docs/RESTORE.md` (date, duration, what broke).
- [ ] Tear down: delete the temp resources and volumes.
- [ ] Recurring reminder: calendar event every 3 months.

### Phase 4 — 15-minute RPO (trigger: 5 paying schools)

Daily dumps are fine while we're small. With 5+ schools, losing a day of fravær, beskeder and ugeplaner is too much. Start the work when school 4 signs up, so it's live by school 5.

- [ ] **Decide first, with real numbers**: WAL-G on our own Postgres, or OVH managed Postgres (PITR built in, ~€44/month). With 5 schools paying, €44 may be cheaper than owning WAL archiving. Record the decision as a new ADR that supersedes the backup part of [self-hosted-postgres-backups](../docs/adr/self-hosted-postgres-backups.md), which rules out custom backup tooling.
- If self-hosted (WAL-G):
  - [ ] Add `wal-g` to the custom Postgres image we already run, and set `archive_mode = on` and `archive_command = 'wal-g wal-push %p'` in its config. Prod Postgres already runs a custom image, so no change to how Dokploy runs it.
  - [ ] Continuous WAL archiving with `archive_timeout = 300` (a WAL segment at least every 5 min, so 15 min holds with margin). Nightly base backup. Both encrypted by WAL-G (libsodium) and sent to OVH **and** the Scaleway bucket from Phase 2.
  - [ ] Retention: base backups and WAL older than 14 days get deleted (`wal-g delete retain FIND_FULL …`), so the DPA promise still holds.
  - [ ] Keep Dokploy's daily `pg_dump` too. Logical dumps don't depend on the WAL chain, so a broken WAL chain doesn't leave us with nothing.
  - [ ] **Disk risk**: if archiving fails, WAL piles up on the VPS until Postgres stops. Alert on `pg_stat_archiver.failed_count` increasing and on disk usage > 80%.
- [ ] New `--check-wal` mode, every 15 min: `pg_stat_archiver.last_archived_time` < 15 min old, no new failures. Own elmah.io heartbeat, interval 30 min.
- [ ] Weekly drill adds a **point-in-time restore**: newest base backup + WAL up to "now − 30 min", and checks that the restored DB reached that time (`pg_last_xact_replay_timestamp()` within 15 min of the target).
- [ ] Update the DPA page text from "dagligt" to "løbende (højst 15 minutters datatab)", and RPO in the runbook.

### Phase 5 — Migrations in docker compose, close the Postgres port

Today CI runs `psql` against prod from GitHub runners. That keeps the DB open to the internet, puts `DATABASE_URL` in GitHub secrets, and migrates prod **before** staging e2e has passed.

- [ ] One-shot `migrate` service in [docker-compose.prod.yml](../infrastructure/docker/docker-compose.prod.yml) (and staging): same API image, runs `Skoleoverblikket.Api migrate` (a small branch in `Program.cs` that calls `Database.MigrateAsync()` and exits; EF Core takes a migration lock). `restart: "no"`, on `dokploy-network`.
- [ ] `api` gets `depends_on: migrate: condition: service_completed_successfully`. If the migration fails, the new API doesn't start. Dokploy's compose deploys honor this condition.
- [ ] Staging runs the same `migrate` service, so e2e now tests the migration itself before prod sees it.
- [ ] **Pre-migration backup moves to `deploy.mjs`**: CI adds an image label `org.skoleoverblikket.latest-migration=<MigrationId>`. `deploy.mjs` compares the label on the new image with the one on the running image. If they differ: trigger the `pre-migration/` Dokploy backup (Phase 1 §2), wait for it, then redeploy. GitHub never touches the DB.
- [ ] Coordinate with [task 44](44-auto-rollback.md): its rollback check reads prod `__EFMigrationsHistory` through `DATABASE_URL` from GitHub. Switch it to the same image-label comparison. Whichever task lands second adapts.
- [ ] Remove the `migrate` job from `ci.yml` and the `DATABASE_URL` GitHub secret. **Rotate the prod DB password** afterwards, since it has lived in GitHub secrets.
- [ ] Close the port: remove the external port on both Dokploy Database resources (app + Keycloak) and deny 5432 in the VPS firewall. Check from outside: `nc -zv <vps-ip> 5432` must fail.
- [ ] Update the runbook: migrations happen when the compose starts.

## Testing

A drill that has never failed hasn't been tested. Before calling Phase 1 done, break each check on purpose and confirm the alert arrives:

- [ ] Point the drill at a truncated dump (upload `head -c 1M` of a real one under a test prefix) → restore fails → `Unhealthy` heartbeat → elmah.io email.
- [ ] Point the drill at a backup prefix whose newest object is 3 days old → `--check-only` fails → `Unhealthy` with that reason.
- [ ] Set an expectation that can't hold (e.g. `Tenants` ±0 with a fake live count) → the reason names that check.
- [ ] Kill the container mid-restore → `trap` still sends `Unhealthy`.
- [ ] Stop the weekly schedule for 8 days (or set the heartbeat interval short) → elmah.io's missing-heartbeat alert fires.
- [ ] `ci.yml` pre-migration backup: run with a test migration on staging first. Then confirm on the first real migration that a new dump appears before `psql` runs.
- [ ] Phase 2: delete a test file in the source bucket → still restorable from Scaleway for 14 days. Try to delete a locked version with the VPS key → refused.
- [ ] Phase 4: block the archive destination (wrong key) → `--check-wal` goes `Unhealthy` within 30 min. PITR restore lands within 15 min of the target time.
- [ ] Phase 5: a deliberately failing migration on staging → `migrate` exits non-zero, new `api` doesn't start, old one keeps serving. `nc -zv <vps-ip> 5432` from outside fails.

No tUnit or Playwright tests: this is infrastructure, not app behavior ([TESTING.md](../docs/TESTING.md)).

## Done when

- Two green weekly drills in a row, plus every failure case above alerted once.
- Retention is 14 days on both DBs and the drill enforces it.
- `docs/RESTORE.md` exists and someone other than the author could follow it.
- ADR updated.
- Phases 2–5 ship as separate PRs. Phase 2 waits for the 30-day sub-processor notice. Phase 4 starts when school 4 signs up. Phase 5 can go any time after Phase 1.

## Open questions

- **School deletion after restore**: does `SchoolRetentionJob` re-delete a resurrected school by itself (cancellation date comes from the restored DB, so probably yes)? Confirm on the first drill by reading the job, and document the answer in the runbook.

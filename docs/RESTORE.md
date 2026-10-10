---
title: 'Restore runbook'
description: >-
  What to do when data is lost or the database is broken, written for 3 a.m.
  Step 1 is always the backup console over an SSH tunnel. Covers the restore
  wizard, going live by swapping the A/B data volumes, the post-restore
  checklist, WAL gaps, S3 outages, a lost VPS and the DPA's 14-day limit.
status: 'Living'
purpose: 'The incident runbook for the database and its backups (tasks 53, 54, 60).'
---

# Restore runbook

## TL;DR

1. Open the backup console: `pwsh scripts/backup-console.ps1 <user>@<vps>`.
2. **Restore** → pick a point → type the spare volume's name → wait for the checks.
3. Type the name again and click **Go live**, then **Redeploy** the compose app in Dokploy.
4. Work through the checklist the console shows, including the GDPR assessment within 72 hours.

Never delete a `skoleoverblikket-pgdata-*`, `skoleoverblikket-pg-control` or `skoleoverblikket-wal-receive` volume, and never run `docker compose down -v`. They are the database.

How it works: [postgres-backup-agent](adr/postgres-backup-agent.md). Environment: [DEPLOYMENT.md](DEPLOYMENT.md).

> Until the task [54](../tasks/54-move-vps.md) cutover, prod Postgres is still a Dokploy Database resource with daily `pg_dump` backups. Restore those with Dokploy's restore button into a **new** database, never over the broken one.

## 1. Open the console (step 1 of every incident)

The console runs in the `backup-agent` container. It doesn't need Keycloak, the API or the app database, so it works when they're down.

```bash
pwsh scripts/backup-console.ps1 <user>@<vps>
```

It reads the access link from the agent over SSH, opens the tunnel and opens the link in your browser. Keep the window open; Ctrl+C closes the tunnel. Set `BACKUP_CONSOLE_SERVER=<user>@<vps>` once and run it with no arguments. If 9090 is taken (the local dev stack), add `-LocalPort 9091`.

Without the script:

```bash
ssh -L 9090:127.0.0.1:9090 <user>@<vps>
# on the VPS, in that session:
docker exec $(docker ps -qf label=com.docker.compose.service=backup-agent) cat /var/lib/backup-agent/console-link
```

The link (`http://localhost:9090/access?key=…`) sets a cookie for 12 hours. The key lives in the agent's volume, so only someone with a shell on the VPS can read it.

| Page | Use it for |
|---|---|
| Overview | "Data secured X min ago", newest backup, oldest restore point, slot, disk, heartbeats |
| Backups | What can be restored (per timeline), every backup, WAL gaps, "Back up now", "Verify now" |
| Drills | Drill history and RTO trend, "Drill now", log the quarterly manual drill |
| Deleted schools | Schools deleted in the last 14 days that a restore would bring back |
| Restore | The restore wizard, the go-live steps, the post-restore checklist, deleting the old volume |
| History | Every run, action and event (also in the ops bucket under `history/`) |

**The console doesn't load:** the agent is down. `docker ps -a | grep backup-agent`, `docker logs <container>`, then redeploy the compose app in Dokploy. Postgres keeps running without the agent; WAL waits in the replication slot for up to 4 GB.

## 2. Decide the scope

- **The whole database is wrong** (bad migration, broken disk, mass deletion): §3–§5. App and Keycloak come back to the same point in time.
- **One school deleted something:** §7. Never roll every school back for one school's mistake.
- **Uploaded files:** not covered by the agent. The off-site copy is task [53](../tasks/53-restore-drill.md) Phase 2.

## 3. Restore with the wizard

**Restore** in the console:

1. Pick the point: a time (Copenhagen time), "Just before a migration" (stops just before the transaction that applied it), or "Full backup as-is". Times outside the restorable ranges are refused; a WAL gap splits the ranges.
2. Leave "Re-delete schools deleted after this point" ticked. It backdates the deletion warning of schools deleted after the point, on the restored copy, so `SchoolRetentionJob` deletes them again on its first pass instead of emailing them a new warning.
3. Type the spare volume's name and start. The wizard wipes the spare volume, runs `pgbackrest restore` into it, starts a temporary Postgres on it inside the agent, runs the drill checks and stops it again. The live volume is never touched: the agent asks Postgres which directory it runs on and only writes to the other one.
4. Read the summary: the checks, rows per table against live, latest migration, Keycloak users, the time recovery actually reached, and the resurrected schools.

If live Postgres is down, the comparisons with live are skipped and marked "—". That's expected in a real incident.

## 4. Go live (swap the A/B volumes)

1. Under the summary, type the spare volume's name and click **Go live**. If a check failed, read it first: tick the box to go live anyway (in a real incident live may be worse). The agent writes the spare's letter to `/pg-control/active`, and the header shows "pgdata-b live on redeploy".
2. In Dokploy, **Redeploy** the compose app. No environment variables to change, nothing to scale down: Redeploy recreates every container. Postgres starts on the restored volume (a new timeline), and the API and Keycloak restart with it, so no stale caches.
3. The agent notices the new live volume, creates a new replication slot, takes a full backup at once and shows the checklist (§5).

Changed your mind before redeploying: **Cancel** on the banner. To undo after going live: **Switch back** under "Old volume" on the Restore page, then Redeploy again. The old volume was never written to.

If Postgres won't start after the redeploy and its log says `select-volume: /pgdata/b is empty`, the marker names a volume without a database. Switch back (or Cancel) and redeploy.

## 5. After go-live

The console's **Restore** page shows the checklist; each tick goes to history:

- **Stripe:** resend webhook events since the restore point (Dashboard → Developers → Webhooks, or `stripe events resend`). Events are kept 30 days.
- **Resurrected schools:** check they're gone again a few minutes after the API started (Deleted schools, API logs from `SchoolRetentionJob`).
- **Smoke test** with the smoke tenant: login, schema, ugeplan, a file. Never log in as a real school user.
- **elmah.io:** no new errors.
- **GDPR:** §6.
- **Old volume:** it keeps the pre-restore database for forensics. Delete it with "Delete old volume" once you're done, at the latest after 14 days (DPA). The console warns after 7.

## 6. GDPR

Lost data is a personal data breach (availability). Assess within 72 hours whether Datatilsynet must be notified. As data processor we notify the affected schools without undue delay (DPA). Write down what was lost (the window between the restore point and the incident) and which schools wrote data in it.

## 7. One school's mistake

Restore to the spare volume with the wizard (§3) but **don't go live**. Then read that school's rows from the spare (the console names it; `pgdata-b` here) with a throwaway Postgres that has no network, on the VPS:

```bash
docker run --rm -d --name restore-read --network none \
  -v skoleoverblikket-pgdata-b:/var/lib/postgresql/data postgres:17
docker exec -it restore-read psql -U postgres -d skoleoverblikket
# \copy (SELECT * FROM "Classes" WHERE "TenantId" = '<school id>') TO '/tmp/classes.csv' CSV HEADER
docker stop restore-read
```

Insert the rows into live by hand. The files stay on the VPS; never copy them to a laptop or GitHub. A per-school export from a point in time is task 60 Phase D.

## 8. WAL gap (lost slot)

If the agent falls more than 4 GB behind (agent down for long, S3 down past the agent's own 4 GB buffer), Postgres drops the slot and keeps running. The console goes red: "WAL gap". A point-in-time restore can't cross the gap.

The agent recovers by itself: new slot, streaming again, full backup requested. If that backup can't run, fix the cause and click **Back up now** (full). The gap closes with that backup. Restores into the gap can only go to the last backup before it.

## 9. The repo or S3 is down

`archive-push` fails and "Not pushed yet" grows on the overview. Up to 4 GB waits on the agent's `wal-receive` volume, then the agent pauses and Postgres holds up to 4 GB more in the slot, then §8. Fix the endpoint or keys in Dokploy (`PGBACKREST_REPO1_*`) and redeploy the agent. Everything waiting is pushed, oldest first, with no gap.

## 10. The VPS is gone

The agent image restores without anything from the old box. On a new VPS with Docker:

```bash
docker run -d --name backup-agent -p 127.0.0.1:9090:9090 \
  --label com.docker.compose.service=backup-agent \
  -v skoleoverblikket-pgdata-a:/pgdata/a -v skoleoverblikket-pgdata-b:/pgdata/b \
  -v skoleoverblikket-pg-control:/pg-control \
  --env-file agent.env \
  ghcr.io/nielspilgaard/skoleoverblikket-backup-agent:<tag>
```

`agent.env` holds the `PGBACKREST_REPO1_*` values (endpoint, bucket, keys, cipher pass) and the `OpsBucket__*` keys from the password manager. Open the console (§1); it shows Postgres as down, which is right, and treats `pgdata-a` as live, so the wizard restores into `pgdata-b`. Restore, click Go live (it writes `b` to the marker), stop this container, then install Dokploy and deploy the compose app with `COMPOSE_PROFILES=selfhosted-db` (task 54 §4 lists everything else to recreate). Postgres starts on `pgdata-b`. Without the cipher pass the backups are unreadable.

## 11. A backup is older than 14 days

The console flags it red (DPA). Normally `expire` after each daily full keeps the oldest at 13–14 days. If fulls keep failing, the oldest data just gets older. Fix the backup first. If no new full can be taken, the DPA wins: expire the stale backups by hand and log it in the console's history, knowing that leaves nothing to restore until the next full succeeds.

```bash
docker exec $(docker ps -qf label=com.docker.compose.service=backup-agent) pgbackrest --stanza=main expire --set=<label>
```

## 12. The agent says the live database has another system-id

The live cluster isn't the one the repo knows, e.g. it was re-initialized empty. Stop and don't delete anything: check which volume `/pg-control/active` names (`docker exec <postgres container> cat /pg-control/active`) and that it's the one you meant. If a new cluster is intended, point the agent at a new repo path (`PGBACKREST_REPO1_PATH`), remove `/var/lib/backup-agent/state.json` in the agent and redeploy it. The old path keeps the old backups until you delete them (within 14 days, DPA).

## 13. After a `pg_restore` into a new cluster

The agent reads two app tables, granted by a migration. A database loaded with `pg_restore` (the task 54 cutover) doesn't get that grant. Run once in the app database:

```sql
GRANT SELECT ON "SchoolDeletionRecords", "__EFMigrationsHistory" TO backup_agent;
```

## 14. Where things live

| Thing | Where |
|---|---|
| Live and spare database | Docker volumes `skoleoverblikket-pgdata-a` / `-b`; `/pg-control/active` (volume `skoleoverblikket-pg-control`) says which one Postgres starts on |
| WAL not yet in the repo | Docker volume `skoleoverblikket-wal-receive` |
| Backups (encrypted) | pgBackRest repo bucket at OVH (`PGBACKREST_REPO1_S3_BUCKET`) |
| Status, history, deleted-schools ledger | Ops bucket `skoleoverblikket-ops` (`status.json`, `history/`, `ledger.json`, `migrations.json`) |
| Console access key, agent state | Docker volume `skoleoverblikket-backup-agent-state` |
| Secrets (S3 keys, cipher pass, role passwords) | Dokploy env and the password manager |
| Measured restore times | Console → Drills (weekly drills and the quarterly manual drill) |

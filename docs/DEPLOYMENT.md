---
title: 'Deployment'
description: >-
  Every secret and environment variable the API needs to start in production,
  which have hardcoded defaults that need overriding, and how to generate
  required secrets.
status: 'Living'
purpose: Checklist for standing up or reconfiguring a production deployment.
---

# Production deployment — environment variables

This document lists every secret and environment variable that must be set before the API can run in production. The API reads configuration via ASP.NET Core's standard provider chain: `appsettings.json` → `appsettings.Production.json` → **environment variables** (highest priority). Use environment variables for all secrets; never commit secret values to source control.

ASP.NET Core maps environment variables to the config key hierarchy using `__` (double underscore) as the section separator. Examples below use that convention.

---

## Required — API will not start without these

| Environment variable                   | Config key                            | Description                                                                                                                |
| -------------------------------------- | ------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | PostgreSQL connection string. Example: `Host=db;Database=skoleoverblikket;Username=app;Password=…`                              |
| `Keycloak__AdminClientId`              | `Keycloak:AdminClientId`              | Client ID of the Keycloak service-account used for admin API calls (e.g. `skoleoverblikket-admin`).                             |
| `Keycloak__AdminClientSecret`          | `Keycloak:AdminClientSecret`          | Secret for the admin Keycloak client. Generate in the Keycloak console → Clients → Credentials.                            |
| `ObjectStorage__AccessKey`             | `ObjectStorage:AccessKey`             | OVHCloud Object Storage access key (S3-compatible).                                                                        |
| `ObjectStorage__SecretKey`             | `ObjectStorage:SecretKey`             | OVHCloud Object Storage secret key.                                                                                        |
| `PresignedUpload__SigningKey`          | `PresignedUpload:SigningKey`          | Random secret used to HMAC-sign upload confirm tokens. **Minimum 32 characters.** Generate with: `openssl rand -base64 32` |
| `Stripe__SecretKey`                    | `Stripe:SecretKey`                    | Stripe live secret key (`sk_live_…`).                                                                                      |
| `Stripe__WebhookSecret`                | `Stripe:WebhookSecret`                | Stripe webhook signing secret (`whsec_…`). Set after creating the webhook endpoint in the Stripe dashboard.                |

---

## Required — hardcoded defaults in appsettings.json that must be overridden in production

These have non-empty values in `appsettings.json` that are correct for production _unless_ your deployment differs.

| Environment variable               | Default value                                    | When to override                                                   |
| ---------------------------------- | ------------------------------------------------ | ------------------------------------------------------------------ |
| `Keycloak__Authority`              | `https://auth.skoleoverblikket.dk/realms/Skoleoverblikket` | Only if you use a different Keycloak hostname.                     |
| `ObjectStorage__ServiceUrl`        | `https://s3.rbx.io.cloud.ovh.net/`               | Only if you switch S3 regions or providers.                        |
| `ObjectStorage__DefaultBucketName` | `skoleoverblikket`                                    | Only if you use a different bucket name.                           |
| `ObjectStorage__PublicEndpoint`    | `https://s3.rbx.io.cloud.ovh.net`                | Only if the public download base URL differs from the service URL. |
| `Smtp__Host`                       | `smtp.tem.scaleway.com`                          | Only if you switch transactional email providers.                  |

---

## Optional

| Environment variable        | Default                     | Description                                                                                 |
| --------------------------- | --------------------------- | ------------------------------------------------------------------------------------------- |
| `Stripe__PriceId`           | _(set in appsettings)_      | Stripe price ID for the monthly subscription. Override to switch plans without redeploying. |
| `App__BaseUrl`              | `https://skoleoverblikket.dk`    | Used to construct absolute URLs in emails and webhooks.                                     |
| `Keycloak__MetadataAddress` | _(empty — auto-discovered)_ | Override only if your Keycloak OIDC discovery endpoint is at a non-standard path.           |
| `ElmahIo__ApiKey`           | _(empty — disabled)_        | elmah.io API key. Error logging to elmah.io activates only when both this and `ElmahIo__LogId` are set. In `docker-compose.prod.yml`, set the host-side variable `ElmahIo_ApiKey` (single underscore) — Compose maps it to the container config key `ElmahIo__ApiKey`. |
| `ElmahIo__LogId`            | _(empty — disabled)_        | elmah.io log ID (GUID). Only errors (uncaught request exceptions and `LogLevel.Error`+) are sent. In `docker-compose.prod.yml`, set the host-side variable `ElmahIo_LogId` (single underscore) — Compose maps it to the container config key `ElmahIo__LogId`. |
| `BackupStatus__AccessKey`   | _(empty — card says "ikke sat op")_ | Key for the backup agent's ops bucket, used by the backoffice backup card. Must only be able to **read** `skoleoverblikket-ops`, never write or delete. In `docker-compose.prod.yml`: `BackupStatus_AccessKey`. |
| `BackupStatus__SecretKey`   | _(empty)_                   | Secret for the key above. In `docker-compose.prod.yml`: `BackupStatus_SecretKey`. `BackupStatus__ServiceUrl` and `BackupStatus__BucketName` default to OVH RBX and `skoleoverblikket-ops`. |

---

## Self-hosted Postgres and the backup agent

From the task [54](../tasks/54-move-vps.md) cutover, Postgres and the backup agent run in `docker-compose.prod.yml` behind the `selfhosted-db` profile ([postgres-backup-agent](adr/postgres-backup-agent.md)). Until then these variables are unused. Runbook: [RESTORE.md](RESTORE.md).

| Variable (Dokploy env) | Description |
| --- | --- |
| `COMPOSE_PROFILES` | `selfhosted-db` starts `postgres` and `backup-agent`. Leave unset until the cutover. |
| `POSTGRES_IMAGE`, `BACKUP_AGENT_IMAGE` | Pinned `sha-…` tags of `skoleoverblikket-postgres` and `skoleoverblikket-backup-agent`. The defaults don't exist, so forgetting them fails loudly instead of pulling `latest`. Not changed by `deploy.mjs`, so a normal deploy never restarts the database. |
| `POSTGRES_PASSWORD` | Superuser password. Only used by the init script and by hand. |
| `APP_DB_PASSWORD`, `KEYCLOAK_DB_PASSWORD` | Passwords of the `skoleoverblikket` and `keycloak` roles, created on first start. `DATABASE_URL` and `KEYCLOAK_DB_PASSWORD` for Keycloak use the same values, host `postgres`. |
| `PG_VOLUME`, `PG_SPARE_VOLUME` | Live data volume (default `pgdata-a`) and restore target (default `pgdata-b`). Going live with a restore = swap both and redeploy. |
| `PG_SHARED_BUFFERS`, `PG_EFFECTIVE_CACHE_SIZE`, `POSTGRES_MEMORY` | Sized for the VPS (about 25% of Postgres' memory for shared buffers). |
| `BACKUP_AGENT_MEMORY`, `DRILL_TMPFS_SIZE` | The drill restores into tmpfs, which counts against the agent's memory limit. Size from the first drill. |
| `PGBACKREST_REPO1_S3_ENDPOINT`, `PGBACKREST_REPO1_S3_REGION`, `PGBACKREST_REPO1_S3_BUCKET` | The pgBackRest repo: its own OVH bucket, not the files bucket and not the ops bucket. |
| `PGBACKREST_REPO1_S3_KEY`, `PGBACKREST_REPO1_S3_KEY_SECRET` | Read-write key for the repo bucket only. |
| `PGBACKREST_REPO1_CIPHER_PASS` | Encrypts every backup and WAL file. Keep it in the password manager: without it the backups are unreadable. Generate with `openssl rand -base64 48`. |
| `OpsBucket_ServiceUrl`, `OpsBucket_AccessKey`, `OpsBucket_SecretKey` | The agent's **read-write** key for `skoleoverblikket-ops` (status, history, deleted-schools ledger). Give the API a separate read-only key (`BackupStatus_*` above). Lifecycle rule: delete `history/` after 400 days. |
| `Heartbeats_ApiKey`, `Heartbeats_LogId`, `Heartbeats_WalId`, `Heartbeats_BackupId`, `Heartbeats_DrillId`, `Heartbeats_VerifyId` | elmah.io heartbeats. A key with only *Heartbeats – Write*, not the app's logging key. The WAL heartbeat carries the agent's overall health every 5 minutes (interval 30 min in elmah.io), backup daily, drill and verify weekly. |
| `BACKUP_CONSOLE_SSH` | Shown in the console and the backoffice card, e.g. `ssh -L 9090:127.0.0.1:9090 ubuntu@<vps>`. |

---

## Frontend (Vite build-time)

The frontend is a static SPA. These variables must be set **at build time** (they are baked into the JS bundle).

| Variable            | Description                                                                                  |
| ------------------- | -------------------------------------------------------------------------------------------- |
| `VITE_KEYCLOAK_URL` | Keycloak base URL — no trailing slash, no realm path. Example: `https://auth.skoleoverblikket.dk` |

Set in the CI/CD pipeline before running `npm run build`. See [web/.env.example](../web/.env.example).

---

## Generating secrets

```bash
# PresignedUpload__SigningKey — 32 random bytes as base64
openssl rand -base64 32

# Or with PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

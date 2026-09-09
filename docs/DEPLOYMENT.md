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

---
title: 'ADR: Self-hosted PostgreSQL with Dokploy-managed backups'
status: 'Accepted'
date: '2026-04-02'
authors: 'Niels Pilgaard Grøndahl'
tags: ['infrastructure', 'database']
supersedes: ''
superseded_by: 'postgres-backup-agent (backup part)'
description: >-
  PostgreSQL runs self-hosted on the OVH VPS rather than managed DBaaS, to
  avoid the ~€44/month managed-Postgres cost pre-revenue; backups use
  Dokploy's built-in pg_dump-based backup feature rather than custom scripts.
---

# ADR: Self-hosted PostgreSQL with Dokploy-managed backups

## TL;DR

PostgreSQL is self-hosted as a Docker Compose service on the OVH VPS — not OVH's managed DBaaS. Backups run via Dokploy's built-in database backup feature (`pg_dump` under the hood, cron-scheduled, S3-compatible destination), not custom scripts.

## Status

**Accepted** (updated 2026-04-02 — backup mechanism changed from a custom cron/script approach to Dokploy's built-in backup feature; decision to self-host rather than use managed DBaaS is unchanged)

**Backup part superseded 2026-10-07** by [postgres-backup-agent](postgres-backup-agent.md): Postgres moves into compose and a backup agent streams WAL to pgBackRest (15-minute RPO, 14-day retention, weekly drill). The Dokploy `pg_dump` backups below stay in use until the task 54 cutover. Self-hosting is unchanged.

## Context

OVHCloud managed PostgreSQL starts at approximately €44/month — too expensive for an early-stage, pre-revenue product. Self-hosting on the same VPS that already runs the API eliminates this cost, at the price of owning backup/restore operations directly.

## Decision

PostgreSQL runs self-hosted in Docker Compose on the OVH VPS. Dokploy's built-in database backup feature handles scheduled backups — no custom cron scripts.

### Backup strategy

- Configure Dokploy database backup via the dashboard Backups tab.
- Schedule: daily via cron expression.
- Destination: OVHCloud Object Storage bucket (S3-compatible, EU region).
- Retention: 30 backups.
- Monthly restore drill to verify backup integrity.
- Monitor backup status via Dokploy dashboard notifications.

## Consequences

### Positive

- **POS-001**: Eliminates the ~€44/month managed-DBaaS cost pre-revenue.
- **POS-002**: Dokploy's built-in backup feature removes the need to write and maintain custom backup scripts.
- **POS-003**: Backups land in the same OVHCloud Object Storage bucket family already used for file storage — see [file-storage-approach](file-storage-approach.md) — keeping vendor count low.

### Negative

- **NEG-001**: The developer owns backup/restore operational risk directly (verification, restore drills, monitoring) that a managed DBaaS would otherwise absorb.
- **NEG-002**: Without the monthly restore drill actually happening, backup existence does not guarantee backup usability — this is a process dependency, not something the tooling enforces.

## Alternatives Considered

### OVHCloud managed PostgreSQL (DBaaS)

- **ALT-001**: **Description**: Fully managed PostgreSQL instance from OVHCloud.
- **ALT-002**: **Rejection Reason**: ~€44/month is too expensive for an early-stage product with no revenue yet.

### Custom cron-based backup scripts

- **ALT-003**: **Description**: Hand-written `pg_dump` cron jobs, the original approach before this ADR's 2026-04-02 update.
- **ALT-004**: **Rejection Reason**: superseded by Dokploy's built-in backup feature, which provides the same `pg_dump`-based mechanism with scheduling, retention, and S3 destination configuration out of the box — no reason to maintain custom scripts once the platform feature existed.

## Related Decisions

- [postgres-backup-agent](postgres-backup-agent.md) — supersedes the backup part of this ADR

- [tech-stack](tech-stack.md) — the self-hosted PostgreSQL choice this ADR elaborates on
- [file-storage-approach](file-storage-approach.md) — shares the same OVHCloud Object Storage destination for backups

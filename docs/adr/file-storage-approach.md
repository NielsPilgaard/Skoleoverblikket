---
title: 'ADR: File storage via OVHCloud Object Storage'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['infrastructure', 'storage']
supersedes: ''
superseded_by: ''
description: >-
  School-uploaded files are stored in OVHCloud Object Storage (S3-compatible,
  EU), already used for database backups, with per-tier storage quotas as a
  cost-aligned upgrade lever.
---

# ADR: File storage via OVHCloud Object Storage

## TL;DR

Files uploaded by schools (course materials, documents, PDFs, avatars) are stored in OVHCloud Object Storage via the S3-compatible API. Storage quota is the upgrade lever, tied to actual infrastructure cost — see [docs/PRICING.md](../PRICING.md) for current quota numbers, which may have changed since this ADR was written.

## Status

**Accepted**

## Context

OVHCloud Object Storage was already in the stack for database backups (see [self-hosted-postgres-backups](self-hosted-postgres-backups.md)), is EU-hosted, and is cost-effective. Adding a second storage vendor for file uploads would add operational overhead without benefit.

## Decision

Files are stored in OVHCloud Object Storage (S3-compatible API), enabling standard tooling (AWS SDK for .NET). Files are linked to courses and browsable via a file explorer in the app. Storage quota per pricing tier is the differentiation lever.

## Consequences

### Positive

- **POS-001**: One object storage vendor for both backups and file uploads — lower operational overhead, one set of credentials to manage.
- **POS-002**: S3-compatible API means standard .NET tooling (AWS SDK) works without a custom client.
- **POS-003**: Storage quota as an upgrade lever ties pricing differentiation directly to actual cost, consistent with [school-based-pricing](school-based-pricing.md).

### Negative

- **NEG-001**: Ties the platform to OVHCloud's S3-compatible API surface and its specific reliability/latency characteristics.

## Alternatives Considered

### Separate storage vendor for files vs. backups

- **ALT-001**: **Description**: Use a different object storage provider for user-uploaded files than for database backups.
- **ALT-002**: **Rejection Reason**: adds a second vendor and credential set for no meaningful benefit — OVHCloud Object Storage already met the requirements (EU-hosted, S3-compatible, cost-effective).

## Related Decisions

- [self-hosted-postgres-backups](self-hosted-postgres-backups.md) — shares the same OVHCloud Object Storage destination
- [postgres-backup-agent](postgres-backup-agent.md) — the pgBackRest repo and the ops bucket are separate OVH buckets next to the files bucket
- [school-based-pricing](school-based-pricing.md) — storage quota is one of the cost-based tier differentiators this decision references; see [docs/PRICING.md](../PRICING.md) for current numbers rather than duplicating them here

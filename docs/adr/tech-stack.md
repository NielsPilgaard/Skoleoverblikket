---
title: 'ADR: Tech stack'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['architecture', 'tech-stack']
supersedes: ''
superseded_by: ''
description: >-
  Selects ASP.NET Core + EF Core + PostgreSQL + React/TypeScript + Keycloak +
  OVHCloud, driven by the developer's existing C# expertise and EU data residency.
---

# ADR: Tech stack

## TL;DR

ASP.NET Core API (.NET 10) + EF Core + self-hosted PostgreSQL. React + Vite + TypeScript + Tailwind frontend. Keycloak for auth. OVHCloud VPS + Object Storage for hosting, all EU-based. Chosen for developer expertise (C#) and low vendor count.

## Status

**Accepted**

## Context

A v1 needed to ship fast with a single developer who has deep C# expertise but limited time to learn a new stack. Data residency had to stay EU-only for GDPR reasons given the target market (Danish schools, personal data on students/staff).

## Decision

| Layer | Choice |
|---|---|
| API | ASP.NET Core Web API, C# 13 / .NET 10 |
| ORM | Entity Framework Core |
| Database | PostgreSQL (self-hosted in Docker Compose) |
| Frontend | React + Vite + TypeScript + Tailwind CSS |
| Auth / SSO | Keycloak (Docker Compose service) |
| Object storage | OVHCloud Object Storage (S3-compatible, EU) |
| Local S3 emulation | LocalStack (v3, no license required) |
| Local orchestration | .NET Aspire (v13) |
| Hosting | OVHCloud VPS + Dokploy + Docker Compose |

## Consequences

### Positive

- **POS-001**: Developer's existing C#/ASP.NET Core/EF Core expertise reduces v1 risk and dev time.
- **POS-002**: Keycloak provides battle-tested multi-tenant OIDC/JWT auth and leaves the door open for future UniLogin integration.
- **POS-003**: OVHCloud co-locates VPS and object storage, minimizing vendor count and latency, and keeps all data in the EU.
- **POS-004**: .NET Aspire replaces a hand-maintained docker-compose.yml for local dev — one `aspire run` command orchestrates PostgreSQL, pgAdmin, Keycloak, and LocalStack with a dashboard, health checks, and OpenTelemetry.

### Negative

- **NEG-001**: Self-hosted PostgreSQL and Keycloak mean the developer owns operational burden (backups, upgrades, security patching) that a managed service would absorb — see [self-hosted-postgres-backups](self-hosted-postgres-backups.md).

## Alternatives Considered

Not formally evaluated against competitors at the time — the choice followed directly from existing developer expertise and the EU-residency constraint. See [monorepo-openapi](monorepo-openapi.md) for the related decision on how the API and frontend share types.

## Related Decisions

- [monorepo-openapi](monorepo-openapi.md) — how API and frontend stay in sync given this stack
- [self-hosted-postgres-backups](self-hosted-postgres-backups.md) — operational consequence of self-hosting PostgreSQL
- [transactional-email](transactional-email.md) — provider choice within the same EU-residency constraint
- [ai-data-boundary](ai-data-boundary.md) — extends the EU-residency constraint to AI processing

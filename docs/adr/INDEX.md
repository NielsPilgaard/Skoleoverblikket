---
title: 'ADR Index'
description: >-
  Concept-to-ADR lookup for all product and architecture decisions. Every concept
  is owned by exactly one ADR — check here before assuming a design choice is
  arbitrary or before writing a new decision that might duplicate an existing one.
status: 'Living'
---

# ADR Index

| Concept | ADR |
|---|---|
| Tech stack (API, ORM, DB, frontend, auth, hosting) | [tech-stack](tech-stack.md) |
| Monorepo layout, OpenAPI type sharing, typed client generation | [monorepo-openapi](monorepo-openapi.md) |
| Tenant routing (`/{slug}`), slug rules, slug immutability | [path-based-tenant-routing](path-based-tenant-routing.md) |
| Laptop-first admin UI, phone-friendly staff views | [responsive-ui](responsive-ui.md) |
| PostgreSQL hosting and backup strategy | [self-hosted-postgres-backups](self-hosted-postgres-backups.md) |
| Transactional email provider (Scaleway TEM) | [transactional-email](transactional-email.md) |
| Pricing model — flat fee per school, not per student | [school-based-pricing](school-based-pricing.md) |
| Billing flow — Stripe Checkout, self-serve, no manual invoicing | [stripe-checkout-billing](stripe-checkout-billing.md) |
| Trial — 30 days, full access, no free tier | [free-trial](free-trial.md) |
| File storage — OVHCloud Object Storage, quotas | [file-storage-approach](file-storage-approach.md) |
| Schema conflict detection — real-time, clock-time overlap | [schema-conflict-detection](schema-conflict-detection.md) |
| Printable schema views — per class/teacher/room | [printable-schema](printable-schema.md) |
| Time slot inheritance and per-class overrides | [time-slot-inheritance](time-slot-inheritance.md) |
| AI data boundary — personal data only to EU AI; GitHub/Claude get PII-free specs only (PII guard planned, not yet enforced — specs stay in the EU zone until the dispatch gate exists); AI never merges or migrates | [ai-data-boundary](ai-data-boundary.md) |

See [docs/PRD.md](../PRD.md) for the product requirements these decisions serve, and [AGENTS.md](../../AGENTS.md) for the documentation map.

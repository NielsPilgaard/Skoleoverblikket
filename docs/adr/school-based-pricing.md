---
title: 'ADR: School-based pricing, not per-student'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['product', 'billing']
supersedes: ''
superseded_by: ''
description: >-
  Pricing is a flat monthly fee per school, differentiated by what actually
  costs more to provide (storage, support, modules) rather than by student
  count, because schools want a predictable fixed budget line.
---

# ADR: School-based pricing, not per-student

## TL;DR

Flat monthly fee per school, not per student. Tiers/add-ons are differentiated by what costs the platform more to provide — storage quota, support level, optional modules — never by school size. See [docs/PRICING.md](../PRICING.md) for current tier and pricing details.

## Status

**Accepted**

## Context

Danish schools' student counts fluctuate term to term and they want a fixed, predictable budget line for admin software rather than a number that moves with enrollment.

## Decision

Pricing is a flat monthly fee per school. Differentiation (storage, support, optional feature modules) is based on platform cost to serve, not student count.

## Consequences

### Positive

- **POS-001**: Predictable budgeting for schools — the bill doesn't move if enrollment changes mid-year.
- **POS-002**: Simpler to reason about and market — no per-seat calculator needed at signup.
- **POS-003**: Aligns pricing axis with actual infrastructure cost drivers (storage, support) rather than an unrelated proxy (student count).

### Negative

- **NEG-001**: A large school pays the same as a small one for the base tier, even though it likely uses more support time in practice — cost-to-serve is only approximated by the differentiators chosen, not measured directly.

## Alternatives Considered

### Per-student pricing

- **ALT-001**: **Description**: Charge a per-student rate, common in some competitor products.
- **ALT-002**: **Rejection Reason**: unpredictable for schools whose enrollment fluctuates; schools consistently dislike this model per market feedback in [docs/PRD.md](../PRD.md) competitive context.

## Related Decisions

- [stripe-checkout-billing](stripe-checkout-billing.md) — how this pricing model is actually billed
- [free-trial](free-trial.md) — the trial mechanism ahead of paid conversion

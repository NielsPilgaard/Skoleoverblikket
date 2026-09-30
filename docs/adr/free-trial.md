---
title: 'ADR: 30-day free trial replaces free tier'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['billing', 'product']
supersedes: ''
superseded_by: ''
description: >-
  New schools get 30 days of full access with no credit card required and no
  permanently free tier — a full trial gives a truer evaluation than a
  feature-gated free plan.
---

# ADR: 30-day free trial replaces free tier

## TL;DR

New schools get a 30-day free trial with full Basis access, no credit card required to start, no permanently free tier.

## Status

**Accepted**

Amended 2026-09-30: trial extended from 14 to 30 days, so schools can evaluate across a full month of real use (including a school holiday) without contacting support.

## Context

A permanently free tier with limited features adds product complexity — tier gating, upgrade prompts, feature flags — that runs against the product's simplicity-first principle (see [docs/VISION.md](../VISION.md)), and gives a worse first impression than the real product.

## Decision

New schools get a 30-day free trial with full Basis access. No credit card required to start. There is no permanently free tier.

## Consequences

### Positive

- **POS-001**: Schools evaluate the real product, not a feature-gated subset — a truer trial experience.
- **POS-002**: No tier-gating logic to build or maintain in the app.

### Negative

- **NEG-001**: Schools that need longer than 30 days to evaluate (e.g. spanning a school holiday) must contact support for an extension — no self-serve trial-extension mechanism exists.

## Alternatives Considered

### Permanently free limited tier

- **ALT-001**: **Description**: A free tier with a feature subset, alongside paid tiers.
- **ALT-002**: **Rejection Reason**: schools that would only ever use the free tier were never going to convert to paid regardless of trial length; the feature-gating complexity isn't worth it for that segment.

## Related Decisions

- [stripe-checkout-billing](stripe-checkout-billing.md) — how the trial converts to a paid subscription

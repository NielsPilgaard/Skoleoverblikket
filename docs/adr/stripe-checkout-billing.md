---
title: 'ADR: Stripe Checkout for self-serve billing'
status: 'Accepted'
date: '2025-01-01'
authors: 'Niels Pilgaard Grøndahl'
tags: ['billing', 'stripe']
supersedes: ''
superseded_by: ''
description: >-
  All billing runs through Stripe Checkout and the Stripe billing portal —
  self-serve, monthly or yearly auto-renew, no manual invoicing, no MobilePay.
---

# ADR: Stripe Checkout for self-serve billing

## TL;DR

Schools sign up and pay via Stripe Checkout; the Stripe billing portal handles upgrade/downgrade/cancel/interval-switch self-serve. No manual invoicing, no MobilePay. See [docs/STRIPE_LOCAL.md](../STRIPE_LOCAL.md) for local testing and [docs/PRICING.md](../PRICING.md) for the current billing model.

## Status

**Accepted**

## Context

Schools and the platform operator both have limited admin time. Manual invoicing does not scale for either side and adds a recurring operational burden with no product value.

## Decision

All billing is handled via Stripe Checkout, with monthly or yearly billing intervals. The Stripe billing portal is used for subscription self-management (interval switch, module add/remove, cancel). No MobilePay integration. No manual invoicing.

## Consequences

### Positive

- **POS-001**: Eliminates manual invoicing burden entirely, for both the school and the platform operator.
- **POS-002**: Stripe Checkout handles SCA/3DS compliance out of the box.
- **POS-003**: The billing portal gives schools self-serve subscription management without a support request.

### Negative

- **NEG-001**: Ties billing entirely to Stripe's feature set and pricing (Stripe's own transaction fees apply).
- **NEG-002**: No MobilePay means schools that expect it as a Danish-market default payment method don't get it — accepted trade-off given B2B monthly/yearly subscription context, not a one-off consumer purchase.

## Alternatives Considered

### Manual invoicing

- **ALT-001**: **Description**: Send invoices directly, schools pay via bank transfer.
- **ALT-002**: **Rejection Reason**: does not scale, adds recurring admin burden this product exists to remove — would contradict the product's own value proposition.

### MobilePay integration

- **ALT-003**: **Description**: Add MobilePay as a payment method alongside card.
- **ALT-004**: **Rejection Reason**: adds integration complexity without meaningful value for a B2B monthly/yearly subscription (as opposed to a one-off consumer purchase, where MobilePay is standard in Denmark).

## Related Decisions

- [school-based-pricing](school-based-pricing.md) — the pricing model this billing mechanism implements
- [free-trial](free-trial.md) — trial period ahead of the first Checkout session

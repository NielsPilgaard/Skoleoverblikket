---
title: 'Pricing'
description: >-
  Current billing model — Basis flat fee, optional module add-ons, monthly or
  yearly interval, 30-day trial. Reflects SubscriptionModulesController /
  Stripe config, not just the original pricing ADR.
status: 'Living'
purpose: Canonical reference for current pricing tiers, trial terms, and billing mechanics — the source of truth PRD.md and AGENTS.md summarize from.
---

# PRICING.md — Skoleoverblikket Pricing

## Model

Simple flat monthly-or-yearly fee per school for the Basis plan, plus optional paid modules a school can add on top. 30-day free trial with full Basis access. Self-serve billing via Stripe Checkout — see [docs/adr/stripe-checkout-billing.md](adr/stripe-checkout-billing.md).

Pricing is transparent and listed on the website. No sales calls, no hidden fees, no per-student pricing — see [docs/adr/school-based-pricing.md](adr/school-based-pricing.md).

---

## Basis (base plan)

All core features, billed monthly or yearly (interval switch is self-serve via the Stripe billing portal). Exact DKK price is configured in Stripe, not this repo — see [docs/DEPLOYMENT.md](DEPLOYMENT.md) `Stripe__PriceId`.

Included:

- Schema planner with conflict detection
- Time slot wizard and per-class overrides
- Staff management (teachers, aides, substitutes)
- Class and course management
- File explorer (linked to courses)
- Stats and reporting (hours per course, hours per teacher/aide)
- Printable weekly schemas (per class, per teacher, per room)
- Admin dashboard
- Staff invitation and onboarding
- Email notifications
- A storage quota (school-level, size TBD per current Stripe config)

## Modules (optional add-ons)

Additional capabilities are sold as opt-in modules on top of Basis, each billed on the same monthly/yearly interval as the base subscription (switching interval switches the whole subscription, base plan and modules together). Enforced via `SubscriptionModulesController` — a tenant's active modules gate access to the corresponding feature area in the app.

| Module | Unlocks |
|---|---|
| Parent module | Parent portal: schema/calendar/ugeplan views, kontaktbog, beskeder, kontakt directory, fraværsregistrering, ferieindmelding — see [AGENTS.md](../AGENTS.md) built-features list |

More modules may be added over time; check `ModulePriceIds` in Stripe config for the current list rather than assuming this table is exhaustive.

---

## Trial

- **30-day free trial** with full Basis access.
- No credit card required to start the trial.
- Trial converts to paid via Stripe Checkout at the end of the 30 days.
- Schools that don't convert lose access (read-only grace period TBD).

## Payment

- Billing via Stripe Checkout, monthly or yearly interval.
- Auto-renew.
- Schools manage their subscription (interval switch, module add/remove, cancel) via the Stripe billing portal.
- All billing is self-serve — no manual invoicing, no MobilePay. See [docs/adr/stripe-checkout-billing.md](adr/stripe-checkout-billing.md).

## Notes

- Pricing is in DKK, targeting the Danish market only.
- No free tier — the 30-day trial replaces it.
- Scandinavian expansion may require pricing adjustments (SEK, NOK) in the future.
- This doc previously described a hypothetical future "Skole+" flat second tier. That model was superseded by the module-add-on approach actually implemented (`SubscriptionModulesController`) — this doc now reflects that.

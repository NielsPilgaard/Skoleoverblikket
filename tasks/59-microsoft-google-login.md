---
title: 'Staff login with Microsoft and Google'
purpose: 'Spec the free alternative to UNI•Login: staff log in with the school Microsoft 365 or Google Workspace account they already have.'
description: >-
  Add Microsoft (Entra ID) and Google as identity providers in Keycloak so staff
  can log in with their existing school account instead of a separate password.
  Free for us, unlike UNI•Login (task 07, parked over STIL fees). Reuses the
  linking design from task 07: link via invitation or "Forbind konto", no email
  matching, no auto-created accounts. Needs a quick check of which accounts
  target schools actually use before building.
status: 'Draft'
---

# Staff login with Microsoft and Google

## TL;DR

Staff click "Log ind med Microsoft" or "Log ind med Google" on the Keycloak login page. Keycloak federates to Entra ID or Google. On first use the external account is linked to an existing staff record through the invitation link or a "Forbind konto" button after normal login. After that, login goes straight in. No cost per login or per year, which is why this replaces [07 UNI•Login](07-uni-login.md) for now.

## Why

- UNI•Login costs 7,500 kr. setup plus 7,500 kr./year (see task 07), too much at current revenue.
- The problem it solved stays: one more password for staff. Many schools give staff a Microsoft 365 or Google Workspace for Education account, so either covers much of the same ground.
- Fits friskoler (primary market) at least as well as UNI•Login.

## Before building

- [ ] Ask existing and pilot schools which account their staff use daily: Microsoft 365, Google Workspace, or neither. Build only the provider(s) that show up.
- [ ] Check the vendor constraint in the memory note "EU vendors first": the school already has its own agreement with Microsoft/Google, and Keycloak stays the auth layer here. Decide whether the IdP counts as a sub-processor for [task 48](completed/48-databehandleraftale.md)'s list. Most likely not, since we don't send them school data, but confirm.
- [ ] Decide pricing: included in Basis (costs us nothing) or part of an add-on. See [PRICING.md](../docs/PRICING.md).

## Technical outline

- Add `microsoft` and/or `google` identity providers to [Skoleoverblikket-realm.json](../infrastructure/keycloak/realms/Skoleoverblikket-realm.json), client ID/secret from config, disabled when not configured.
- Microsoft: multi-tenant Entra app registration (`organizations` endpoint, so personal accounts are rejected). Google: one OAuth client of type "External" (an "Internal" client only works inside our own Workspace), which needs Google's app verification.
- Linking (from task 07 §3): store the provider subject on the staff record once linked. New staff link from the invitation page, existing staff link via Keycloak account linking. No email-based first-broker-login matching even though both providers release email, because an email match lets anyone with that address claim the account if the school recycles addresses.
- No-match: Danish error page telling the user to ask the school's admin for an invitation.
- Tests: integration tests for linking and no-match; Playwright e2e against a mock IdP realm in the Aspire stack (same mock approach planned in task 07).

## Tasks

- [ ] Before-building checks above
- [ ] Keycloak IdP config, disabled by default
- [ ] Link on invitation + "Forbind konto" for existing staff
- [ ] No-match error page
- [ ] Mock IdP realm in Aspire + tests
- [ ] Short Danish guide for Hanne: how staff connect their account

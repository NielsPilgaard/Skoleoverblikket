---
title: 'Keycloak 26.2 → 26.8'
purpose: 'Upgrade Keycloak in prod, staging and dev together, with a database backup before the one-way schema migration, replacing Dependabot PR #72.'
description: >-
  Dependabot #72 bumps the Keycloak image to 26.8 and its realm-stripping
  Alpine stage to 3.24. CI is green, but Keycloak migrates its database on
  first start and can't be rolled back without a restore, the Aspire AppHost
  pins 26.2 separately, and six minor versions of release notes need reading
  for theme and realm changes. Its web/Dockerfile node bump is already in #86.
status: 'Ready'
---

# Keycloak 26.2 → 26.8

## TL;DR

- #72 changes `infrastructure/keycloak/Dockerfile` (`keycloak:26.2` → `26.8`, `alpine:3.22` → `3.24`) and `web/Dockerfile` (`node:20-alpine` → `node:26-alpine`, the same line as #86, so it merges cleanly). CI including e2e passes.
- Risk 1: on first start Keycloak migrates its Postgres schema. Rolling back the image after that needs a database restore. Back up the Keycloak database first.
- Risk 2: [AppHost Program.cs](../infrastructure/aspire/Skoleoverblikket.AppHost/Program.cs) pins `quay.io/keycloak/keycloak:26.2` separately. Dev would keep running 26.2 while prod runs 26.8.
- Risk 3: custom login theme in `infrastructure/keycloak/themes/` and the realm import. Read the 26.3–26.8 upgrade notes for theme, realm-import and hostname changes.
- Size S–M. Owner: Niels + agent (the agent does the code and notes, Niels does the prod backup and watches the deploy).

## Steps

1. **Agent:** read the Keycloak upgrade guide for 26.3 through 26.8. List anything that touches our setup: login theme templates, realm JSON import, `KC_HOSTNAME*` options, health or metrics endpoints, admin REST API calls in `api/Skoleoverblikket.Api/Auth/` (Refit clients). Put the list in the PR.
2. **Agent:** on top of #72 (or a fresh branch if Dependabot moved on), set the AppHost image tag to the same version. Run `/verify` and `/test`. Check the login page, signup and password reset by hand in the Aspire stack, since the theme renders there.
3. **Niels:** before the deploy, take a fresh backup of the Keycloak database (see [RESTORE.md](../docs/RESTORE.md)). Write down how to roll back: previous image tag + restore that backup.
4. **Niels:** merge, watch the deploy, then log in on prod as a staff user and a parent user.
5. Close #72 if a replacement PR was used.

## Done when

- Prod, staging, e2e and dev all run the same Keycloak version, and login, signup and password reset work on prod.

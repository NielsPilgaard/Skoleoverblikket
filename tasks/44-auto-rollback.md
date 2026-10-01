---
title: 'Auto-rollback and deploy safety net'
purpose: 'Make every production deploy self-verifying and self-reverting, so a bad deploy (ours or an AI fix) never leaves schools on a broken site.'
description: >-
  After each CD deploy, watch production for 15 minutes (external health checks,
  read-only Playwright smoke test on an internal smoke tenant, elmah.io error
  spike). On failure, redeploy the last known good image tag, freeze CD and open
  a revert PR — unless a migration ran since last-good, in which case alert only.
  Adds a branch ruleset on main, a bot GitHub App, a public readiness endpoint
  and Pingpuffin monitoring. Phase 1 of the feedback system; ships before any AI code.
status: 'Proposed'
---

# Auto-rollback and deploy safety net

## TL;DR

New `watch` job at the end of `cd.yml`: 15 minutes of checks against prod. Pass → record the tag as **last known good** (GitHub Deployment on a separate `production-verified` environment). Fail → if no migration changed between last-good and this sha and prod has no applied migration missing at last-good: redeploy last-good with `deploy.mjs`, open a `deploy-freeze` issue (CD skips while it's open) and a revert PR that closes it. If a migration changed: no rollback, alert only. Applies to **all** deploys, not just AI ones. Also: `main` ruleset, "Skoleoverblikket Bot" GitHub App, `/api/health` readiness endpoint, smoke tenant (`IsInternal`), Pingpuffin for 24/7 uptime. Everything runs in public-repo Actions — €0.

## Context

Current pipeline ([ci.yml](../.github/workflows/ci.yml) → [staging.yml](../.github/workflows/staging.yml) → [cd.yml](../.github/workflows/cd.yml)):

- Push to `main` → CI builds, tests, **applies migrations to prod via `psql` before deploy**, publishes `sha-xxx` images to GHCR.
- Staging runs Playwright e2e against the images; on success its `deploy` job calls CD (reusable workflow) with CI's image tag.
- CD calls [infrastructure/scripts/deploy.mjs](../infrastructure/scripts/deploy.mjs) (Dokploy `compose.saveEnvironment` + `compose.redeploy`) and stops once Dokploy says "done". Nobody checks that the site actually works afterwards.
- Rollback is possible by hand: `CD` workflow_dispatch with an older `image_tag`.
- `/alive` only checks the process is up (not DB/Keycloak). `/health` and `/alive` are not routed publicly — Traefik only forwards `PathPrefix(/api)` to the API.
- `main` has **no branch protection**.

Because migrations run before deploy and are forward-only, rolling back the image after a migration means old code against a new schema — possibly worse than the bug. AI fix PRs can never contain migrations ([ai-data-boundary](../docs/adr/ai-data-boundary.md)), so they are always safe to roll back.

## Decisions (confirmed)

- **D1 — Scope**: auto-rollback on **all** deploys whose range since last-good has no migration change. Exercising the path on our own deploys keeps it trustworthy.
- **D2 — Migration deploys**: no auto-rollback. Alert (email) only; a human decides.
- **D3 — Smoke tenant**: a dedicated, isolated internal tenant in prod ("Smoketest Friskole"), not reused from sales demos.
- **D4 — Freeze, don't push**: automation never pushes to `main`. Rollback = redeploy + freeze issue + revert PR.
- **D5 — 24/7 monitoring**: Pingpuffin (Danish), alert only. Off-deploy outages are usually VPS/DB/Keycloak; rolling back code doesn't fix those.
- **D6 — Alerts by email** via Scaleway SMTP (EU), because rollback is urgent and GitHub notification emails are unreliable for `workflow_run`.

## Scope

### 1. Readiness endpoint

- `GET /api/health` (anonymous): checks PostgreSQL (`SELECT 1`) and Keycloak OIDC discovery reachability. 200/503 with a minimal body (no versions, no internals beyond `status`).
- Also returns header `X-App-Version: <image sha>` so the watch job can assert the new version is live. Bake the sha into the image as `App__Version` build arg (also used by task 45/46).
- Keep `/alive` as the container healthcheck (process only).

### 2. Bot GitHub App

- Create GitHub App **"Skoleoverblikket Bot"**, installed on the repo. Permissions: `contents: write`, `pull_requests: write`, `issues: write`, `deployments: write`, `metadata: read`.
- Workflows get a token via `actions/create-github-app-token`. Needed because PRs created with `GITHUB_TOKEN` don't trigger CI.
- Secrets: `BOT_APP_ID`, `BOT_APP_PRIVATE_KEY`. Reused by task 47 (the API dispatches with it).
- The app is **not** on the ruleset bypass list.

### 3. Branch ruleset on `main`

- Require status checks: `API — build & test`, `Web — build & lint` (and `ai-fix-guard` once task 47 lands).
- Bypass: repository admin only (owner keeps direct pushes). Bot app and Claude app cannot bypass.
- Configure via `gh api` and document the command in this task when done.

### 4. Smoke tenant

- `Tenant.IsInternal` bool (default false) — **migration written by a human** (`/add-migration`).
- Internal tenants are excluded from: `StatsController` totals, SuperAdmin tenant lists (unless "vis interne" filter), Stripe trial expiry / billing gates, marketing/onboarding emails.
- Seed once by hand in prod: fake classes, schema, ugeplan, one staff user `smoke@…` whose password lives in secrets `SMOKE_USER` / `SMOKE_PASSWORD`. Fake data only.
- Playwright project `smoke` (`web/tests/smoke/*.spec.ts`, outside the regular `tests/e2e` testDir), base URL `https://skoleoverblikket.dk`, **read-only**: login → dashboard → class schema → ugeplan → logout. `data-testid` selectors only.
- **Trace, video and screenshot off** for the smoke project. The repo is public and traces would contain the smoke user's JWT.

### 5. Watch job (`cd.yml`)

Runs after `deploy`, `timeout-minutes: 20`.

**Concurrency**: one workflow-level group in `cd.yml`, `concurrency: { group: production-deploy, cancel-in-progress: false }`, so `deploy`, `watch` and `rollback` of one run hold the group together and a new deploy cannot start while a watch or rollback is active. Every CD entry point (`workflow_run` from Staging, `workflow_dispatch` incl. manual rollback with `image_tag`) runs through `cd.yml` and therefore joins this group; no other workflow may deploy prod. Note: GitHub keeps at most one *pending* run per group, so a newer queued deploy replaces an older queued one — acceptable, since the newer sha includes the older.

1. Fetch last-good tag from a **separate record**: latest GitHub Deployment with status `success` on environment `production-verified` (written only by step 6, never by the `deploy` job), excluding the sha being watched. Do not read the `production` environment: the `deploy` job's `environment: production` makes GitHub mark the current candidate `success` before it is verified.
2. For 15 minutes, every 30 s: `GET /` (200 + HTML references a hashed bundle), `GET /api/health` (200 + `X-App-Version` == deployed sha after the first 2 minutes), Keycloak discovery `GET https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/.well-known/openid-configuration`.
3. At ~2 min and ~10 min: run the `smoke` Playwright project (one automatic retry).
4. At end: elmah.io API — count errors in the watch window vs the 15 minutes before deploy. Fail if `count > max(ELMAH_MIN_ERRORS, ELMAH_SPIKE_FACTOR × baseline)` (repo variables, start at 10 and 5).
5. **Fail conditions**: 3 consecutive failures of any health probe, smoke failing after retry, or error spike.
6. **Pass**: create a Deployment on environment `production-verified` for this sha with status `success` → it becomes last-good. Then (task 47) post `Feedback:` trailers to the API.

Implement as a Node script `infrastructure/scripts/watch.mjs` (same style as `deploy.mjs`), with Playwright invoked from the workflow.

### 6. Rollback job

Runs when `watch` fails:

1. **Migration check against production, not just the sha range.** CI applies migrations to prod on every push to `main`, so prod can hold migrations from commits newer than `<sha>` (e.g. a later push whose CD run is still queued). Read `MigrationId`s from prod `__EFMigrationsHistory` (same `DATABASE_URL` CI uses for `psql`) and compare with the migration files present at `<last-good-sha>` (`git ls-tree --name-only <last-good-sha> -- api/Skoleoverblikket.Api/Data/Migrations/`). Any applied migration missing at last-good, **or** `git diff --quiet <last-good-sha>..<sha> -- api/Skoleoverblikket.Api/Data/Migrations/` reporting a change → **alert only** (email: "Deploy <sha> fejlede — migration i spil, ingen automatisk rollback"). Stop. If the prod query fails, treat it as "migration found" (alert only).
2. Otherwise: run `deploy.mjs` with `IMAGE_TAG=<last-good tag>`, then a shortened watch (health probes only, 3 minutes). If that also fails → open the `deploy-freeze` issue (step 3, title `🚨 Deploy frosset: rollback af <sha> til <last-good> fejlede`) so the freeze gate blocks further CD runs, keep the "rollback fejlede" alert email, and stop (no revert PR).
3. Open issue `🚨 Deploy frosset: <sha> rullet tilbage til <last-good>` with label `deploy-freeze` (failing checks summarised, links to the run).
4. Create branch `rollback/<sha>` with `git revert --no-edit <last-good-sha>..<sha>` and open a PR labelled `rollback-revert` whose body has `Closes #<freeze issue>`. Bot app token so CI runs.
5. Email the owner with links.

### 7. Freeze gate

- First step of `cd.yml` `deploy` job: if any open issue has label `deploy-freeze` → skip (log why), unless `workflow_dispatch` with input `force: true`.
- Merging the revert PR closes the freeze issue → next push deploys normally. A forward fix means closing the issue by hand.
- Task 47's dispatch also checks the freeze.

### 8. Pingpuffin

Manual setup (no code): monitors for `https://skoleoverblikket.dk/`, `/api/health`, Keycloak discovery URL; alert by email. Record the monitor list here when done.

## Open questions

- Thresholds (`ELMAH_MIN_ERRORS`, `ELMAH_SPIKE_FACTOR`, 3 consecutive probe failures) are guesses — tune after the first ~10 deploys.
- Does the Keycloak image change often enough that rolling it back with the others could ever hurt (realm import differences)? Currently all three images roll together; revisit if Keycloak config becomes stateful.

## Out of scope

- Database rollback / down-migrations. Migration deploys stay human-handled.
- Blue/green or canary traffic splitting (single VPS, single replica).
- Rollback of staging.

## Testing

- API integration test: `/api/health` returns 200 with DB up, 503 with an unreachable Keycloak authority (configure a bogus authority in the factory).
- API integration test: internal tenants excluded from stats and SuperAdmin tenant list.
- Rollback path: deliberately deploy a tag whose web bundle throws on load (a throwaway branch built to a test tag) via `workflow_dispatch` — verify redeploy, freeze issue, revert PR, email. Do this once before task 47 goes live, and document the result here.

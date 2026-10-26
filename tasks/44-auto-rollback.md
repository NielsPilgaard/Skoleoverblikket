---
title: 'Auto-rollback and deploy safety net'
purpose: 'Make every production deploy self-verifying and self-reverting, so a bad deploy (ours or an AI fix) never leaves schools on a broken site.'
description: >-
  After each CD deploy, watch production for 15 minutes (external health checks,
  read-only Playwright smoke test on a fake-data smoke school, elmah.io error
  spike). On failure, redeploy the image tag that was running before — unless
  migrations changed in between, in which case the run just goes red. No freeze,
  no revert PR. Adds a branch ruleset on main, a public readiness endpoint,
  elmah.io deployment tracking and Pingpuffin monitoring. Phase 1 of the feedback
  system; ships before any AI code.
status: 'In progress'
---

# Auto-rollback and deploy safety net

## Status (2026-10-26)

**Code is built** (§1, 1b, 4, 5, 6). Choices made while building it:

- **Simplified rollback (2026-10-26, Niels)**: the first build had a `production-verified` record, a `deploy-freeze` issue, a revert PR with dispatched CI and migration gate, a prod `__EFMigrationsHistory` check and SMTP alerts. All dropped. `deploy.mjs` reads the tag running before it deploys; a failed watch redeploys that tag. Unfixed code on `main` gets redeployed by the next merge, fails its watch and is rolled back again, so no freeze is needed.
- **Smoke school billing**: give it free access with no end date (backoffice) instead of billing code that skips it.
- **Smoke flow**: login → staff dashboard → `/mig/skema` → click a lektion → that klasse's ugeplan → log out. Mit skema is the read-only way into a class schema for staff. The config is a separate [playwright.smoke.config.ts](../web/playwright.smoke.config.ts), outside `tests/e2e`, not a project in the e2e config, so it skips the e2e global setup and Aspire.
- **Rollback only on a watch verdict**: the rollback step runs only when `watch.mjs` reported a reason. npm or Playwright install failures turn the run red without rolling back.
- **Alert = the red run.** GitHub emails the owner about failed runs. Check during the drill that this email arrives for a CD run started by Staging; if not, the Pingpuffin monitors are the backstop.
- **Task 47**: its D5 ("no dispatch while deploys are frozen") referred to the dropped freeze issue. Replace it with "no dispatch while the latest CD run failed" when 47 is built.

**Left for Niels** (secrets and variables: [DEPLOYMENT.md](../docs/DEPLOYMENT.md#github-actions-secrets-and-variables-for-cd)):

1. Repo settings: the Actions secrets and variables, including `SMOKE_USER` / `SMOKE_PASSWORD`.
2. After this ships: switch the Pingpuffin monitor to `/api/health` (§8).
3. After this merges and `CI result` has run on `main`: add `CI result` and `Migration review` as required checks on the `default` ruleset (§3).
4. The rollback drill from Testing, once.

Done 2026-10-26: Pingpuffin (§8), smoke school in prod, `default` ruleset requires a pull request.

## TL;DR

After `deploy.mjs` deploys, the same CD job watches prod for 15 minutes. `deploy.mjs` first records the tag that was running. Watch fails → if no migration changed between that tag and this sha, redeploy it; either way the run goes red. Applies to **all** deploys, not just AI ones. Also: `main` ruleset, `/api/health` readiness endpoint (Xabaril health-check packages), elmah.io deployment per deploy (version = sha), smoke school, Pingpuffin for 24/7 uptime. Everything runs in public-repo Actions — €0.

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

- **D1 — Scope**: auto-rollback on **all** deploys whose range since the previous tag has no migration change. Exercising the path on our own deploys keeps it trustworthy.
- **D2 — Migration deploys**: no auto-rollback. The run goes red; a human decides.
- **D3 — Smoke tenant**: a dedicated school in prod ("Smoketest Friskole") with fake data only, not reused from sales demos. No `IsInternal` flag (dropped 2026-10-26): it showing in the backoffice list and getting the welcome email is fine.
- **D4 — Redeploy only**: automation never touches `main`. Rollback = redeploy the previous tag. No freeze issue or revert PR (dropped 2026-10-26: a solo owner fixes forward, and a repeat of the bug is rolled back again).
- **D5 — 24/7 monitoring**: Pingpuffin (Danish), alert only. Off-deploy outages are usually VPS/DB/Keycloak; rolling back code doesn't fix those.
- **D6 — Alerts**: GitHub's failed-run email (SMTP alerts dropped 2026-10-26). Verify it arrives during the drill.

## Scope

### 1. Readiness endpoint

- `GET /api/health` (anonymous): standard ASP.NET Core health checks tagged `ready`, mapped with `MapHealthChecks("/api/health", Predicate = tag "ready")`. 200/503 with a minimal body (no versions, no internals beyond `status`).
  - PostgreSQL: `AspNetCore.HealthChecks.NpgSql` (`AddNpgSql(connectionString, tags: ["ready"])`) — no hand-written `SELECT 1`.
  - Keycloak: `AspNetCore.HealthChecks.Uris` against the OIDC discovery URL (same Xabaril family), tag `ready`.
- Also returns header `X-App-Version: <image sha>` so the watch job can assert the new version is live. Bake the sha into the image as `App__Version` build arg (also used by task 45/46).
- Keep `/alive` as the container healthcheck (process only). `/health` (all checks, from ServiceDefaults) stays internal.

### 1b. elmah.io deployment tracking

- After `deploy.mjs` succeeds, the `deploy` job creates an elmah.io deployment: `POST https://api.elmah.io/v3/deployments` with `version: <sha>`, `description: <commit subject>`, `userName: <github.actor>`, `logId: ELMAHIO_LOG_ID`. API key with only *Deployments – Write*, secret `ELMAHIO_DEPLOYMENT_API_KEY` (separate from the app's logging key). Plain `curl` step — no extra action.
- The rollback step creates a deployment for the previous version too, so errors after a rollback are attributed to the version actually running.
- The app also sets `msg.Version = App__Version` in `OnMessage` for both the ASP.NET Core and logging providers, so attribution is exact even during the container switchover (elmah.io only fills `Version` from the latest deployment when the message has none).
- Gives deployment markers and per-version error counts in the elmah.io UI for free, and lets the watch job count errors **by version** instead of by time window (§5.4).

### 2. Workflow tokens (no GitHub App)

Everything in this task uses `GITHUB_TOKEN` with `contents: read`, `packages: read`. The GitHub App moved to [task 47](feedback/47-feedback-ai-fix-prs.md), where the API needs a non-expiring credential and AI PRs need a bot author.

### 3. Branch ruleset on `main`

Set up for phone fixes via Claude cloud sessions (see "Cloud sessions" in AGENTS.md). This task only adds `ai-fix-guard` once task 47 lands.

- Require a pull request (0 approvals; solo owner can't approve own PRs). Done; the owner is the one bypass actor.
- Required checks: only `CI result` and `Migration review`. `CI result` is the last job of [ci.yml](../.github/workflows/ci.yml) and fails if any other job failed or was cancelled, so jobs can be renamed or added without touching the ruleset. `Migration review` stays separate: it reruns on label changes, which CI must not. GitHub's "require workflows to pass" rule would avoid check names entirely, but it is only for organization rulesets.
- Add the checks once `CI result` has run on `main` (a required check that never runs blocks every PR).

### 4. Smoke tenant

- Set up by hand in prod (done 2026-10-26): fake classes, schema, ugeplan, one staff user whose login lives in secrets `SMOKE_USER` / `SMOKE_PASSWORD`. Fake data only. Free access with no end date.
- Playwright project `smoke` (`web/tests/smoke/*.spec.ts`, outside the regular `tests/e2e` testDir), base URL `https://skoleoverblikket.dk`, **read-only**: login → dashboard → class schema → ugeplan → logout. `data-testid` selectors only.
- **Trace, video and screenshot off** for the smoke project. The repo is public and traces would contain the smoke user's JWT.

### 5. Watch (`cd.yml`)

Steps after the deploy in the same `deploy` job (`timeout-minutes: 60`).

**Concurrency**: one workflow-level group in `cd.yml`, `concurrency: { group: production-deploy, cancel-in-progress: false }`, so a new deploy cannot start while a watch or rollback is active, and "the tag running before" is always a finished run's tag. Every CD entry point (`workflow_run` from Staging, `workflow_dispatch` incl. manual rollback with `image_tag`) runs through `cd.yml` and therefore joins this group; no other workflow may deploy prod. Note: GitHub keeps at most one *pending* run per group, so a newer queued deploy replaces an older queued one — acceptable, since the newer sha includes the older.

1. `deploy.mjs` reads the running tag from the Dokploy compose env (`API_IMAGE`) before saving the new one, and outputs it as `previous_tag`.
2. For 15 minutes, every 30 s: `GET /` (200 + HTML references a hashed bundle), `GET /api/health` (200 + `X-App-Version` == deployed sha after the first 2 minutes), Keycloak discovery `GET https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/.well-known/openid-configuration`.
3. At ~2 min and ~10 min: run the `smoke` Playwright project (one automatic retry).
4. At end: elmah.io messages API — count Error/Fatal messages with `version:<sha>` in the watch window vs messages with `version:<previous sha>` in the 15 minutes before deploy. Fail if `count > max(ELMAH_MIN_ERRORS, ELMAH_SPIKE_FACTOR × baseline)` (repo variables, start at 10 and 5). Read key: secret `ELMAHIO_READ_API_KEY` (*Messages – Read* only).
5. **Fail conditions**: 3 consecutive failures of any health probe, smoke failing after retry, or error spike.
6. **Pass**: the run is green. Then (task 47) post `Feedback:` trailers to the API.

Implement as a Node script `infrastructure/scripts/watch.mjs` (same style as `deploy.mjs`), with Playwright invoked from the workflow.

### 6. Rollback step

Last step of the job, runs when the watch reported a failure:

1. No previous tag, or `git diff --quiet <previous sha> <sha> -- api/Skoleoverblikket.Api/Data/Migrations/` reports a change (or fails) → no rollback, error annotation. CI already migrated prod, so old code would run against the new schema.
2. Otherwise: `deploy.mjs` with `IMAGE_TAG=<previous tag>`, an elmah.io deployment for it, then a 3-minute watch (health probes only).
3. The run stays red either way.

### 8. Pingpuffin

Done 2026-10-26: one monitor, alert by email. Today on `https://skoleoverblikket.dk/`; switch it to `https://skoleoverblikket.dk/api/health` once this ships. That URL goes through the web container's nginx to the API, so one monitor covers web, API, Postgres and Keycloak.

## Open questions

- Thresholds (`ELMAH_MIN_ERRORS`, `ELMAH_SPIKE_FACTOR`, 3 consecutive probe failures) are guesses — tune after the first ~10 deploys.
- Does the Keycloak image change often enough that rolling it back with the others could ever hurt (realm import differences)? Currently all three images roll together; revisit if Keycloak config becomes stateful.

## Out of scope

- Database rollback / down-migrations. Migration deploys stay human-handled.
- Blue/green or canary traffic splitting (single VPS, single replica).
- Rollback of staging.

## Testing

- API integration test: `/api/health` returns 200 with DB up, 503 with an unreachable Keycloak authority (configure a bogus authority in the factory).
- Rollback path: deliberately deploy a tag whose web bundle throws on load (a throwaway branch built to a test tag) via `workflow_dispatch` — verify the redeploy, the elmah.io deployment for the previous tag, and that GitHub's failed-run email arrives. Do this once before task 47 goes live, and document the result here.

---
title: 'Faster e2e in CI'
purpose: 'Cut the E2E job, the slowest job on every PR, from ~5 minutes (sometimes 15) to about the length of the API job.'
description: >-
  The Playwright suite runs in ~30s; the rest of the 260–350s E2E job is setup,
  and in 2 of 6 measured PR runs an apt step hung for 10+ minutes. Five fixes:
  use the runner's preinstalled Chrome instead of apt deps, boot the stack in
  parallel with Node setup, tighten healthchecks, build images in parallel, and
  stop rebuilding the web image. Target ~150s, no 15-minute tail.
status: 'Ready'
---

# Faster e2e in CI

## TL;DR

- E2E ([e2e.yml](../.github/workflows/e2e.yml)) is the critical path on PRs: 260–350s vs. 120–180s for the API job. Staging runs the same job before every deploy.
- Biggest problem: `npx playwright install-deps chromium` (apt) took **635s and 668s** in 2 of 6 PR runs. Fix 1 removes it.
- Phase 1 (fixes 1–3) is workflow and compose changes only, about half a day. Phase 2 (fix 5) is small. Phase 3 (fix 4) touches the web app, not the prod deploy.
- All three phases are built. Left: measure one PR run and one Staging run against the targets below, then close the task.
- Done when: a PR run of E2E takes ≤ ~180s after Phase 1 and ≤ ~150s after Phase 3, and no run spends more than a minute on browser setup.

## Baseline (6 PR runs, 2026-10-26)

| Step | Typical | Notes |
|---|---|---|
| Install Playwright system deps (apt) | 12–23s | 635s and 668s in 2 of 6 runs, making those jobs ~15 min |
| Build API + Keycloak images (PR only) | ~35s | Built one after another; Staging pulls them instead |
| Build web image | 55–69s | Every run, Staging included |
| Start staging stack (`up --wait`) | 42–51s | Keycloak → API → web start in sequence |
| Playwright tests | 27–36s | Not worth tuning |
| **Job total** | **260–350s** | |

Measure per-step times with:

```sh
gh api repos/{owner}/{repo}/actions/runs/<run-id>/jobs --jq '.jobs[] | select(.name|test("E2E")) | .steps[] | "\(((.completed_at|fromdate) - (.started_at|fromdate)))s  \(.name)"'
```

## Phase 1: workflow and compose (S, Agent)

**Built 2026-10-26, measured 2026-10-30** (5 PR and 3 Staging runs): the browser steps are gone and "Wait for staging stack" takes 17–22s instead of 42–51s. But the Playwright step went from 27–37s to 48–75s with the same 55 tests, every test ~1.6× slower. Full Chrome in new-headless mode is heavier than Playwright's headless shell. Fix (2026-10-30): CI uses the headless shell again, installed with `npx playwright install --only-shell chromium` and no `--with-deps`, because the runner's Chrome already brought the system libraries. Left: confirm on the next PR that the Playwright step is back near 35s. Job totals before that fix: PR 192–318s, Staging 150–181s.

Differences from the plan below:

- `docker compose up -d` without `--wait` still blocks on every `depends_on` healthcheck, so the stack starts with `up -d --wait` in a background subshell, and a "Wait for staging stack" step after `npm ci` waits for its exit code.
- `start_interval: 1s` is on every healthcheck, not just `keycloak` and `api`, with `start_period` raised to 120s (Keycloak) and 60s (API) so the fast polling lasts the whole boot.
- The API needs nothing from Keycloak at startup except `AdminRoleBackfillJob`, which retries after 5 minutes (removed in task 61 Phase B). So `api → keycloak` is now `service_started`.
- The smoke config ([playwright.smoke.config.ts](../web/playwright.smoke.config.ts)) uses the runner's Chrome in CI, and cd.yml no longer installs a browser. It stays on Chrome after the headless-shell fix: it runs one test per deploy.

### Fix 1: use the runner's Chrome, drop apt

GitHub's Ubuntu runner image ships Google Chrome with its system libraries. In [playwright.config.ts](../web/playwright.config.ts), set `channel: 'chrome'` on the chromium project when `process.env.CI` is set. Locally it keeps using Playwright's bundled Chromium.

Then delete the three browser steps in [e2e.yml](../.github/workflows/e2e.yml): "Cache Playwright browsers", "Install Playwright browsers" and "Install Playwright system deps".

- Saves ~15–25s per run and removes the 10-minute apt hangs.
- Tradeoff: CI tests against stable Chrome, not Playwright's pinned Chromium. Fine for these flows. A Chrome/Playwright mismatch would show up as a launch error, not a flaky test.
- [cd.yml](../.github/workflows/cd.yml) runs `npx playwright install --with-deps chromium` for the post-deploy smoke watch. Give it the same treatment if `watch.mjs` launches through the same config or can take a channel. Otherwise leave it, since it only runs once per deploy.

### Fix 2: boot the stack during Node setup

Today the order is Node setup → image builds → `docker compose up -d --wait`, so Keycloak's ~45s boot starts last. Reorder:

1. Checkout, buildx, GHCR login, pull or build images.
2. `docker compose up -d` (no `--wait`).
3. Setup Node, `npm ci`.
4. `docker compose up -d --wait` (idempotent; waits for health).
5. Run Playwright.

Saves ~30s. Keep the 5-minute timeout on the wait step.

### Fix 3: healthchecks in [docker-compose.staging.yml](../infrastructure/docker/docker-compose.staging.yml)

- Add `start_interval: 1s` to `keycloak` and `api`. Keycloak currently polls every 10s, so it can sit healthy for up to 10s before the API is allowed to start.
- Check whether the API needs Keycloak at startup (JWT metadata is normally fetched lazily on the first authenticated request; look for anything that calls the Keycloak admin API on boot). If it doesn't, change `api → keycloak` from `service_healthy` to `service_started` so the API boots alongside Keycloak. `web` still waits for a healthy API, and Playwright's global setup talks to Keycloak directly, so `up --wait` still covers it.

Saves ~10–20s.

## Phase 2: parallel image builds (S, Agent)

**Built 2026-10-30.** One `docker/bake-action` step with [docker-bake.e2e.hcl](../infrastructure/docker/docker-bake.e2e.hcl) builds all three targets on PRs. Staging skips it and pulls the three published images. `source: .` makes bake build the checked-out ref instead of its default Git context. Left: measure "Build images" on the next PR against the ~100s of the three old steps (the API build alone varied 10–168s with cache hits and misses).

On PRs, the API, Keycloak and web images build one after another (~100s). Replace the three `docker/build-push-action` steps with one `docker/bake-action` and a small `docker-bake.hcl` (or a bake target in the workflow) that builds all three with `load: true` and the same `type=gha` cache scopes (keep `ignore-error=true` on `cache-to`). Saves ~20–30s.

On Staging only the web image builds, so this phase helps PRs only.

## Phase 3: stop rebuilding the web image (M, Agent)

**Built 2026-10-31 with option A**, without the prod change. The image keeps the prod URL baked in as `VITE_KEYCLOAK_URL`, and `KEYCLOAK_URL` at container start overrides it via [runtime-config.sh](../web/runtime-config.sh) → `/env.js` (served `no-cache`). Prod leaves `KEYCLOAK_URL` unset, so the rollout needs nothing from Niels. Staging pulls the published web image, and PR e2e builds it once in the bake without a build arg. The staging compose file sets `KEYCLOAK_URL=http://localhost:8080`. Proven locally: an image built with the prod URL reports `authServerUrl` `http://localhost:8080` with the variable set and the prod URL without it. Invalid values (no `http(s)://`, quotes, backslashes) stop the container before nginx starts. Left: measure the next Staging run, where the ~60s web build should be gone.

The published web image has the prod Keycloak URL baked in at build time (`VITE_KEYCLOAK_URL` in [web/Dockerfile](../web/Dockerfile)), so e2e rebuilds it every run (~60s). Pick one:

- **A. Runtime config (recommended).** The nginx container writes `/env.js` from environment variables at start (an entrypoint script in `/docker-entrypoint.d/`), `index.html` loads it before the app, and the web code reads the Keycloak URL from `window` with the Vite variable as a fallback for `npm run dev`. Then one image works in every environment: Staging pulls the published image instead of building, PR e2e builds it once in Phase 2's bake, and prod sets the URL in its compose file. Niels updates the prod compose/Dokploy environment at rollout.
- **B. Build `dist` on the host.** E2E already runs `npm ci` for Playwright. Run `npm run build` with the local Keycloak URL and serve `web/dist` from a plain `nginx:alpine` with [nginx.conf](../web/nginx.conf) mounted. No prod changes, but e2e no longer tests the real web image.

Saves ~40–60s per run, on Staging too.

## Out of scope

- Sharding or more Playwright workers. The suite is ~30s.
- Caching the whole compose stack or Keycloak data between runs. Not worth the flakiness.

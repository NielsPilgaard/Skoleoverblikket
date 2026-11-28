---
title: 'E2E coverage for critical flows'
purpose: 'Close the gaps in Playwright coverage so that a green Staging run means the flows schools depend on still work.'
description: >-
  The e2e suite has 26 specs, but the schema builder (the core product),
  ferieindmelding, SFO, stå-mål-med, class rollover and the Stripe checkout
  start have no flow test. Add one spec per flow, ranked by how badly a silent
  break would block Hanne, and widen the read-only prod smoke test to open the
  same pages. Goal: deploying without manually clicking through the app.
status: 'Ready'
---

# E2E coverage for critical flows

## TL;DR

- Staging runs the whole Playwright suite before every deploy ([TESTING.md](../docs/TESTING.md#ci)), and [task 44](44-auto-rollback.md) rolls back a deploy whose smoke test fails. Both only catch what the tests exercise.
- Untested today: **schema builder** (creating a lektion, the conflict warning), **ferieindmelding** end to end, **SFO** week plan, **stå-mål-med**, **class rollover** and **billing** (Stripe checkout start). Each is a flow Hanne can't recover from on her own if it breaks.
- Add one spec per flow (Phases 1–3), then widen the prod smoke test to open the same pages read-only (Phase 4).
- Size M. Owner: Agent. Do it before the refactors in [79](79-architecture-deepening-round-1.md), [73](73-shared-ui-components-round-2.md) and [74](74-back-navigation-restores-state.md), which touch these pages.

## Why now

Deploys feel risky because a green pipeline doesn't prove that the main flows work. The suite covers login, invitations, fravær, vikar, files, calendar, Klassechat, import and the ugeplan, but nothing creates a lektion in the schema builder, which is what schools buy the product for. Integration tests cover the conflict rules in the API. Nothing checks that the builder UI calls the API and shows the result.

## Rules

Follow [TESTING.md](../docs/TESTING.md): one spec per flow, `data-testid` selectors only (add them where missing, also in components), arrange data through the API (`api()` in [parents.ts](../web/tests/e2e/parents.ts)), no `page.route` fakes except the Stripe redirect target in Phase 3. Each test creates its own data with unique names, so tests can run in parallel against the shared Debugskolen tenant without depending on each other.

Before merging, run each new spec with `--repeat-each=5` locally. A flaky test erodes trust in a green run as much as a missing one.

## Phase 1: Schema builder (`schema-builder.spec.ts`)

Highest value. Admin flow on `/klasser/:classId/skema/:schemaId` ([SchemaBuilderPage.tsx](../web/src/pages/SchemaBuilderPage.tsx)):

- [ ] Arrange via API: a class, a fag, two lærere, a lokale and a schema.
- [ ] Add a lektion through the UI (fag, lærer, lokale, time slot). It shows in the builder, and after a reload it is still there.
- [ ] The same lektion shows in the teacher's schedule (`/medarbejdere/:id/skema`) and the room's schedule (`/lokaler/:roomId/skema`).
- [ ] Conflict: in a second class, put the same lærer in the same slot. The conflict warning shows and names the lærer. Fixing it (picking the other lærer) clears the warning.
- [ ] Edit and delete a lektion. Both survive a reload.
- [ ] Phone width: the teacher's `/mig/skema` shows the lektion at 390 px width without horizontal scrolling (staff views must work on a phone).

## Phase 2: Seasonal and daily admin flows

These break unnoticed because they're used once a season, and then a whole school notices at once.

- [ ] **`vacation-registration.spec.ts`**: admin creates a window (weeks), opens it, parent registers their child for a week ([parents.ts](../web/tests/e2e/parents.ts) `createParentLogin`), admin sees the answer on the detail page, closes the window, and the parent can no longer submit. Remember the SFO rule: SFO holidays follow ferieindmelding, not the school's Ferie/Lukkedag.
- [ ] **`sfo.spec.ts`**: admin fills the SFO week plan, it shows in every view that reads it (check [FEATURES.md](../docs/FEATURES.md) for which roles see it), the print view (`/udskriv/sfo`) renders.
- [ ] **`staa-maal-med.spec.ts`**: with a schema from the Phase 1 arrange helper, the coverage page shows the fag with hours, and the board view (`/bestyrelse/staa-maal-med`) shows the same.
- [ ] **`class-rollover.spec.ts`**: roll a test class over to the next school year ([ClassRolloverPage.tsx](../web/src/pages/ClassRolloverPage.tsx)) and check the result the page promises (read the page first; don't guess the rules). Use only classes the test created, never Debugskolen's seeded classes.

## Phase 3: Billing (`billing.spec.ts`)

- [ ] `/abonnement` renders the trial/subscription state.
- [ ] "Betal" starts Stripe Checkout: assert the redirect goes to `checkout.stripe.com` and stub that target with `page.route`. No e2e against Stripe itself (Stripe's test mode is covered by [STRIPE_LOCAL.md](../docs/STRIPE_LOCAL.md) manually). If [62](62-stripe-to-alunta.md) ships first, test the Alunta redirect instead.
- [ ] A parent-module page is reachable for a parent when the module is on. A broken module gate locks every parent out.

## Phase 4: Wider prod smoke test

Staging catches code regressions. The smoke test after deploy ([smoke.spec.ts](../web/tests/smoke/smoke.spec.ts)) catches what only prod has: config, real data, migrations. Extend it, **read-only**, on the smoke school:

- [ ] Open a schema in the schema builder (no edits) and check that lektioner render.
- [ ] Open `/mig/skema` or a teacher's schedule, `/sfo`, `/ferieindmelding` and `/abonnement`, and check each renders its main `data-testid` without an error message.
- [ ] Keep trace, video and screenshot off ([task 44](44-auto-rollback.md) §4: traces would hold the smoke user's JWT in a public repo).
- [ ] Keep the whole smoke run under 1 minute, so the watch window in task 44 isn't stretched.

## Done when

- All new specs pass in PR e2e and Staging, and each passed `--repeat-each=5` locally.
- [TESTING.md](../docs/TESTING.md) "Covered flows" lists the new flows.
- `/verify` and `/test` pass.

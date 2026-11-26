---
title: 'Local load testing for a large school'
purpose: 'Find out, before a big school signs up, whether one ~800-student school (and the schools around it) runs fast on our stack.'
description: >-
  Nobody knows how the API performs for a school with ~800 students, 80 rooms
  and 100 teachers. Build a generator for a realistic large tenant, k6 scripts
  for the real peaks (morning schedule check, schema editing with conflict
  detection, ferieindmelding evening, message to all parents) and run them
  locally, first on Aspire to find slow queries, then on containers capped to
  the prod VPS size to find capacity. Never against prod.
status: 'Ready'
---

# Local load testing for a large school

## TL;DR

- Our typical customer has ~300 students. A school with ~800 students, 80 rooms and 100 teachers is untested, and the core promise (real-time conflict detection) is the part most likely to slow down.
- Plan: a generator seeds one large school plus ~20 typical ones, k6 runs four realistic peak scenarios, and we read latency from k6 and query counts from the Aspire dashboard.
- Two passes: **Phase 2** on the normal Aspire stack finds slow code (N+1 queries, missing indexes, O(n²) loops). These problems show up on any machine. **Phase 3** runs the prod images with CPU and RAM capped to the prod VPS and finds how many schools one VPS can carry.
- Local only. Never run load against prod: it shares one VPS with real schools.
- Size M. Owner: Agent. Prod VPS size and thresholds are decided below.

## Why now

- Before the first school over ~500 students signs up. A slow schema builder during the summer planning would be found by the customer, not by us.
- Known suspect: [ConflictDetectionService.cs](../api/Skoleoverblikket.Api/Services/ConflictDetectionService.cs) loads every slot of every active schema in the tenant with six `Include`s, then compares each slot of the edited schema against all of them. It runs on every schema edit ([SchemasController.cs](../api/Skoleoverblikket.Api/Controllers/SchemasController.cs), four call sites). That is ~1,000 slots and ~30k comparisons per edit at 800 students. Probably fine, but it's the first thing to measure.
- Other suspects: no explicit `TenantId` indexes in `AppDbContext` (check which tables get one from the FK convention), and the notification fan-out to ~1,300 parents.

## Target school ("Storskolen")

Sizes for the generator. Numbers are per school year.

| Data | Count | Note |
|---|---|---|
| Students | 800 | 0.–9. klasse, 3 spor, ~25 per class |
| Classes | 32 | 30 regular + 2 special classes |
| Parents | ~1,300 | 1.6 per student, some with several children |
| Staff | 100 | ~80 lærere, ~15 pædagoger, admin and leadership |
| Rooms | 80 | Including faglokaler |
| Schema slots | ~1,000 | ~30 lessons per class per week |
| Ugeplan entries | ~40,000 | One per lesson for a full school year |
| Fravær | ~15,000 | ~5% of student-days |
| Messages | ~5,000 threads | Plus class chat |
| SFO | 300 children | 0.–3. klasse |
| Files | 2,000 rows | Metadata only, small blobs |

Plus **20 typical schools** (~300 students each, same proportions) in the same database, so the tenant query filter has to pick one school out of many, like in prod.

## Scenarios

Each one has a pass threshold in k6. Starting targets, to adjust once we see real numbers: **p95 under 300 ms for reads, under 500 ms for a schema edit with conflict check, 0 server errors, under 0.1% failed requests.**

1. **Morning peak (07:45–08:15).** All 100 staff open today's schedule, vikardækning and the ugeplan. 30% of parents open the ugeplan and register fravær within 15 minutes. Mostly reads, the most common real load.
2. **Schema building.** Hanne and two colleagues edit the schema at the same time: place, move and delete slots, each edit followed by the conflict check. One to three users, so this measures latency per edit, not throughput. Also measure with 3 overlapping schemas (current year, next year draft, a variant).
3. **Ferieindmelding evening.** The window opens, a notification goes to all parents, and 60% respond between 19:00 and 21:00 with a peak in the first 30 minutes.
4. **Message to all parents.** One message to ~1,300 recipients: time until the request returns, and time until the email queue is empty in Mailpit.
5. **Soak.** Scenario 1 at half load for 30 minutes. Watch memory, the Npgsql connection pool and the email queue for leaks.
6. **Capacity (Phase 3 only).** Scenario 1 for 1, 5, 10 and 20 large schools at once on the capped stack, until a threshold fails. The answer is "one VPS carries N schools of this size".

## Phases

### Phase 1: generator

- A dev-only console tool, `api/tools/LoadSeed` (references the API project, builds its service provider, sets the tenant per school). It creates entities through the owning services' create methods, the same rule as `TestDataBuilder`, so the data is valid. Going through HTTP like [seed-data.ps1](../scripts/seed-data.ps1) would also work but takes far longer for ~60k rows.
- Deterministic (fixed random seed) so runs are comparable. Danish-looking fake names, no real people.
- Creates one Storskolen by default; `--large-schools N` creates up to 20 for scenario 6, each with its own logins.
- Creates Keycloak users for the logins k6 needs (all 100 staff of Storskolen, ~800 distinct parents to cover the ferieindmelding peak, admins) through the Keycloak admin API, with one shared dev password. The dev realm's `skoleoverblikket-web` client already allows direct grants, which k6 uses to get tokens.
- Refuses to run unless the connection string points at localhost and the environment is `Development`. It must not be possible to point it at prod by mistake.
- A reset story: `scripts/nuke.ps1`, then Aspire, then the generator. Run it in its own worktree or after [72](72-dev-postgres-per-worktree.md), or it overwrites everyday dev data.

### Phase 2: find slow code (Aspire)

- k6 scripts in `loadtest/` (one file per scenario, a shared login helper). k6 is a single binary, free, and fits the Grafana stack we already use. `scripts/loadtest.ps1 -Scenario morning` runs one.
- Turn on `pg_stat_statements` in the dev Postgres config to rank queries by total time. Use the Aspire dashboard traces to count queries per request.
- For every endpoint over target: find the cause, fix it in a separate PR (a new migration for indexes via `/add-migration`), re-run. Expect most of the work in the conflict check and list endpoints without paging.
- Machine speed doesn't matter much here: query counts, rows read and O(n²) loops are the same on a laptop.

### Phase 3: capacity (prod images, capped)

- Run the prod images from `infrastructure/docker/docker-compose.prod.yml` locally, with `cpus` and `mem_limit` set to the prod VPS (and Postgres's real `shared_buffers`, not the dev 128 MB).
- Run scenario 6. Record where it breaks first: API CPU, Postgres, connection pool, Keycloak token endpoint or RAM.
- Keycloak is part of the load: every login is a token request. Check that the morning peak doesn't hit it harder than the API.

### Phase 4: write it down

- A short `docs/PERFORMANCE.md`: target school, thresholds, the numbers per scenario before and after fixes, and how many large schools one VPS carries. Link it from the AGENTS.md docs table.
- Fixes that weren't done in this task become their own tasks in [INDEX.md](INDEX.md).
- Not in CI for now. Re-run by hand before a VPS change ([54](54-move-vps.md)) and before signing a school over ~500 students. A nightly run can come later if the numbers turn out tight.

## Decisions (Niels, 2026-11-21)

1. **Prod VPS**: 4 vCPU, 8 GB RAM, shared by the API, Postgres, Keycloak and the backup agent. Phase 3 caps the whole compose stack to that total, not each container.
2. **Peaks are estimates.** There are no real users yet, so Loki has no traffic to measure. Keep the scenario numbers above and re-check them once real schools use the product.
3. **Thresholds confirmed.** 300 ms (reads) and 500 ms (schema edit) are hard limits. The schema builder should feel instant, so treat anything over 150 ms p95 on a schema edit as worth a look even when it passes.

## Done when

- The generator builds Storskolen plus 20 typical schools on a clean stack in under 10 minutes, and refuses to run against anything that isn't local.
- Scenarios 1–5 pass their thresholds on Aspire, or every failure has a fix merged or a task filed.
- Scenario 6 has a number: how many large schools one prod-sized VPS carries.
- `docs/PERFORMANCE.md` records the results.

---
title: 'Stå mål med — point-in-time coverage snapshots'
purpose: 'Let admins save a persisted copy of the live subject-hour coverage view so a tilsynsførende can review coverage as it stood earlier in the skoleår, not just today.'
description: >-
  New feature — admin-triggered "gem øjebliksbillede" action stores a
  serialized copy of StaaMaalMedController's coverage computation, tagged
  with skoleår and a reason, plus read endpoints to list/retrieve past
  snapshots. Storage and retrieval only; no PDF export, no scheduling, no
  UI beyond a simple list-and-detail view.
status: 'Proposed'
---

# Stå mål med — point-in-time coverage snapshots

## TL;DR

New `StaaMaalMedSnapshot` entity storing a serialized copy of the coverage
computation (per class, per subject, hours + status), tagged with skoleår,
a timestamp, a reason string, and who created it. Admin-triggered only —
a "gem øjebliksbillede" button on the coverage page — no scheduling/cron in
this task. New endpoints to create, list, and retrieve a single snapshot,
`[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]` matching
`StaaMaalMedController`. New EF Core migration required (not written here).
Scope is storage + retrieval only.

## Context

`GET /api/v1/staa-maal-med/coverage`
(`api/Skoleoverblikket.Api/Controllers/StaaMaalMedController.cs`) computes
green/yellow/red/missing coverage per subject category per class entirely
live, off whichever `Schema`/`SchemaSlot` rows are active for "today"
(`DateOnly.FromDateTime(DateTime.UtcNow)`, lines 25–32). Nothing about that
computation is persisted — every request recomputes from current state.

This is a problem for the annual tilsyn workflow described in
[19-staa-maal-med.md](19-staa-maal-med.md#5-tilsynssystemet-kontekst-for-feature-positionering):
an ekstern tilsynsførende visits at least once a year and needs to assess
coverage across the whole skoleår, not just the moment they happen to load
the page. If a schema changes mid-year — a course gets recategorized, a
class's slots get edited, a new schema replaces an old one — there is no
record of what the coverage picture looked like before the change. There is
also no way to answer "show me the coverage report for skoleår 2025/26"
after that skoleår has ended and its schemas are no longer the active ones
`StaaMaalMedController` would pick up.

This task is scoped narrowly: give admins a way to freeze and later retrieve
a copy of the coverage view. It is explicitly **not**
[task 20 (Fase 3)](../20-staa-maal-med-fase3.md) — task 20 is about storing and
publishing the school's own undervisningsplaner/mål under Friskoleloven §1a
(`TeachingPlan`, `TeachingGoal`, `CompliancePath`), a much larger and
separate feature. This task only snapshots the existing structural
hour-coverage computation; it adds no new compliance data model.

Skoleår is already a first-class string elsewhere in the codebase:
`UvmTimetableService.Load()`
(`api/Skoleoverblikket.Api/Services/UvmTimetableService.cs:41-43`) derives
`"{startYear}-{startYear + 1}"` (e.g. `"2025-2026"`) from
`now.Month >= 8 ? now.Year : now.Year - 1`, matching the
`Data/uvm-timetal/2025-2026.json` file naming convention. The snapshot's
`SchoolYear` column stores this string form directly (`"2025-2026"`), reusing
`UvmTimetableService`'s exact derivation so it lines up with which UVM
timetal file was used to compute the frozen numbers. Task 20's
`TeachingPlan`/`CompliancePath` key on `int SkoleaarStartYear` instead
(`2025` for `"2025-2026"`) — the two representations carry the same
information, so any query needing to join snapshot and plan data for "the
same skoleår" converts at that single boundary (`SchoolYear.Split('-')[0]`
↔ `SkoleaarStartYear`), never compares the string and int forms directly.

## Proposed scope

1. **Data model**
   - `StaaMaalMedSnapshot`: `Id`, `TenantId`, `SchoolYear` (string, e.g.
     `"2025-2026"`, same format as `UvmTimetableService`), `CreatedAt`,
     `CreatedByStaffId` (who triggered it), `Reason` (short free-text, e.g.
     "Manuelt gemt før tilsynsbesøg" — admin-entered or a sensible default),
     `DataVersion` (int, **required**, not nullable — every snapshot is
     stamped with the `CoverageResponseDto` schema version it was written
     against, starting at `1`; a nullable/optional version field would let
     the first-ever snapshot ship without one, which then has no way to be
     told apart from "version unknown" once a `2` exists), `Data` (serialized
     copy of `CoverageResponseDto` shaped per `DataVersion` — likely
     `jsonb`/`json` column; exact serialization shape is an implementation
     detail for whoever picks this up, but it should be enough to
     reconstruct the same per-class, per-subject, hours + status table the
     live view shows). If `CoverageResponseDto` changes shape later, bump
     `DataVersion` and add a version-specific deserializer/migrator on read
     so old rows (`DataVersion == 1`) stay reconstructable rather than
     silently failing to parse against the new shape.
   - Tenant-scoped via the standard EF Core global query filter
     (`HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)`) — never
     bypass it, per `AGENTS.md`.
   - Retention: kept indefinitely, no auto-purge — consistent with this
     being a small friskole's occasional audit record, not a high-volume
     table (a handful of snapshots per skoleår at most). Admin can manually
     delete a snapshot (`DELETE /snapshots/{id}`, `[Authorize(Roles =
     Roles.Admin)]`); Board keeps read-only access to list/retrieve, same as
     the rest of stå-mål-med, but cannot delete.
   - Needs a new EF Core migration (new table). Not written as part of this
     task — use `/add-migration` when implementing.

2. **Backend** — extend `StaaMaalMedController`
   (`api/Skoleoverblikket.Api/Controllers/StaaMaalMedController.cs`) or add
   a sibling controller under the same `api/v1/staa-maal-med` route prefix:
   - `POST /snapshots` (`[Authorize(Roles = Roles.Admin)]` — snapshot creation
     is admin-triggered only, per the TL;DR and "gem øjebliksbillede" button;
     Board stays read-only same as the rest of stå-mål-med) — runs the same
     coverage computation `GetCoverage` already does, serializes the result,
     stamps it with the derived `SchoolYear`, `CreatedByStaffId` (resolved
     from the authenticated user, not a client-supplied parameter), and an
     optional `Reason` from the request body. Persists a new
     `StaaMaalMedSnapshot` row. Returns the created snapshot (id + metadata,
     not necessarily the full payload).
   - `GET /snapshots` — lists snapshots for the tenant, newest first,
     summary fields only (id, skoleår, created at, created by, reason) —
     not the full serialized payload, to keep the list endpoint light.
   - `GET /snapshots/{id}` — returns one snapshot's full stored payload in
     the same shape as `CoverageResponseDto`, so the frontend can reuse the
     existing coverage table rendering against historical data.
   - Reuse `CoverageResponseDto`/`ClassCoverageDto`/`SubjectCoverageDto`
     record shapes (or a versioned equivalent) for the serialized payload so
     the frontend doesn't need a second rendering path for live vs.
     snapshotted data.

3. **Frontend**
   - A "Gem øjebliksbillede" button on the existing stå-mål-med coverage
     page, calling the new `POST /snapshots` endpoint. Simple confirmation
     toast on success — no configuration dialog beyond an optional reason
     field.
   - A new simple page (or a tab/section on the existing page) listing past
     snapshots — date, skoleår, who created it, reason — that links into a
     detail view reusing the existing coverage table component against a
     single snapshot's stored data.
   - Regenerate the typed API client (`/codegen`) after controller/DTO
     changes, per `AGENTS.md`.

## Open questions

- **Auto-snapshot at all**: this task commits to manual/admin-triggered
  snapshots only, per `AGENTS.md`'s simplicity-first principle — a school
  secretary clicking a button before a tilsyn visit is simpler to build,
  explain, and reason about than a scheduler. Whether an automatic
  end-of-skoleår snapshot (or periodic snapshots during the year) is worth
  adding later should only be decided if a real school or tilsynsførende
  asks for it — not committed scope here.
- **Diffing between snapshots**: showing what changed between two snapshots
  (or between a snapshot and the live view) is not scoped in. If tilsyn
  review turns out to need "what changed since last snapshot," that's a
  follow-up task, not part of this one.
- **PDF/export of a snapshot**: out of scope per the task brief. If a
  tilsynsførende wants a printable report, that's a separate, later task
  (and would naturally build on the stored `Data` payload here).

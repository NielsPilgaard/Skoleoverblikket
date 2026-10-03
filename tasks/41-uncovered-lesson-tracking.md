---
title: 'Uncovered lesson tracking'
purpose: 'Surface, per class/subject, how many scheduled lektioner went uncovered when the assigned teacher/aide was absent and no vikar was assigned.'
description: >-
  New read-only signal — derives "uncovered lesson" events from task 38's
  proposed StaffAbsence/VikarAssignment model (affected SchemaSlot rows with
  no matching VikarAssignment) and surfaces a count per class/subject
  alongside the existing StaaMaalMedController coverage view. No new storage,
  no alerts, no admin workflow — a structural indicator only.
status: 'Proposed'
---

# Uncovered lesson tracking

## TL;DR

Today, `VikarController` assignment is entirely opt-in — nothing records whether a lektion that lost its teacher/aide to absence actually got a substitute, or simply went untaught. This task adds a **derived, read-only count** of such "uncovered lessons" per class/subject, computed from task 38's proposed `StaffAbsence`/`VikarAssignment` model (a `StaffAbsence`-affected `SchemaSlot` with no matching `VikarAssignment` on that date = uncovered), and surfaces it next to the existing `StaaMaalMedController` `/coverage` view. No new table, no alerts, no "mark as made up" workflow — just a count, framed strictly as a structural indicator, never a compliance verdict.

**Prerequisite met: task 38 is built** ([38-teacher-report-abscence.md](completed/38-teacher-report-abscence.md)). It shipped differently from the proposal below, so read this first:

- `StaffAbsence` exists as proposed (`Models/StaffAbsence.cs`, owned by `StaffAbsenceService`).
- There is **no `VikarAssignment` table**. Vikar cover per lektion and date is stored on the existing `WeekPlanSlot.SubstituteTeacherId` / `SubstituteAideId` (one `WeekPlanSlot` per klasse, ISO week and `SchemaSlot`, which is one date). Teacher and aide seats are covered separately.
- Affected lektioner and their cover: `StaffAbsenceService.GetDetailAsync` (`GET /api/v1/staff-absences/{id}`), which lists each affected `(Date, SchemaSlotId, Seat)` with its current substitute. Derive "uncovered" from that: an affected seat on a past school day with no substitute on the matching `WeekPlanSlot`.

The sections below still describe the original proposal.

## Context

### The gap

`VikarController` (`api/Skoleoverblikket.Api/Controllers/VikarController.cs`) lets an admin look up free/busy staff for a slot (`GET /api/v1/staff/available`) and manually assign a substitute onto a `WeekPlanSlot` (`PUT .../substitute`). Assignment is entirely manual and opt-in — there is no concept of "this slot needed a substitute." Nothing records the outcome when a teacher or aide is out: did a vikar get assigned, or did the lesson simply go uncovered/cancelled?

`StaaMaalMedController` (`api/Skoleoverblikket.Api/Controllers/StaaMaalMedController.cs`) computes subject-hour coverage (`GET /api/v1/staa-maal-med/coverage`) entirely from the *scheduled* timetable template — `SchemaSlot` rows on schemas active today, grouped by class and `Course.Category`, compared against UVM's vejledende timetal. A slot that is scheduled every week counts as taught even if the teacher was out and no vikar was ever found. The view has no way to distinguish "dansk was fully scheduled and actually delivered all year" from "dansk was scheduled but silently went uncovered 6 times."

This matters for §1a of Friskoleloven (see `tasks/completed/19-staa-maal-med.md`, section 1): binding condition 3 requires "klar sammenhæng mellem mål/planer og den faktiske undervisning" — a clear connection between goals/plans and *actual* teaching, not just the scheduled template. Condition 4 (connection between goals and student learning outcomes) is also weakened by chronic uncovered lessons in a subject. A class with repeated uncovered lektioner in dansk undermines the "faktisk undervisning" the law requires, and today's coverage view has no visibility into that at all.

### Dependency on task 38

`tasks/38-teacher-report-abscence.md` proposes (not yet built):

- `StaffAbsence` — staff self-reports absence over a date range (or admin files on their behalf); no confirm/dismiss, self-report is final.
- `VikarAssignment` — one row per `(Date, SchemaSlotId)`, pointing at `AssignedStaffId` + `StaffAbsenceId`. Unique index on `(TenantId, SchemaSlotId, Date)`.
- `GET /api/v1/staff-absence/{id}/affected-slots` — resolves the `SchemaSlot` rows affected by a given absence (Teacher/Aide = absent staff, Weekday falls in the absence's date range), with ranked substitute candidates.

This task's entire premise — "was an affected slot covered or not" — depends on that affected-slots resolution and the `VikarAssignment` table existing. **If task 38 is not implemented, there is no data to derive an uncovered-lesson signal from, and this task cannot begin.** Treat task 38 as a hard prerequisite, not a nice-to-have dependency.

## Proposed scope

### Defining "uncovered lesson"

An uncovered lesson is an occurrence of: a `SchemaSlot` was affected by a `StaffAbsence` (per task 38's `affected-slots` resolution, for a specific `Date` within the absence's range) **and** no `VikarAssignment` exists for that `(SchemaSlotId, Date)` pair, evaluated only after that `Date` has passed (a future-dated absence with no vikar yet assigned is simply not counted yet — see the "already passed" query restriction below). One `VikarAssignment` on a `(SchemaSlotId, Date)` covers the whole lesson regardless of which role (teacher or aide) was originally absent — see the coverage-unit note under Open Questions for why this matches task 38's one-row-per-slot-per-date model.

### Data/query approach — two options, recommend (a)

**(a) Derived/computed view (recommended).** No new storage. Query `StaffAbsence` rows, expand each to its affected `(SchemaSlot, Date)` pairs (same resolution logic task 38's `affected-slots` endpoint already implements), **deduplicate the expanded pairs** (two overlapping `StaffAbsence` rows — e.g. both the teacher and the aide out the same day — can each expand to the same `(SchemaSlotId, Date)`; without dedup that single lesson would be double-counted as two uncovered occurrences), then left-join the deduplicated set against `VikarAssignment` on `(SchemaSlotId, Date)`, and count pairs with no match, restricted to dates that have already passed. Group by `SchemaSlot.Schema.ClassId` and `SchemaSlot.Course.Category` to produce a per-class/per-subject count.

- Pro: no new table, no risk of the signal drifting out of sync with the underlying `StaffAbsence`/`VikarAssignment` data (there is only one source of truth). Consistent with AGENTS.md's "simplicity first" and avoiding unnecessary abstractions.
- Con: the affected-slots resolution logic (weekday/date-range matching) has to be shared or reimplemented between `StaffAbsenceController` and wherever this count is computed — needs to live in one shared service, not copy-pasted, to avoid the two going out of sync.

**(b) Explicit status field.** Add a status (e.g. `Covered` / `Uncovered` / `MadeUp`) directly on `VikarAssignment`, or a new row type representing "this affected slot was explicitly marked uncovered." Would let an admin explicitly flag "ikke dækket" for a slot rather than relying purely on absence-of-a-row.

- Pro: supports an explicit "mark as uncovered" admin action (e.g. when a lesson was cancelled outright, not superseded by a vikar) without relying on time-based inference.
- Con: new field/table to keep in sync; another thing that can silently drift from reality if an admin forgets to set it. Not needed for a read-only count — the derived query already covers the common case (no vikar assigned by the time the date passes).

**Recommendation: (a).** Start with the pure derived query. If a real need emerges for admins to explicitly mark a covered-looking slot as "actually didn't happen" (e.g., vikar assigned but lesson still cancelled for another reason), that's a distinct future enhancement, not part of this task's initial scope.

### Backend

- Extract the affected-slots resolution logic from task 38's `StaffAbsenceController` into a shared service (e.g. `IAffectedSlotResolver` or similar) covering date, weekday, role, and slot matching — do not leave it in the controller and do not reimplement it a second time for the coverage query. Both the `affected-slots` endpoint and the uncovered-lesson count use this one shared implementation.
- New query/service method: given the current tenant, return uncovered-lesson counts grouped by `(ClassId, Course.Category)`, restricted to `StaffAbsence` dates that have already passed (no point counting a future-dated absence with no vikar yet — the vikar may still be assigned before the date arrives) and further restricted to dates within the skoleår boundary used by `StaaMaalMedController.GetCoverage`'s active schema period (the same `Schema.StartDate`/`EndDate` window the coverage computation already uses for the class) — an absence from a prior skoleår must never contribute to the current period's count.
- Extend `StaaMaalMedController`'s `GetCoverage` response to include this signal. Concretely: add an `UncoveredLessonCount` (int) to `SubjectCoverageDto`, matching the DTO's existing grouping exactly. `ClassCoverageDto.UnexpectedGradeCategories` (categories taught at a grade that UVM doesn't define for it, e.g. Tysk in 3. klasse) is a separate `List<string>` with no `SubjectCoverageDto` row at all — uncovered lessons in an unexpected category would otherwise be silently dropped from the response. Keep `UnexpectedGradeCategories` as-is (string list, unchanged JSON/OpenAPI contract for existing v1 consumers) and add a new, separately-named field (e.g. `UnexpectedGradeCategoryDetails: List<UnexpectedCategoryDto>` carrying `(Category, UncoveredLessonCount)`) populated alongside it, so the uncovered-lesson signal reaches unexpected categories without breaking existing consumers of the string list. `CoverageResponseDto` needs no structural change beyond what flows through from the extended `SubjectCoverageDto` and the new details field.
- Tenant scoping via `ITenantContext` per AGENTS.md — the uncovered-lesson query must filter `StaffAbsence`/`VikarAssignment`/`SchemaSlot` through the same tenant boundary as every other query; never accept a tenant identifier as a parameter, resolve from context.
- `[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]` — same authorization as the rest of `StaaMaalMedController`, since this is additive data on the same endpoint.

### Frontend

- Wherever the `/coverage` response is rendered (the stå-mål-med dashboard page), show the uncovered-lesson count alongside each subject's existing green/yellow/red/missing status — e.g. a small annotation like "3 uncovered dansk-lektioner denne skoleår" next to the dansk row for a given class.
- No new page. This is an additive field on an existing view.
- Regenerate typed client (`/codegen`) after the `SubjectCoverageDto` change.

### Framing (must follow)

Per task 19 section 4's explicit warning: never present this as a compliance verdict. The count is a structural indicator only.

- Correct: "3 uncovered dansk-lektioner denne skoleår."
- Wrong: "Skolen opfylder ikke stå mål med-kravet i dansk."

Apply the same rule to any UI copy, tooltip, or label introduced here.

## Out of scope (flag as future enhancements, not committed)

- Automatic alerts or notifications when an uncovered-lesson count crosses some threshold.
- Any admin workflow to retroactively mark an uncovered lesson as "made up" (e.g. extra lektion added later to compensate). This would likely require option (b)'s explicit status field or a new "make-up lesson" concept — deliberately deferred.
- Explicit "mark as ikke dækket" admin action for a slot that technically has a `VikarAssignment` but the lesson still didn't happen for some other reason (vikar no-show, etc.).
- Any change to `StaaMaalMedController`'s existing green/yellow/red/missing status computation — the uncovered-lesson count is purely additive, not a modifier of the existing hour-based status.

## Open questions

- Does a `VikarAssignment` created *after* the date has passed (admin backfills a substitute assignment retroactively) still resolve the slot as covered? Current assumption: yes — the query checks for existence of a matching `VikarAssignment` row regardless of when it was created, not just ones created before the date. Worth confirming once task 38's actual `POST /assign` semantics are finalized.
- **Coverage unit decision**: one `VikarAssignment` on a `(SchemaSlotId, Date)` marks that whole lesson covered, regardless of which role (teacher or aide) was originally absent — matches task 38's model of one `VikarAssignment` row per `(SchemaSlotId, Date)`. If task 38 is later built with per-role assignment (e.g. a slot needing both a substitute teacher and aide requires two separate rows), this task's coverage query must be revisited — a slot with only one of the two roles covered would currently read as fully covered, which may understate real gaps. Not a blocker to start with the simpler whole-lesson model; flagged for whoever implements task 38 to confirm its actual row shape first.

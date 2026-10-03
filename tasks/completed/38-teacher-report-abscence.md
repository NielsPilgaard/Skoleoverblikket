---
title: 'Staff absence reporting + vikar assignment'
purpose: 'Let staff report their own absence and let admin assign a substitute (vikar) per affected lektion.'
description: >-
  New feature — staff self-reports absence for a date range, admin sees
  which SchemaSlot lektioner are affected and assigns a vikar per slot from
  an auto-suggested availability list. Separate domain from the existing
  parent→student AbsenceReport feature; no model reuse.
status: 'Completed'
---

# Staff absence reporting + vikar assignment

## TL;DR

New `StaffAbsence` (staff reports self absent, date range + reason, no
confirm/dismiss — self-report is final) and `VikarAssignment` (date +
SchemaSlot + assigned Staff, one row per affected lektion) models. Admin can
also file a `StaffAbsence` on behalf of another staff member. For each
affected `SchemaSlot` (Teacher or Aide = absent staff, Weekday matches date
range), admin gets an availability-ranked candidate list and assigns a vikar
per slot. Assigned vikar is notified; affected schema views show the
substitution inline for that date without touching the underlying recurring
`SchemaSlot` template.

## Context

`SchemaSlot` (`api/Skoleoverblikket.Api/Models/SchemaSlot.cs`) is a
**recurring weekly template** — Weekday + TimeSlot → Course + Teacher +
optional Room/Aide, no date field, shared across every week the schema is
active. There is no per-week instance table. This means a date-specific
substitution cannot mutate `SchemaSlot` directly (would break every other
week) — it must live in a new overlay table keyed by date.

The existing `AbsenceReport` model (`AbsenceController.cs`,
`AbsenceReport.cs`) is a different feature: parent reports a *student's*
absence, admin confirms/dismisses. Do not extend or reuse it — different
actor (staff vs. parent), different subject (self vs. student), different
downstream effect (vikar assignment vs. simple confirm).

`Staff.Role` (`StaffRole` enum) already has `Teacher`, `Aide`, `Substitute` —
`Substitute` is just a role label, not an eligibility gate; any staff member
can be assigned as vikar.

## Decisions (confirmed)

- **Downstream effect**: full vikar workflow — report triggers a list of
  affected lektioner, admin assigns a substitute per slot.
- **Reporter**: staff reports own absence; admin can also file on behalf of
  another staff member (phone-in-sick case).
- **Approval**: self-report is final — no confirm/dismiss step, unlike
  `AbsenceReport`.
- **Data model**: new `StaffAbsence` + `VikarAssignment` tables, not an
  extension of `AbsenceReport`.
- **Assignment scope**: `VikarAssignment` row per (Date, SchemaSlotId),
  pointing at `AssignedStaffId` + `StaffAbsenceId`. Original `SchemaSlot`
  untouched.
- **Vikar eligibility**: any `Staff` row, any role.
- **Candidate suggestion**: ranked list, not free-text pick.
  - Rank 1: `Staff.Role == Substitute` first, then others.
  - Exclude staff busy at that Weekday+TimeSlot: busy = has own `SchemaSlot`
    as Teacher or Aide at that Weekday+TimeSlot (any class), OR already has
    a `VikarAssignment` at that Date+TimeSlot from a different absence, OR
    has an active `StaffAbsence` covering that date themself.
  - Exclude the absent staff member themself.
- **Notification + display**: assigned vikar gets notified via
  `INotificationService` (new `NotificationType.VikarAssigned`). Schema
  views (staff + parent) show the substitution inline for that date —
  overlay `VikarAssignment` on top of `SchemaSlot` at render time, template
  itself stays unmodified.

## Proposed scope

1. **Data model**
   - `StaffAbsence`: `Id`, `TenantId`, `StaffId` (absent staff),
     `ReportedByStaffId` (self or admin who filed it), `Date`, `EndDate?`,
     `Reason?`, `CreatedAt`. No status enum — self-report is final.
   - `VikarAssignment`: `Id`, `TenantId`, `StaffAbsenceId`, `SchemaSlotId`,
     `Date` (the specific date within the absence range this covers),
     `AssignedStaffId`, `CreatedAt`. Unique index on
     `(TenantId, SchemaSlotId, Date)` — one vikar per slot per date.
   - New EF Core migration via `/add-migration`.

2. **Backend** — new `StaffAbsenceController` (`api/v1/staff-absence`)
   - `POST /` — staff reports own absence, or admin reports for any staff
     (`[Authorize]`, role check inline: self or `Roles.Admin`).
   - `GET /mine` — staff's own reported absences.
   - `GET /` (`Roles.Admin`) — all absences, filterable by date range.
   - `GET /{id}/affected-slots` (`Roles.Admin`) — resolves `SchemaSlot` rows
     where `TeacherId`/`AideId` = absent staff and `Weekday` falls in the
     absence's date range; for each, returns availability-ranked candidate
     list per the rules above.
   - `POST /{id}/assign` (`Roles.Admin`) — body: `SchemaSlotId`, `Date`,
     `AssignedStaffId`. Validates candidate isn't busy (re-check server-side,
     don't trust client-supplied list). The availability re-check and the
     `VikarAssignment` insert must be atomic against concurrent admins
     assigning the same staff member to overlapping slots — a database lock,
     serializable transaction with retry, or an equivalent constraint, not a
     plain check-then-write. Creates `VikarAssignment`, notifies assigned
     staff.
   - `DELETE /vikar-assignment/{id}` (`Roles.Admin`) — unassign.
   - Tenant scoping via `ITenantContext` per `AGENTS.md` — never bypass
     global query filter.

3. **Frontend**
   - Staff-facing: "Meld fravær" action (own schedule page or new small
     page) — date range + reason form, list of own past reports.
   - Admin-facing: new page listing staff absences, drill into affected
     lektioner, candidate dropdown per slot (ranked list from
     `affected-slots`), assign/unassign.
   - Schema/schedule views: overlay vikar substitution for the viewed date
     (e.g. "Vikar: [Name] i dag i stedet for [Teacher]") — needs the
     date-aware views to fetch `VikarAssignment` for the visible date range
     and merge with `SchemaSlot` template client-side or via an enriched
     endpoint.
   - Regenerate typed client (`/codegen`) after controller/DTO changes.

## Open questions

- Which existing schema/schedule view(s) need the vikar overlay? Likely the
  staff personal schedule view and the class schema view — needs a look at
  current schedule-rendering endpoints/components to decide where the
  overlay merge happens (API-side enrichment vs. client-side merge of two
  responses).
- Should `StaffAbsenceController` reuse `EditClassRequirement`-style
  authorization anywhere, or is plain `Roles.Admin` sufficient since this
  isn't scoped to a class? Current assumption: plain `Roles.Admin`, no
  per-class permission check, since absence/vikar assignment is a
  whole-school admin action, not a class-editor action.

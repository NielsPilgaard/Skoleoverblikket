---
title: 'Extract inline controller logic into services'
purpose: 'Move business logic currently embedded in Controllers/ action methods into dedicated service classes, matching the existing thin-controller pattern, so agents and humans can read/modify logic without wading through HTTP plumbing.'
description: >-
  A survey of api/Skoleoverblikket.Api/Controllers/ found ten controllers with
  non-trivial business logic (multi-step orchestration, raw EF/SQL queries,
  domain algorithms) inline in action methods, instead of delegated to a
  service class as INotificationService, ConflictDetectionService,
  SubscriptionService, and the invitation services already do. This task
  extracts that logic, prioritized by severity and by cross-controller
  duplication (FilesController/BoardFilesController and the repeated
  "resolve current user" logic are the highest-value extractions).
status: 'Proposed'
---

# Extract inline controller logic into services

## TL;DR

Ten controllers hold business logic that belongs in a service: CSV import engine, message fan-out/recipient resolution, presigned-upload crypto, slot-generation/backup algorithms, recurrence/holiday math, dashboard aggregation, and more. The codebase already has an established pattern — `INotificationService`, `ConflictDetectionService`, `SubscriptionService`, `ExcelReportBuilder`, `UvmTimetableService`/`SchoolWeekCalculator`, `ParentInvitationService`, `StaffInvitationService`, `BoardMemberInvitationService` — these ten controllers just haven't been brought in line with it. Two items are flagged as highest-value because they eliminate cross-file duplication, not just inline-in-one-file mess: `FilesController`/`BoardFilesController` (near copy-pasted presign/quota/folder-delete logic) and a repeated "resolve current parent/staff from Keycloak subject" block duplicated across four controllers.

No behavior change intended anywhere in this task — pure extraction/refactor. Each item should ship as its own PR so review stays scoped to one service at a time.

## Context

`AGENTS.md` establishes the target shape: controllers are thin, tenant-scoped via `ITenantContext`, and delegate business logic to services. Most of the codebase follows this. A survey of `api/Skoleoverblikket.Api/Controllers/` (2026-08-26) found ten controllers that don't — see the table below for specifics. Motivation is maintainability and AI-agent readability specifically: an agent asked to modify "how CSV import matches existing students" currently has to read a 750-line controller method mixing HTTP concerns with matching logic; the fix is the same shape as every other service in this codebase — extract, test via the public endpoint per `docs/TESTING.md` (still no testing private methods), keep the controller as a thin caller.

## Proposed scope

Work items, in recommended order (highest value / most duplication first). Each is independently shippable.

### 1. `FileUploadService` — dedupe `FilesController` + `BoardFilesController`

`api/Skoleoverblikket.Api/Controllers/FilesController.cs` and `BoardFilesController.cs` independently implement near-identical logic: HMAC presigned-upload token signing/verification (`Presign`, `Confirm`), storage quota calculation, and recursive folder deletion (`DeleteFolder`, `CollectDescendantFolderIdsAsync`). Extract one shared `FileUploadService` (or `IFileUploadService`) covering token sign/verify, quota check, and recursive folder-descendant resolution; both controllers call it. Confirm the two implementations are actually equivalent before merging — check for any drift (e.g. different quota rules for board files vs. general files) and preserve it as a parameter, not a silently dropped behavior.

### 2. `ICurrentUserResolver` — dedupe "resolve current user" across 4 controllers

The "resolve current parent/staff from Keycloak subject claim" block is repeated near-verbatim in `MessagesController`, `ContactThreadsController`, `AbsenceController`, and `VacationRegistrationController`. Extract to a shared `ICurrentUserResolver` (or extend an existing auth-adjacent service if one fits better) returning the resolved Parent/Staff entity or a not-found result. All four controllers call it instead of repeating the lookup.

### 3. `ImportService` — `ImportsController.cs` (lines 48-738)

Largest single offender: `ImportStudentsAndParents`, `ImportStaff`, `ImportRooms`, `ImportClasses`, `ImportBoardMembers` implement a full CSV upsert/dedup engine inline — class/student/parent matching, email validation, role/bool parsing, ambiguous-name detection — with no service at all. Extract per-entity import logic into an `ImportService` (or one service per entity type if that reads cleaner — judge during implementation) with each controller action reduced to: parse request, call service, return per-row result/warnings.

### 4. `MessagingService` — `MessagesController.cs` (lines 184-346, 559-728)

`GetSent`, `GetThread`, `SendGroupMessage`, `ResolveRecipients`, `BuildAllRecipientsAsync` mix raw SQL/CTE queries, thread-root walking, group-message recipient fan-out, BCC email batching, and notification orchestration inline. Extract query/orchestration logic to a `MessagingService`; controller keeps request validation and response shaping.

### 5. `TimeSlotTemplateService` — `TimeSlotsController.cs` (lines 85-394)

`UpsertTemplate`, `RestoreTemplate`, `CreateBackupAsync`, `GenerateSlotsFromTemplate`, `ValidateBreaksAgainstModules` implement the slot-generation algorithm, break/module-boundary validation, and S3 JSON backup/restore (with transaction) inline. Extract to a service; the S3 backup/restore piece in particular is exactly the kind of thing that should be independently testable without an HTTP round-trip.

### 6. `SchoolCalendarService` — `CalendarController.cs` (lines 216-376)

`ExpandRecurrence`, `ComputeDefaultHolidays`, `ComputeEaster` are pure domain algorithms (RRULE-style recurrence expansion, Gregorian Easter calculation, Danish school-holiday derivation) with zero HTTP dependency — ideal extraction candidate, should be closer to a pure static/stateless service than most others on this list.

### 7. `DashboardStatsService` — `StatsController.cs` (lines 33-126)

`GetDashboard` does dense multi-entity aggregation (hours-per-course/staff grouping, unassigned-slot gap calculation) inline. Extract to a service method returning the dashboard DTO.

### 8. `WeekPlanCoverageService` (or fold into existing) — `WeekPlanController.cs` (lines 100-204, 390-414)

`GetWeekPlan`, `IsFullWeekCovered` compute holiday-week coverage and fag-swap/substitute projection inline in DTO mapping. Extract the computation; keep DTO mapping in the controller.

### 9. `VikarController.cs` (lines 61-144) — reuse `ConflictDetectionService`, don't extract new

`GetAvailable` independently reimplements staff availability/overlap logic that `ConflictDetectionService` already provides. This item is "call the existing service" not "write a new one" — smallest task on this list, do it early if looking for a quick win alongside item 6.

### 10. `ContactThreadsController.cs` (lines 211-239, 480-535)

`GetRelevantStaffIdsAsync` (union of teacher/aide/permission staff for a class) and `SendNotificationsCoreAsync` (per-recipient notification loop) — check for overlap with `MessagingService` (item 4) once that exists; these may become one shared recipient-resolution service rather than two.

### 11. `StaaMaalMedController.cs` (lines 136-251) — lowest priority

`ComputeCoverageAsync` does green/yellow/red threshold calculation inline, but already reuses `UvmTimetableService`/`SchoolWeekCalculator` correctly. Lowest priority on this list — extract the threshold-calculation piece to a service method only if touching this file for another reason (e.g. task 41's uncovered-lesson-count addition); not urgent standalone.

## Out of scope

- Any behavior change. This is pure extraction — inputs/outputs of every affected endpoint must be identical before and after. If a bug is found during extraction, fix it in a separate, clearly-labeled commit/PR, not silently folded into the refactor.
- Rewriting DTOs, changing API contracts, or touching the OpenAPI spec beyond what naturally regenerates from unchanged endpoint signatures.
- `SchemasController`, `BillingController`, `StripeWebhookController`, `ReportsController`, `AbsenceController`, `ClassesController`, `StaffController`, `SfoWeekPlanController`, `VacationRegistrationController` — surveyed and already thin/service-backed, no action needed.

## Open questions

- Item 1 (`FileUploadService`): needs confirmation that `FilesController` and `BoardFilesController` quota/permission rules are actually identical before merging — if board files have different quota semantics, the shared service needs a parameter, not an assumption of sameness.
- Items 4 and 10 may end up sharing a recipient-resolution service once both are looked at together — don't commit to two separate services until item 4 is done and item 10's overlap is confirmed.
- Whether per-item work should be one PR each (recommended, per TL;DR) or batched — default to one PR per numbered item unless a reviewer asks for batching.

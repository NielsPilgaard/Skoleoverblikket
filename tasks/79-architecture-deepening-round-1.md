---
title: 'Architecture deepening, round 1'
purpose: 'Checklist for the refactors found in the 2026-11-17 architecture review of the most-changed code.'
description: >-
  Moves business rules out of the controllers and pages that changed most since
  2026-09: ugeplan, the school calendar (holidays and "today"), files, SFO, the
  parent–student link and the web week helpers. Each phase gives one service
  or module sole ownership of its rules, following the AGENTS.md encapsulation
  section. Also fixes a probable bug where the file browser calls both file APIs.
status: 'Ready'
---

# Architecture deepening, round 1

## TL;DR

- Six phases from the 2026-11-17 review, plus a quick bug fix (Phase 0). Each phase is its own PR and can ship alone, except Phase 6, which goes with Phase 1 or 2.
- Order: 0 → 1 → 2 → 3 → 4 → 5 → 6. Phase 1 is the hottest code (WeekPlanPage 21 commits, WeekPlanController 10 since 2026-09-01).
- Size L in total. Owner: Agent. One decision for Niels: the school-year start rule in Phase 2.
- No behaviour change for users, except the bugs listed in Phases 0, 2 and 4. No migrations expected; if one turns up, it needs the `migration-reviewed` label from Niels.

Every phase follows [AGENTS.md](../AGENTS.md) "Encapsulation": thin controllers, one sealed service per feature that is the only writer of its entities, `TestDataBuilder` goes through the owning service. Every phase ends with `/verify` and `/test`, and updates [docs/FEATURES.md](../docs/FEATURES.md) when a controller → service mapping changes.

## Phase 0: File browser fetches both file APIs (S)

[FileSystemBrowser.tsx](../web/src/components/files/FileSystemBrowser.tsx) runs `staffQuery` and `boardQuery` on every load (around lines 708–723), so staff call `/board-files` and board members call `/files`. Board members likely get a 403 on every page view.

- [ ] Add `enabled: variant === 'staff'` to `staffQuery` and `enabled: variant === 'board'` to `boardQuery`.
- [ ] Check in the browser (network tab) that `/filer` calls only `/files` and the board files page calls only `/board-files`.
- [ ] Extend the board files e2e test so it fails if `/api/v1/files` is requested.

## Phase 1: WeekPlanService owns the ugeplan (M)

Today three places write `WeekPlanSlot`: [WeekPlanController.cs](../api/Skoleoverblikket.Api/Controllers/WeekPlanController.cs), [WeekPlanService.cs](../api/Skoleoverblikket.Api/Services/WeekPlanService.cs) and [SubstituteService.cs](../api/Skoleoverblikket.Api/Services/SubstituteService.cs) (around lines 234–253). The controller's `GetWeek` holds ~130 lines of rules: week dates, the holiday query, `IsFullWeekCovered`, picking the active schema and joining schema slots with ugeplan slots. Adding a file to a slot lives in the service, removing one in the controller. The slot DTO is built twice (around lines 189–211 and 311–333).

API:

- [ ] Move `GetWeek` into `WeekPlanService.GetWeekAsync`, including the holiday-week check and active-schema pick. Build the slot DTO in one projection, used by both get and upsert.
- [ ] Move `UpsertSlot` into `WeekPlanService.UpsertSlotAsync`: validate schema slot and course, set fields, save once, return the DTO. Failures as a small enum (not found, invalid course, …), not ProblemDetails.
- [ ] Move `RemoveFile` into `WeekPlanService.RemoveFileAsync` next to `AddFileToSlotAsync`.
- [ ] Replace `GetOrAddSlotAsync` (adds without saving) with methods that save. Give SubstituteService a named method such as `WeekPlanService.AssignSubstituteAsync(...)` and stop it from setting slot fields itself.
- [ ] Remove `AppDbContext` from `WeekPlanController`. Every action is bind → one service call → map.
- [ ] Switch `TestDataBuilder` and hand-built tests that create `WeekPlanSlot` to the service.
- [ ] Integration tests through the endpoints still pass; add one for "remove file from slot" and one for a substitute shown on the slot if missing.

Web:

- [ ] [WeekPlanPage.tsx](../web/src/pages/WeekPlanPage.tsx): delete the hand-written DTO types (around lines 23–87) and use the generated ones.
- [ ] Replace the local slot types `LessonSlot` ([LessonEditor.tsx](../web/src/components/weekplan/LessonEditor.tsx)), `ParentWeekPlanSlot` ([ParentWeekPlan.tsx](../web/src/components/weekplan/ParentWeekPlan.tsx)) and those in [WeekPlanPrintPage.tsx](../web/src/pages/WeekPlanPrintPage.tsx) with the generated type.
- [ ] One holiday-day renderer used by WeekPlanPage, ParentWeekPlan and WeekPlanPrintPage (they render holidays three ways today). Check [73](73-shared-ui-components-round-2.md) "week list with holidays" first and reuse it if built.
- [ ] Staff view and the parent preview on WeekPlanPage open the same week. Today staff use `getPlanningDate` and the preview uses `preselectedDate()` (the 15:00 rule); [isoWeek.ts](../web/src/utils/isoWeek.ts) says they must agree.

Done when: no controller or other service writes `WeekPlanSlot` or `WeekPlanSlotFile`, and the web has one slot type.

## Phase 2: One school calendar for holidays and "today" (M)

The rule "a holiday is `Ferie` or `Lukkedag`" is written out in [SchoolDayCalendar.cs](../api/Skoleoverblikket.Api/Services/SchoolDayCalendar.cs), [SchoolWeekCalculator.cs](../api/Skoleoverblikket.Api/Services/SchoolWeekCalculator.cs), WeekPlanController (moved by Phase 1), [ReportsController.cs](../api/Skoleoverblikket.Api/Controllers/ReportsController.cs) and [ComplianceCoverageService.cs](../api/Skoleoverblikket.Api/Services/ComplianceCoverageService.cs). "Today" is `SchoolDayCalendar.Today()` (Danish date) in some places and `DateOnly.FromDateTime(DateTime.UtcNow)` in about ten others, so from 00:00 to 01:00/02:00 Danish time they disagree by a day.

- [ ] **Niels decides:** does the school year start on 1 August (API, `SchoolDayCalendar.SchoolYearFirstDay`) or on the Monday of the week containing 1 August (web, [useWeekNavigation.ts](../web/src/hooks/useWeekNavigation.ts))?
- [ ] Give `SchoolDayCalendar` the questions callers need, for example `Today`, `IsSchoolDay(date)`, `HolidayDaysAsync(from, to)`, `IsHolidayWeekAsync(week)` and `SchoolYear(date)`. Only it reads `CalendarEntryType.Ferie`/`Lukkedag` for this rule.
- [ ] Move `IsFullWeekCovered` into it.
- [ ] Name it for school days only. SFO opening on school holidays follows ferieindmelding, not this calendar (see Phase 4).
- [ ] Switch SchoolWeekCalculator, WeekPlanService, ReportsController and ComplianceCoverageService to it.
- [ ] Replace every `DateOnly.FromDateTime(DateTime.UtcNow)` meant as "school today": WeekPlan, SchedulesController (×3), VacationRegistrationController (×3), StatsController, ClassMembershipService, ConflictDetectionService, ComplianceCoverageService. Find them with `grep -rn "FromDateTime(DateTime.UtcNow)" api/Skoleoverblikket.Api`.
- [ ] Use `TimeProvider` so tests can set the clock. Add an integration test at 00:30 Danish time that expects the Danish date.
- [ ] Apply the school-year decision on both API and web.

Done when: `grep -rn "Lukkedag" api/Skoleoverblikket.Api --include=*.cs` outside Models, Migrations and CalendarController hits only SchoolDayCalendar, and no "today" uses UTC.

## Phase 3: Files with one upload path (M)

[FileSystemBrowser.tsx](../web/src/components/files/FileSystemBrowser.tsx) (1054 lines) switches between staff and board files with about 13 `variant === 'board'` branches. [FilePicker.tsx](../web/src/components/files/FilePicker.tsx) (518 lines) is a second browser with its own upload. In the API, [FilesController.cs](../api/Skoleoverblikket.Api/Controllers/FilesController.cs) and [BoardFilesController.cs](../api/Skoleoverblikket.Api/Controllers/BoardFilesController.cs) each repeat presign, quota and create (around lines 148–191 and 117–148). `FileUploadService` is a set of static helpers. These are the upload rules from the AGENTS.md Security section.

API:

- [ ] One service owns presign-with-size, quota, file-type check and creating `SchoolFile`/`SchoolFileFolder`, for both staff and board files. Replace the static `FileUploadService` helpers with it.
- [ ] Both file controllers call it and stop injecting `AppDbContext` for these entities.
- [ ] Integration tests cover for both variants: oversize rejected, wrong type rejected, quota reached, and a board member cannot reach staff files (and the reverse).

Web:

- [ ] Split FileSystemBrowser into files: `UploadModal`, `FilePreviewModal`, `CreateFolderModal`, `InlineRename`, `Breadcrumb` and the browser.
- [ ] Replace the `variant` branches with one adapter object per variant (list, upload, rename, move, delete, create folder), chosen once at the top. `FilesPage` and `BoardFilesPage` pass the adapter or a variant that picks it.
- [ ] Drop the unused `showHeader` prop.
- [ ] Make FilePicker use the same browser and upload instead of its own. Keep the round 1 "one list with upload status" behaviour from [67](67-shared-ui-components-round-1.md).
- [ ] Replace the `keycloak.hasRealmRole` admin check with whatever [61](61-school-roles-in-api.md) settles on, if 61 has landed.

Done when: there's one upload path in the API and one in the web, and no `variant ===` checks below the top of the browser.

## Phase 4: SFO service and shared autosave (M)

There is no SFO service. [SfoController.cs](../api/Skoleoverblikket.Api/Controllers/SfoController.cs) writes `SfoShift`/`SfoShiftStaff` (5 saves). [SfoWeekPlanController.cs](../api/Skoleoverblikket.Api/Controllers/SfoWeekPlanController.cs) writes `SfoWeekPlan`/`SfoWeekPlanShift` and copies the get-or-create from WeekPlanService (around lines 180–208), but clears the whole `ChangeTracker`. [SfoPage.tsx](../web/src/pages/SfoPage.tsx) (876 lines) does "all days" as 5 parallel creates with partial-failure handling (around lines 246–258). Notes autosave exists three times: `NotesEditor` (WeekPlanPage), `SfoNotesEditor` (SfoPage) and LessonEditor's 800 ms autosave, each handling overlapping saves differently.

API:

- [ ] New `SfoService` owning `SfoShift`, `SfoShiftStaff`, `SfoWeekPlan` and `SfoWeekPlanShift`. Both SFO controllers stop injecting `AppDbContext`.
- [ ] One endpoint that creates a shift for a list of days in one save. Remove the 5-parallel-creates loop from SfoPage.
- [ ] Fix get-or-create the same way as WeekPlanService (no `ChangeTracker.Clear()`).
- [ ] Don't apply the Phase 2 school-holiday rule to SFO. SFO isn't necessarily closed on school holidays; whether it's open depends on ferieindmelding (`VacationRegistrationService`). SFO week plans ignoring school holidays is correct today. Showing SFO open/closed days from ferieindmelding is a separate feature, not part of this task.
- [ ] Integration tests: create shift for all days, and concurrent get-or-create of the same week.

Web:

- [ ] One `useAutosave` hook (debounce, queued save, rollback on error, "Gemt" state). Use it in NotesEditor, SfoNotesEditor and LessonEditor. Check [73](73-shared-ui-components-round-2.md) "save messages" first.
- [ ] Split SfoPage into the shift grid, shift form modal, cell modal with staff search, and time picker files.
- [ ] Replace `DAY_NAMES` in SfoPage and the weekday array in [SfoPrintPage.tsx](../web/src/pages/SfoPrintPage.tsx) with [lib/weekdays.ts](../web/src/lib/weekdays.ts).

## Phase 5: One owner for the parent–student link (S)

`ParentsController` (`parent.Students.Add/Remove`, around lines 128 and 148), `ParentInvitationService` (around line 76) and [ImportsController.cs](../api/Skoleoverblikket.Api/Controllers/ImportsController.cs) (around line 257) all write the parent–student link. `Student` is created by StudentsController and ImportsController (around line 132).

- [ ] A parent service owns link and unlink. ParentsController, ParentInvitationService and ImportsController call it.
- [ ] A student service owns creating `Student`. StudentsController and ImportsController call it.
- [ ] ParentsController and StudentsController stop writing `AppDbContext` for these entities.
- [ ] `TestDataBuilder` creates students and links through the services.
- [ ] Data-level check ("is this student in this school") lives in the service.

## Phase 6: Web week helpers (S, with Phase 1 or 2)

[isoWeek.ts](../web/src/utils/isoWeek.ts), [useWeekNavigation.ts](../web/src/hooks/useWeekNavigation.ts) and [weekdays.ts](../web/src/lib/weekdays.ts) exist, but pages keep their own copies.

- [ ] [ClassesPage.tsx](../web/src/pages/ClassesPage.tsx): replace `isoWeekMonday`/`isoWeekSunday` (around lines 259–271).
- [ ] [ParentVacationRegistrationPage.tsx](../web/src/pages/parent/ParentVacationRegistrationPage.tsx): replace `getMondayOfWeek` and `weekLabel` (around lines 19–78).
- [ ] [SchemaBuilderPage.tsx](../web/src/pages/SchemaBuilderPage.tsx): use `weekdayLabel` from lib/weekdays.ts.
- [ ] Replace the weekday arrays in WeekPlanPrintPage, ParentWeekPlan (`SHORT_WEEKDAYS`) and SfoPrintPage (if Phase 4 hasn't).
- [ ] School-year range follows the Phase 2 decision.

## Not in this task

- The shared list UI in `web/src/components/ui/list` is in good shape. Small fix if someone is there anyway: `useListControls` memoizes on `filters`/`sorts` arrays that pages pass inline, so the memo is rebuilt every render.
- `MessagesController` (749 lines) and `ClassChatController` (683 lines) write their entities without a service. Extract when their logic next changes, per AGENTS.md "Legacy controllers".

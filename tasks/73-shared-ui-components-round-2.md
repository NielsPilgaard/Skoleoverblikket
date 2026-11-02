---
title: 'Shared UI components, round 2: dialog, delete, messages, states, buttons, fields, badges, tabs, week picker, page header'
purpose: 'Build the patterns Niels picked from the round 2 mockups on 2026-11-02 as shared React components and move every page onto them, so dialogs, deletes, save feedback and page headers look and behave the same.'
description: >-
  Niels picked combinations from task 65's mockups: one dialog shell, delete
  confirmation in tiers with a trash for files, a rule for which save message
  goes where, skeleton and first-use empty states, one Button, one form field,
  one status badge, underline tabs, a week stepper with a week list and
  holidays, and one page header. Builds on PR #89 (task 67). Also makes deleting
  a subject in use hard to do by accident: today one click removes its lessons
  in every class.
status: 'Ready'
---

# Shared UI components, round 2

## TL;DR

- Build the picks below in `web/src/components/ui/` and move the existing pages onto them. Seven parts, in order, on one branch and one PR (Niels, 2026-11-02).
- **Start after PR [#89](https://github.com/NielsPilgaard/Skoleoverblikket/pull/89) (task 67) is merged.** It adds `ui/icons.tsx`, `ui/select/` with `WeekStepper`, and `useWeekNavigation`, and it moves most pages. Grep every count below again on `main`.
- The visual spec is the mockup page **"Skoleoverblikket – UI-valg runde 2, kombinationer"** (<https://claude.ai/artifact/7JGMiWgpE1XiXEyydHXSUD>, slug `32fff0b3-9dfc-4b2b-bab4-8fae66e271de`, private to Niels). Read it with the artifacts `read_artifact` tool. Single variants it doesn't repeat (I1, R4, R6, R8) are on the round 2 page (<https://claude.ai/artifact/TBw9bUfcNbqVoJ9RK6iHXA>, slug `d41469b1-459c-4a41-8794-78c8984c070d`).
- **"Der opstod en fejl" goes away.** Every failure says what happened and what to do next, see [Errors](#errors).
- **Replace, don't add.** Every existing copy of a pattern moves onto the shared component in the same part, and the old markup is deleted.
- Done when: the [migration checklist](#migration-checklist) greps only hit `web/src/components/ui/`, `/verify` and `/test` pass, and each part has been checked on a 375 px phone and a laptop.

## Why

Every page builds its own dialogs, buttons, states and headers, so Hanne has to learn each screen again. Measured in `web/src` on PR #89's branch, 2026-11-02: 20 files use `<Modal>`, 17 calls to `confirm()`, 59 files with their own `<h1>`, and "Der opstod en fejl" in 31 files. Round 2 of the same work as [task 67](67-shared-ui-components-round-1.md).

Reading the delete code also turned up a real risk:

- **Deleting a subject in use is one click.** `Slet faget "Dansk"?` cascades to `SchemaSlot` in every class, then `WeekPlanSlot` (ugeplan text and lektier) and `WeekPlanSlotFile`. Nothing says so.
- **Deleting a class with students, or a room in use, looks like it fails with a server error.** `Student.ClassId` is `Restrict` and `SchemaSlot.RoomId` has no delete action, and neither `ClassesController.Delete` nor `RoomService.DeleteRoomAsync` handles the violation. Read from the code, not run. Part 4 confirms it with a test first.
- **Pages throw away the API's explanations.** The API has 163 `Problem(` calls, most with a Danish `detail`, and `lib/problem.ts` can read them. Only 15 files use it. `ClassesPage` prints "Der opstod en fejl. Prøv igen." whatever the API said.

## Picks

| Component | Picks | Mockup |
|---|---|---|
| Dialog | M2 + M5 + M7 | M-A |
| Delete | D1, D5, D2, D8 for files, plus a blocked state (Q5) | D-A to D-E |
| Messages | G3, G5, G6, G7, G9 | G-A |
| States | S1, S4, plus proper errors (Q4) | S-A |
| Buttons | B1, B7, B4 for a few big choices | B-A, H-B |
| Form field | I1 | round 2, I1 |
| Badges | E3, E9 | E-A, E-B |
| Tabs | R4, R6, R8 on phone | round 2, R4, R6, R8 |
| Week picker | W3 + W6, W8 for planning | W-A, W-B |
| Page header | H1 + H3 + H5 + H6, H9 on rare pages | H-A, H-B |

## Shared rules

Same as [task 67](67-shared-ui-components-round-1.md#shared-rules-all-parts): 44 px touch targets, `text-base` inputs, text no lighter than `gray-500`, color always with text or an icon, keep every `data-testid`, one component per pattern, `sm:`/`md:` in the app where the mockup uses `@2xl:`. Add icons to `ui/icons.tsx`. Danish copy comes from the mockup, except the numbers.

## Parts

### 1. `Button`, `Field`, `StatusBadge`

| Piece | Spec |
|---|---|
| `Button` (B-A) | `variant`: primary, secondary, danger, text. 44 px. `loading` shows a spinner and blocks a second click. Danger always has an icon. Icon-only buttons require `label` (`aria-label` and `title`). One primary per screen or dialog. |
| Missing input (B7) | Forms don't disable the submit button. Tapping it shows what's missing at the field. `disabledReason` (grey button, reason under it) is only for what the user can't change, such as a permission. See Q3. |
| Big choice (B4) | `Button` with `description`. Only for 2–3 weighty choices on a page. |
| `Field` (I1) | Label above, help text, error under with an icon. Checks on blur, then clears as soon as it's fixed. Only optional fields are marked, with "(valgfrit)". Border `gray-500`: `gray-300` is 1.5:1 against white, and borders need 3:1. |
| `StatusBadge` (E-A, E-B) | Status, icon and five colors by meaning: green fine, yellow needs action, blue info, red absent, grey no longer current. Optional `detail` ("Vikar Mikkel Sørensen"). Lists leave out "Aktiv", as `PersonList` does. |

### 2. `Dialog` (M-A)

Replaces `Modal.tsx`. Bottom sheet on phone, centered on laptop; title and close on top, buttons at the bottom, only the middle scrolls.

- Build on native `<dialog>` with `showModal()`. That gives focus trapping, an inert page behind and one Esc listener, which fixes today's bug: `Modal` listens for Esc while closed, so Esc closes every mounted dialog. No new dependency. Toasts need the `popover` attribute or they render behind the dialog.
- `dirty` prop: closing by backdrop, X, Esc or "Annuller" shows "Luk uden at gemme?" inside the dialog, only when there are changes (M5).
- Move `LessonEditor` onto it: PR #89 gave it its own copy of the shell. Also check the other `fixed inset-0` overlays: `FileSystemBrowser`, `AssignSubstitutePanel`, `ImportPage`, `ParentAbsencePage`. The sidebars are not dialogs.

### 3. Messages and states

| Situation | Use | Failure |
|---|---|---|
| Form with a Gem button | "Gemt kl. 10:42" beside the button (G3) | Same place, stays: what happened, and that the text isn't lost |
| Action in a list | `useToast`: stacked at the bottom, at most 3, gone after 6 s (G5). Above the bottom bar on phone | Same stack, stays, "Prøv igen" |
| After a page change | Green `PageBanner` on top until closed (G6) | |
| Autosave | "Alt er gemt" status (G7), the badge `LessonEditor` already has | The `LessonEditor` behaviour |
| Something went out to many people | Large check for 2 s with the number of recipients (G9). Few places: messages to parents, nothing else without asking Niels | Toast that stays |

Document this table in a comment at the top of the toast file, as `ui/select/index.ts` does. Failures follow Q4.

`PageState` (S-A): `loading` renders a skeleton in the content's shape (rows for lists, a prop for cards), shown after 0.4 s so fast loads don't flash. `empty` has an icon, a title, one sentence on what the feature is for and one button (S4). `error` renders the [Errors](#errors) text with its action. "Ingen matcher søgningen" stays in `ListToolbar`.

#### Errors

Niels, 2026-11-02: "Der opstod en fejl" isn't good enough. The grep for it must end at zero, and the 33 files with their own "Kunne ikke ..." line move too.

`describeError(err, { what: 'klassen', action: 'gemme' })` in [lib/problem.ts](../web/src/lib/problem.ts) is the only way a failed request becomes text. The caller names the thing and the verb, so no text is generic. It returns the message and the action to offer.

| Failure | How to tell | Message | Action |
|---|---|---|---|
| The API explains | 4xx with `detail` | The `detail`, as is | None, or what the message says |
| Invalid input | 400 with `errors` | At each `Field`, by key. A line by the button for keys with no field | None |
| No connection | `fetch` throws | "Ingen forbindelse. Klassen blev ikke gemt." | Prøv igen |
| Logged out | 401 | "Du er blevet logget ud." | Log ind |
| No access | 403, no `detail` | "Du har ikke adgang til klassen. Spørg skolens administrator." | None |
| Gone | 404 | "Klassen findes ikke længere. En anden kan have slettet den." | Tilbage |
| Changed meanwhile | 409, no `detail` | "Klassen er ændret af en anden. Hent den igen, og se om din ændring stadig passer." | Hent igen |
| Our fault | 5xx, after `apiFetch`'s retries | "Det er ikke din skyld. Klassen blev ikke gemt. Prøv igen om lidt. Sker det igen, så skriv til kontakt@skoleoverblikket.dk og nævn koden A1B2C3." | Prøv igen |

Rules:

- Never state a cause or a comfort we don't know. No "det skyldes oftest internettet" on a 500, and "intet er slettet" only where that's true. The round 2 mockups show that kind of example text; this table wins.
- **Typed text is never lost.** A failed save keeps the dialog open and the form filled, and says so ("Det du har skrevet, er ikke væk").
- "Prøv igen" repeats the same request. `apiFetch` already waits out deploys, so don't add another retry loop.
- The code comes from the ProblemDetails `traceId`. Check that it's in the response and that a shortened one can be found in the logs; use the full id if not.

**API side**, in the same part:

- `detail: ex.Message` in `BillingController` and `AdminController` sends exception text, possibly Stripe's, to users. Log it and return a fixed Danish message.
- English messages, such as `ValidationProblem("A class cannot be both renamed and archived ...")` in `ClassesController`. Grep the `Problem(` and `ValidationProblem(` calls and translate them.
- 81 bare `BadRequest()`, `Conflict()`, `Forbid()` and `UnprocessableEntity()` returns, plus `Problem(statusCode: 500)`. Add a Danish `detail` where the user can do something about it. The status table covers the rest.

### 4. Delete

**Tiers.** The API tells the page which one applies, so a subject with no lessons is a one-liner and one with 186 is not.

| Tier | Component | When |
|---|---|---|
| D-A | Sentence, "Behold X" and "Slet X" | Nothing else is affected |
| D-B | Lists: "Det her slettes" and "Det her bliver ikke rørt" | Something follows, inside one class or person |
| D-C | D-B plus typing the name; the button states the amount | Hits several classes, or a whole class |
| D-D | No red button: why, and a button to the next step | The API refuses the delete |
| D-E | No dialog: "Flyt til papirkurv" | Files and folders |

**What deleting really does**, from the EF model on 2026-11-02. Check it again, and write the copy from this rather than from the round 2 page, whose D1, D2 and D4 text was made up:

| Page | Today | Tier |
|---|---|---|
| `CoursesPage` | Cascades to lessons in all classes, their ugeplan and attachments. Files only lose the subject | D-A with no lessons, otherwise **D-C** |
| `ClassesPage`, class | Cascades to schemas, week plans, chat, attendance checks, `ClassPermission`. Refused by the database with students | **D-D** with students, otherwise D-C |
| `ClassesPage`, schema | Cascades to lessons and their ugeplan | D-B |
| `ClassTimeSlotsPage` | A **Gem** button that empties the schema. Button says "Gem og tøm skemaet" | D-C, only when lessons exist; a plain save otherwise |
| `StaffPage` | API already answers 409 if the person has lessons, or if it's your own login. Otherwise cascades to absences, SFO shifts, permissions | **D-D**, otherwise D-B |
| `StudentsPage` | Cascades to absence, follow-ups, contact threads, vacation entries | D-B |
| `RoomsPage` | Refused by the database when in use | **D-D** when in use, otherwise D-A |
| `SubstituteCoverDetailPage` | Removes substitutes on the affected lessons | D-B |
| `ParentsPage` | Check what follows | D-A or D-B |
| `LinksPage`, `BoardMembersPage`, `CalendarPage`, `ClassChatPage` | Nothing follows | D-A |
| `StaffAbsencePage`, `BackofficeSubProcessorNoticePage` | Not deletes (cancel, send) | D-A shape, plain button, their own wording |
| `FileSystemBrowser` on `FilesPage` | Immediate delete from storage | D-E |
| `FileSystemBrowser` on `BoardFilesPage` (`variant="board"`) | Immediate delete from storage | D-E |

**API.** A `GET .../delete-preview` per entity that has D-B to D-D: courses, classes, schemas, time slot changes, staff, students, rooms. It returns blockers and counts, in the owning service, using projections. No Danish in the response; the page writes the copy. Every endpoint names its roles (admin, matching its delete). `Classes` delete still lives in the controller, so move it into a service in the same PR (AGENTS.md). The delete itself must return the same blockers as a `ProblemDetails` 409 instead of a 500, since the preview can be stale. Integration tests: subject preview counts lessons across classes, class with students and room in use give 409, tenant isolation.

**Trash (D-E)**, for school files and board files (Q6):

- `DeletedAt` on `SchoolFile`, `SchoolFileFolder`, `BoardFile` and `BoardFileFolder`, added to the query filter. Move "delete" to set it, and delete from storage only when purged. `FilesController` and `BoardFilesController` write to `AppDbContext` directly, so move this logic into a service.
- Restore endpoint. Trashing a folder trashes its contents; restoring brings them back together.
- Purge after 30 days: a job like `ClassChatAttachmentSweeper`. It crosses tenants with `IgnoreQueryFilters()`, so it needs the comment and a tenant-isolation test.
- While in the trash, a file disappears from ugeplaner and comes back on restore, so `WeekPlanSlotFile` stays. Test it, since a filtered required navigation is easy to get wrong.
- **Check:** `SchoolDeletionService` must also remove trashed files, or a deleted school leaves files behind. Quota counts them until they're purged. Files have a public `Url`, so a trashed file's link keeps working for 30 days. Find out whether that holds for board files too, and stop serving them if it does.
- Add a sentence on the 30 days to section 6 of `PrivacyPolicyPage`, which lists retention per kind of data. See Q6.
- The migration needs Niels's `migration-reviewed` label, so the PR stays red until he sets it.

### 5. `Tabs`

- **R4 by default:** underline, 3 px, optional count, scrolls on phone with the selected tab scrolled into view, `role="tablist"` and arrow keys. A yellow count means something needs attention. Move `AbsencePage`, `ClassesPage`, `ClassRolloverPage`, `ComplianceCoveragePage`, `ImportPage`, `MessagesPage`. `ParentWeekPlan`'s day tabs are their own pattern; leave them.
- **R6** (`SideTabs`): side menu on laptop, list with "‹ Indstillinger" on phone. First use `SchoolSettingsPage`, which has three sections today.
- **R8** (`phoneBar` prop on `Tabs`): bottom tabs on phone only, for 2–4 short tabs used daily, with icons. First use `AbsencePage`, where "Anmodninger om fri" needs a short label. No other page without asking Niels. See Q2.

### 6. Week picker (W-A, W-B)

Builds on PR #89's `WeekStepper` and `useWeekNavigation`:

- **Tap the week to get a list (W3).** Replaces the `jumps` prop, `weekJump` and `yearJump`, which sit beside the stepper on `WeekPlanPage` and `SfoPage`. Each row has week number, dates, holiday name and "Denne uge". The list covers the school year (`schoolYearWeeks` exists). SFO's year becomes a `Select` at the top of the list. Sheet on phone, popover on laptop. This reverses PR #89's "dropdowns stay beside the stepper" for weeks (Q7).
- **Holiday in the stepper (W6).** Blue box, sun icon and the name; "Denne uge" / "Næste uge" beside the number. The week's content says "Efterårsferie: ingen undervisning" instead of looking empty. "Tilbage til denne uge" stays.
- **Data needs no API change.** A hook reads `GET /api/v1/calendar?year=` for both calendar years, as `ParentCalendarPage` does (parents can read it). A week is a holiday when every Monday to Friday is covered by `Ferie` or `Lukkedag` and no `Arbejdsdag`. Partly covered weeks only show the name in the list. Schools that never added holidays just see dates; don't fall back to `/calendar/defaults`, which may be wrong for that school.
- **W8** (`picker="calendar"`): the same stepper with a month grid with week numbers. Only for pages that choose a week far ahead. Look for one; if none exists yet, build it when a page needs it, as task 67 did for `ComboBox`.
- Day, month and quarter steppers stay as they are. `weekplan-lesson-editor.spec.ts` uses the `week-jump` and `year-jump` testids; update it.

### 7. `PageHeader` (H-A, H-B)

Fixed slots: back link or breadcrumb, title and one line, one primary action, one tool slot (search or `WeekStepper`).

- Laptop: breadcrumb when more than one level down (H3), button right of the title.
- Phone: back link instead of the breadcrumb. **The primary action sits in a bottom bar (H6)**, not under the title (Q1). Toasts sit above it. Mind the safe area, and that `Layout` owns the scroll container.
- Scrolled: sticks and shrinks to one line, title and on laptop the button (H5). Use a gap between the collapse and expand thresholds so it doesn't flicker.
- `description` and `help` props for rare pages (H9, H-B): `ClassRolloverPage` first, then `ImportPage`. B4 buttons belong there. The mockup's button text is an example; check it against what `ClassRolloverPage` does.
- Move every page's `<h1>`. Print pages (`print-title`) and the landing page are not in scope.

## Decisions

Niels, 2026-11-02, on the conflicts between the picks:

- **Q1, H1 vs H6 on phone.** H1 puts the button under the title, H6 in a bottom bar. Bottom bar always, so there's one rule.
- **Q2, R8 vs H6.** Two bottom bars don't fit. On a page with R8, the primary action sits inside the tab's content.
- **Q3, B1 vs B7.** B7 when the user can fix it by filling something in. A grey button with a reason only for what they can't change. Typing the name (D-C) is the exception: grey until it matches.
- **Q4, errors.** S6 and G4 weren't picked, but the picks had no failure state. Niels: "der opstod en fejl holder ikke, gør det ordentligt". Hence [Errors](#errors), which goes further than the S6 box. Failures show where the success would have: by the button, in the stack, on the page.
- **Q5, D-D.** Added: a blocked state, because the API refuses some deletes, and the D-B lists would otherwise end in an error.
- **Q6, board files.** In the trash too, one behaviour in `FileSystemBrowser` (confirmed by Niels). The DPA has no promise about deleting single files at once (section 10 covers termination), but it's still awaiting legal review ([INDEX](INDEX.md)), so mention the trash there.
- **Q7, week dropdowns.** PR #89 keeps dropdowns beside the stepper. W3 replaces them with the week list. PR #89 merges unchanged; this task removes them.
- **Q8, one branch and one PR** for all parts, trash included.

## Migration checklist

Found on PR #89's branch on 2026-11-02; grep again on `main`. At the end of each part, its grep should only hit `web/src/components/ui/`.

| Part | Replace with | Grep |
|---|---|---|
| 1 | `Button` | `bg-brand-600` on `<button` (66 distinct class strings on 2026-10-31) |
| 1 | `Field` | `<input`, `<textarea` with their own label markup |
| 1 | `StatusBadge` | `rounded-full bg-` |
| 2 | `Dialog` | `<Modal`, `fixed inset-0` (20 + 8 files). Delete `Modal.tsx` |
| 3 | `PageState` | `animate-pulse` (28 files), `Indlæser` |
| 3 | `describeError` | `Der opstod en fejl` (31 files, must reach zero), `Kunne ikke` (33 files), `detail: ex.Message` |
| 3 | Messages | `Gemt` (`NotificationPreferencesPage`, `MarkdownTextarea`) |
| 4 | Delete tiers | `confirm(` (17 calls in 16 files) |
| 5 | `Tabs` | `border-b-2`, `role="tablist"` |
| 6 | `WeekStepper` | `jumps`, `weekJump`, `yearJump` |
| 7 | `PageHeader` | `<h1` (59 files) |

## Steps

1. Read [PERSONAS.md](../docs/PERSONAS.md), the mockup page and PR #89's `ui/` folder.
2. Do the parts in order on one branch, with one PR. Commit per part, each with a `feat:` subject that describes what users get.
3. Per part: check a 375 px phone and a laptop in the running app (`/run`), keyboard and screen-reader basics. Say in the PR what wasn't checked.
4. E2E: keep existing tests green. Add Playwright tests for what would block Hanne if it broke silently: the delete tiers (a subject in use needs its name typed, a teacher with lessons is blocked), the trash and restore, the week list, and errors (`page.route` a 500 and a dropped connection: the right text shows, typed text stays, "Prøv igen" works).
5. `/verify` and `/test` before the PR. Part 4 needs API integration tests.

## Out of scope

- Undo toasts (variant D3): not picked.
- Swipe, floating buttons, overflow menus, floating labels: not picked.
- A trash for anything but school and board files.
- Dark mode, new brand colors or fonts.

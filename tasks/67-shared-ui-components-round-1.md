---
title: 'Shared UI components, round 1: lists, pickers, ugeplan, lesson editor, file picker'
purpose: 'Build the five UI patterns Niels picked on 2026-10-31 as shared React components and move the existing pages onto them, so every list, picker and file attachment looks and behaves the same.'
description: >-
  Niels picked one design per component from the round 1 mockups: T-C person
  list and T-B data list (search, filter chips, sort), the select family by
  situation (no swipe chips), the parent ugeplan with day tabs, today
  preselected and the teacher's "Generelt for ugen" on top, the lesson editor
  with autosave and "Næste lektion", and a one-list file picker with
  suggestions and an upload queue. The published mockup page is the visual
  spec. Round 2 picks (task 65) get their own build task.
status: 'In progress'
---

# Shared UI components, round 1

## Status (2026-11-02)

**All five parts are built on one branch:** PR [#89](https://github.com/NielsPilgaard/Skoleoverblikket/pull/89), `feature/shared-ui-lists`. Niels asked for one branch instead of one PR per part. Not merged yet.

- **Part 1:** [web/src/components/ui/list/](../web/src/components/ui/list/) has `PersonList`, `DataTable`, `ListToolbar`, `useListControls` and `parts.tsx`. `DataTable` renders one layout at a time ([useMediaQuery](../web/src/hooks/useMediaQuery.ts)), because a CSS-hidden copy would give every `data-testid` two matches. Filters: 2–5 options are chips, more than 5 a `Select`, a lone "Alle" renders nothing.
- **Part 2:** [web/src/components/ui/select/](../web/src/components/ui/select/) has `Select`, `SegmentedControl`, `RadioCards` and `WeekStepper`; [useWeekNavigation](../web/src/hooks/useWeekNavigation.ts) holds the week state. Every `<select>` moved. `RecentSelect`, `ComboBox`, `MultiSelect` and `PersonPicker` are not built: no page in parts 3–5 needed them.
- **Part 3:** [ParentWeekPlan](../web/src/components/weekplan/ParentWeekPlan.tsx), also used by the staff preview. The API now stores who wrote "Generelt for ugen" and when (`NotesUpdatedBy`, `NotesUpdatedAt`), and `ParentStudentDto` has `IsEnrolledInSfo`.
- **Part 4:** [LessonEditor](../web/src/components/weekplan/LessonEditor.tsx).
- **Part 5:** [FilePicker](../web/src/components/files/FilePicker.tsx). "Senest brugt af dig" needed data: `WeekPlanSlotFile` now stores `AttachedBySubject` and `AttachedAt`, and `GET /api/v1/files/recent` reads them, so the section fills up from new attachments only. The real upload limit is 500 MB, not the mockup's 25 MB.
- **No "Husk" box** (Niels, 2026-11-02). Nothing stores "husk", and the editor stays at two fields. Folder rows also skip the file count, which the API doesn't return.
- **Left as they are:**
  - `ParentContactBookPage`: a message inbox, not a list of people.
  - `ImportPage` invite table: a checkbox picker, a future `MultiSelect`.
  - Timetable and print grids: CalendarGrid, PrintSchemaPage, SfoPrintPage, WeekPlanPrintPage, MySchedulePage, StaffSchedulePage, RoomSchedulePage, ParentSchemaPage.
  - `PasteGrid` (an input grid) and `ComplianceCoveragePage` (UVM-timetal).
  - The per-student button in `AttendancePage` keeps `aria-pressed`: it's a toggle per child, not a group of options.
- **Kept beside the stepper** (Niels, 2026-11-02): the week dropdown on Ugeplan and the week and year dropdowns on SFO, through `WeekStepper`'s `jumps` prop. Removed: the dots on the phone calendar.
- **Folders stay on top** in the Filer list, whatever the sort and direction (Niels, 2026-11-02): `pinFirst` in the list controls.
- **Before merge:** the migration needs the `migration-reviewed` label from Niels. Not yet checked by hand on a real phone or with a screen reader.
- **After merge:** delete the `pr-assets/shared-ui-lists` branch, which only holds the PR's screenshots.

## TL;DR

- Build the picked designs as shared components in `web/src/components/ui/` and move the existing pages onto them. Five parts, each shippable on its own, in the order below.
- The visual spec is the mockup page **"Skoleoverblikket – UI-valg, kombinationer"** (<https://claude.ai/artifact/PHhZU3G82LBbKwa6BT2Vyq>, slug `b47df259-b1f6-48bc-adbc-1f93177dce98`, private to Niels). Read it with the artifacts `read_artifact` tool. Its Tailwind classes are meant to be lifted as-is.
- Picks: **T-C** and **T-B** (tables), the **select rule table** (selects), **U-A** (parent ugeplan), **L-A** (lesson editor), **F-A** (file picker).
- **Replace, don't add.** Every existing instance of a pattern moves onto the shared component in the same part, and the old markup is deleted. See [Migration checklist](#migration-checklist).
- Done when: the migration checklist greps find no hand-rolled copies, `/verify` and `/test` pass, and each part has been checked on a 375 px phone and a laptop.

## Why

Every page builds its own list, `<select>`, accordion and file picker, so the same thing looks and behaves differently across pages and Hanne has to learn each screen again. Measured in `web/src` on 2026-10-31: 20 files with a `<table>`, 21 with a `<select>`, the parent ugeplan collapses every day and shows times in `gray-400`, and the lesson editor puts five features in one long modal. Inputs use `text-sm`, which makes iPhone zoom in on focus.

## Shared rules (all parts)

- Touch targets at least 44 px (`h-11`). Inputs `text-base` (16 px) so iPhone doesn't zoom.
- Text no lighter than `gray-500` on white. Color always paired with text or an icon (badges say "Inviteret", dots have a legend and `sr-only` text).
- Phone layouts in the mockup use container queries (`@2xl:`); in the app use the normal breakpoints (`sm:`/`md:`). The tailwind config has no container-queries plugin; don't add one for this.
- Keep every existing `data-testid`. Move it onto the new component's element when markup changes, so Playwright tests keep working.
- One component per pattern. If a page needs a variant, add a prop; don't fork the component.

## Parts

### 1. Lists: `PersonList` (T-C) and `DataTable` (T-B), with one shared `ListToolbar`

| Piece | Spec |
|---|---|
| `ListToolbar` | Search field, 3–5 filter chips (`aria-pressed`), "Viser N af M" count (`aria-live="polite"`), sort `Select`. Same behaviour in both lists. Empty state "Ingen matcher søgningen" + "Ryd søgning". |
| `PersonList` (T-C) | Two-line rows: avatar initials, name, "role · classes", status badge only when not Aktiv, chevron. Tapping a row folds out details (`dl`) and that person's actions inline, primary first. Same on phone and laptop. Use for people: staff, students, parents. |
| `DataTable` (T-B) | One column definition renders a table on laptop and label/value cards on phone; nothing is hidden on phone. Sortable column headers on laptop; on phone the toolbar's sort select does it. Use when users compare columns: ferieindmeldinger, files, payments. |

Move [StaffPage.tsx](../web/src/pages/StaffPage.tsx) first (it hides columns on phone today), then grep `<table` in `web/src/pages` and move each page to whichever of the two fits. Wide number grids (UVM-timetal, fravær per week) are out of scope: they need a sticky first column (round 1 T5), which is a separate component.

### 2. Selects: a family, chosen by situation

Build these and document the rule in a comment at the top of `web/src/components/ui/select/index.ts`:

| Situation | Component | Round 1 |
|---|---|---|
| 2–4 short options | `SegmentedControl` (radio inputs behind) | V2 |
| The choice has a consequence to explain | `RadioCards` | V3 |
| 5–30 options | `Select` (native `<select>`, styled, chevron icon, inline error/conflict text) | V1 |
| Options the user repeats weekly | `RecentSelect`: "Senest brugt" group on top; bottom sheet on phone, popover on laptop | V8 in V6 |
| Over 30 options | `ComboBox` (search as you type, secondary text per option). Use a headless library rather than hand-rolling ARIA | V5 |
| Several at once | `MultiSelect` (removable chips above checkboxes; search above 15 options) | V7 |
| Depends on availability | `PersonPicker` (ledig / optaget with the reason, ledige first) | V9 |
| Values in sequence | `WeekStepper` (‹ Uge 45 › with dates under, "Gå til denne uge") | V10 |

No swipe chips (V4). Switching child uses `SegmentedControl`; switching class uses `Select`. Build `Select`, `SegmentedControl`, `RadioCards` and `WeekStepper` in this part and move every existing copy onto them (see the migration checklist). Build the others (`RecentSelect`, `ComboBox`, `MultiSelect`, `PersonPicker`) when a page in parts 3–5 or a later task needs them. `WeekStepper` is also task 65's week navigator baseline; if task 65 picks a different week navigator, that pick replaces it.

### 3. Parent ugeplan (U-A)

[ParentWeekPlanPage.tsx](../web/src/pages/parent/ParentWeekPlanPage.tsx) and `WeekPlanList.tsx`.

- Top: `WeekStepper` (week number, class and dates).
- **"Generelt for ugen"** card next: teacher avatar and name, when it was written, text clamped to 3 lines with "Læs hele" / "Vis mindre". Hidden when the teacher wrote nothing. Data: the week plan notes that `NotesEditor` in [WeekPlanPage.tsx](../web/src/pages/WeekPlanPage.tsx) writes.
- Day tabs Man–Fre (`role="tablist"`). The selected day is chosen automatically: today until 15:00, then the next school day; Saturday and Sunday select Monday of the next week. The preselected tab says "I dag" or "I morgen" instead of the weekday. Dots under the date for lektier (sky) and changes (amber), with a legend.
- Selected day: a brand-colored summary card (date, school hours, "derefter SFO" when relevant) with two boxes: what's different today (vikar, changed subject) and lektier. Then the lesson list: time, subject, description, lektier box, vikar line, "Ændret" badge with the old subject struck through, file links (44 px).
- Times in `gray-700`, not `gray-400`.
- Several children: a `SegmentedControl` above the week stepper (instead of today's stacked classes). A merged family view (round 1 U9) is out of scope.

### 4. Lesson editor (L-A)

Replaces `EditSlotModal` in [WeekPlanPage.tsx](../web/src/pages/WeekPlanPage.tsx). Bottom sheet on phone, centered modal on laptop.

- Header: subject, "Tirsdag 3. nov · 08:50–09:35 · 3.A", save status badge ("Gemmer …" with spinner → "Gemt" with check, `role="status"`), close button.
- Two fields: "Hvad skal I lave?" and "Lektier (valgfrit)", both `text-base`. Keep the markdown support the current `MarkdownTextarea` has.
- **Autosave**: save about 800 ms after typing stops, and immediately on blur, "Næste" and "Luk". Show "Fortryd mine ændringer" once the text differs from when the lesson was opened; it restores that text and saves it.
- Three rows below: Filer ("2 vedhæftet"), Byt fag ("Nej" or the new subject), Vikar ("Ingen" or the name). Each opens a sub-step; Filer opens the part 5 picker.
- Footer: small "Luk" + large primary "Næste: Idræt 10:00 →". Next goes to the next lesson that day, then the next day ("Næste: Onsdag Engelsk 08:00"). Disabled on the last lesson of the week.
- If the save fails, the badge says so in plain Danish with "Prøv igen", and the text stays in the field. Never lose typed text.

### 5. File picker (F-A)

Replaces [FilePicker.tsx](../web/src/components/files/FilePicker.tsx) (used by `WeekPlanPage`); build it reusable for messages, fravær and ferieindmelding.

- One list, no tabs. Top: title, close, search ("Søg efter filnavn eller fag"), then "Fra telefonen" + "Tag billede" (`capture="environment"`) on phone, or "Upload fra computeren, eller træk filer hertil" (also a drop target) on laptop.
- Upload queue (F10) above the lists: per-file progress, and a finished file is ticked automatically. Errors in plain Danish with the next step: too big ("Den må højst være 25 MB. Del et link til den i stedet." + Fjern), connection lost ("Prøv igen"). One failed file doesn't stop the others.
- Sections: "Forslag til {fag}, {klasse}" (files with the lesson's subject, newest first), "Senest brugt af dig" (files the user attached recently), "Alle filer" (folder rows). 2–3 rows each in the first two.
- F1 rows: 56 px, file-type icon, name and a second line (why it's suggested, or subject · size), checkbox on the right.
- Footer: "Ingen filer valgt" / "1 fil valgt" / "N filer valgt" + "Færdig".
- Check what data exists for "senest brugt" (lesson attachments by the current user) before adding an endpoint. If it needs one, follow the AGENTS.md service rules.
- Find the real upload size limit in the API; the mockup's 25 MB is a placeholder.

## Migration checklist

Each part replaces every existing copy of its pattern. Found in `web/src` on 2026-10-31; grep again before starting, since pages change:

| Part | Replace with | Existing copies |
|---|---|---|
| 1 | `PersonList` / `DataTable` + `ListToolbar` | All 20 files with `<table>` (except wide number grids, see part 1). Person lists that aren't tables today: [ParentDirectoryPage.tsx](../web/src/pages/ParentDirectoryPage.tsx), [ParentContactBookPage.tsx](../web/src/pages/parent/ParentContactBookPage.tsx). |
| 2 | `Select` | All 21 files with `<select>`. |
| 2 | `WeekStepper` | Week navigation (`prevWeek`/`nextWeek`) in [WeekPlanPage.tsx](../web/src/pages/WeekPlanPage.tsx), [ParentWeekPlanPage.tsx](../web/src/pages/parent/ParentWeekPlanPage.tsx), [SfoPage.tsx](../web/src/pages/SfoPage.tsx), plus any week/day stepper under another name (schema, fravær). |
| 2 | `SegmentedControl` | Hand-rolled `aria-pressed` toggle groups in `AttendancePage`, `BillingPage`, `ParentAbsencePage`, `RoleSwitcher` (not the formatting buttons in `MarkdownTextarea`; those are a toolbar). |
| 2 | `SegmentedControl` or `RadioCards` | Radio groups in `CalendarPage`, `LinksPage`, `MessagesPage`, `VacationRegistrationPage`: 2–4 short options → segmented; options that need an explanation → radio cards. |
| 3–5 | New ugeplan, lesson editor, file picker | Old `WeekPlanList` accordion, `EditSlotModal`, `FilePicker`: delete them once nothing imports them. |

Not in this task: `Sidebar` and `DatePicker` (their own patterns), and anything round 2 covers (modals, buttons, toasts, tabs). A page that needs a pattern this task doesn't cover keeps its markup and gets a line in the PR description, so task 65's build task can pick it up.

At the end of each part, the grep for that part's pattern should only hit `web/src/components/ui/`.

## Decisions

- **D1, autosave visibility (decided 2026-10-31: accept).** With autosave, parents can see half-typed text for the few seconds someone is typing. Accepted: a draft/publish state needs a schema change, and parents rarely look at that moment. No draft state in this task.

## Steps

1. Read [PERSONAS.md](../docs/PERSONAS.md) and the mockup page. Skim the five current pages/components named above.
2. Do the parts in order (1 → 5), one PR per part. Each PR: component(s), page migrations, a `feat:` commit that describes what users get.
3. Per part: check a 375 px phone and a laptop in the running app (`/run`), keyboard and screen-reader basics (tabs, fold-outs, status).
4. E2E: keep existing tests green. Add a Playwright test for the lesson editor's autosave + "Næste" flow (a silent break blocks every teacher) and for the ugeplan's preselected day.
5. `/verify` and `/test` before each PR.

## Out of scope

- Round 2 components (modal, toast, buttons and so on): task 65, then its own build task.
- Sticky-first-column number grids (round 1 T5) and image grids (F5).
- Dark mode, new brand colors or fonts.

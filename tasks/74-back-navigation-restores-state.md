---
title: 'Back navigation that restores the page as it was'
purpose: 'Make every back button, and the browser and phone back gesture, return to the previous page exactly as the user left it: same week, tab, filter, folder, open accordions and scroll position.'
description: >-
  Today going back resets the page: the week jumps to this week, the open class
  folds up, the absence tab's date resets, and the list starts at the top. Back
  buttons are hand-written per page, some hard-code the list route and some call
  navigate(-1) with no fallback. This task adds one back mechanism based on the
  browser history, puts page state that matters in the URL or in the history
  entry, restores scroll in Layout's scroll container, and moves every page onto
  it.
status: 'Ready'
---

# Back navigation that restores the page as it was

## TL;DR

- **One rule: back means browser back.** The in-app back button, the browser button and the Android back gesture do the same thing, so the page you return to is the one you left. When there is no in-app page to go back to (link from an email, a new tab), the button goes to the page's parent route instead.
- **State that should survive back lives in the history, not in `useState`.** Week, tab, filter, search, folder and selected thread go in the URL (`useUrlState`). Open accordions and "vis flere" go in the history entry (`useEntryState`). Scroll is saved per history entry and restored in `Layout`'s `<main>`.
- One PR, five parts done in order. Part 1 builds the hooks and `BackLink`; parts 2 to 4 move pages onto them; part 5 waits for Niels (Q4) and 73 part 2, and is left out if either isn't ready.
- Size L. Owner: Agent. No API change.
- **Start after PR [#89](https://github.com/NielsPilgaard/Skoleoverblikket/pull/89) ([task 67](67-shared-ui-components-round-1.md)) is merged**: it rewrites many of the same pages and adds `useWeekNavigation`. [Task 73](73-shared-ui-components-round-2.md) part 7's `PageHeader` renders this task's `BackLink` in its back slot, whichever lands first.
- Done when: the [migration checklist](#migration-checklist) greps come back clean, `navigation.spec.ts` passes, and the flows in [Done when](#done-when) work on a 375 px phone and a laptop.

## Why

Hanne opens Klasser, folds out 4.A, scrolls down to its skemaer and opens one. When she goes back, 4.A is folded up again (unless it was the last class she opened, see below) and the list is at the top. Thomas picks week 49 in the ugeplan, opens a lesson's files in another page, goes back and is in week 47. On a phone, every swipe back is a small reset. Read from the code on `main` (3e2ffde), 2026-11-17, not run:

- **Scroll is never restored, and doesn't even reset.** `ScrollToTop` calls `window.scrollTo`, but app pages scroll inside `Layout`'s `<main className="overflow-y-auto">`, which stays mounted between routes. So a new page can open scrolled halfway down, and going back lands wherever the last page was. Public pages scroll the window, so `ScrollToTop` only works there.
- **Page state is `useState`, so it's gone on back and on reload.** Weeks in `WeekPlanPage`, `ParentWeekPlanPage` and `SfoPage`; the date in `AttendanceTab`; the period in `AbsenceRegisterTab` and `AbsenceStatsTab`; the school year in `CalendarPage` and `ParentCalendarPage`; the folder in `FileSystemBrowser`; tabs and the selected message in `MessagesPage`; the selected thread in `ContactBookPage`, `ParentContactBookPage` and `ClassChatPage`; the class filter in `StudentsPage`; the open snapshot in `ComplianceCoveragePage`; the expanded student in `ParentDirectoryPage`; open days in `WeekPlanList`.
- **Only 9 files use `useSearchParams`**, and `AbsencePage` (`fane`) and `AttendancePage` (`dato`, `trin`) are the only ones that keep view state there. `ClassesPage` remembers one expanded class in `localStorage`, which is a cross-visit preference, not page state: two tabs fight over it.
- **Back buttons are hand-written on 14 pages, with three behaviours:**
  - `navigate(-1)` with no fallback: `WeekPlanPage`, `ClassTimeSlotsPage` (when there's no `schemaId`). Opened from a link in an email, it leaves the app or does nothing.
  - Fixed link to the list: `SchemaBuilderPage` (`/klasser`), `StaffSchedulePage`, `RoomSchedulePage`, `SubstituteCoverDetailPage`, `VacationRegistrationDetailPage`, `AttendancePage`, `BackofficeTenantDetailPage`. This pushes a fresh copy of the list, so its state is lost, and the browser back button then goes *forward* into the detail page again.
  - In-page back (master/detail in one route): `MessagesPage`, `ContactBookPage`, `ParentContactBookPage`, `ClassChatPage` on phone, and "← Tilbage til versioner" in `ComplianceCoveragePage`. The phone back gesture skips these and leaves the page.
- **Leaving after delete pushes.** `SubstituteCoverDetailPage` and `BackofficeTenantDetailPage` call `navigate('/list')` after a delete, so back returns to the deleted item's error page.

## Design

### Which state goes where

| Kind | Example | Where | History entry? |
|---|---|---|---|
| What the page shows | week, tab, date, school year, filter, search, folder, selected thread or snapshot | URL search param, `useUrlState` | No: `replace` (see Q2) |
| A step that feels like a new screen | phone thread view, opening a folder | URL search param, `useUrlState({ push: true })` | Yes |
| How the page is folded | open accordion, expanded row, "vis flere", collapsed section | history entry, `useEntryState` | No |
| Scroll | `<main>`, opt-in inner scrollers | history entry, saved automatically | No |
| Preference across visits | sidebar groups (`NAV_EXPANDED_GROUPS_KEY`) | `localStorage`, unchanged | — |
| Never restored | open dialogs and menus, drafts, drag state, toasts | `useState` | — |

The test for URL vs entry: would Hanne paste this link to a colleague and expect them to see the same thing? Week and folder, yes. Which class she had folded out, no.

URL param names are Danish, like the existing `fane`, `dato` and `trin`: `uge` (`2026-W49`), `aar`, `fra`/`til`, `kvartal`, `klasse`, `mappe`, `soeg`, `traad`, `besked`, `version`. Default values are left out of the URL, so the sidebar link and the default view are the same URL. One table of names in the hook's JSDoc.

### Part 1 pieces

All in `web/src/lib/navigation/`.

| Piece | Spec |
|---|---|
| `NavigationTracker` | Mounted once inside `BrowserRouter` in `App.tsx`. Keeps a per-tab map in `sessionStorage`: `location.key → { prevKey, path, title }`. On PUSH, `prevKey` is the entry we came from; on REPLACE, the new key inherits the old entry's `prevKey`, and on a REPLACE that only changes params it also copies the old key's `useEntryState` values; on POP, nothing. Prune to the newest ~200 keys. The first entry of a tab has key `"default"` and never has a `prevKey`, which is what makes deep links fall back. Keys survive reload because React Router stores them in `history.state`. Wrap every `sessionStorage` call in try/catch; without storage, back falls back to the parent route. |
| `usePageTitle` | Also writes the title into the current entry, so the back label can name the previous page. |
| `useBack(fallback)` | Returns `{ label, href, goBack }`. If the current entry has a `prevKey`: `navigate(-1)` and the previous page's title. Otherwise `navigate(fallback.to, { replace: true })` and `fallback.label`. `fallback` is the parent route, required, so no page can strand the user. |
| `BackLink` | `<a href={href}>` from `useBack`: the previous entry's path if there is one, else `fallback.to`, so middle-click and "open in new tab" open the page the label names. `onClick` calls `goBack`. Text "← {label}", "Tilbage" if the previous page has no title. 44 px target, `data-testid="back-link"`. Laptop and phone look the same; task 73's `PageHeader` decides placement. |
| `useLeave(fallback)` | For leaving after delete or a finished flow: back if there's a previous entry, else `replace` to the fallback. Never pushes. |
| `useUrlState(name, codec, default, { push? })` | Typed read/write of one search param. Keeps other params. Default value removes the param. `replace` unless `push`. Codecs for string, int, ISO week, ISO date, enum. Invalid values read as the default, so an old or hand-typed link never breaks the page. |
| `useEntryState(id, default)` | Like `useState`, but stored in `sessionStorage` under `${location.key}:${id}`, JSON. A fresh visit (sidebar click, new key) starts at the default; back and reload restore. A param-only replace carries the values to the new key (see `NavigationTracker`), so on `SfoPage` changing the week keeps `allDays` open, like the scroll rule below. |
| Scroll restoration | `useScrollRestoration(ref, id)`; `Layout` uses it on `<main>` (add `data-testid="app-main"`), and `ScrollToTop` becomes the same hook on `window` for public pages. Set `history.scrollRestoration = 'manual'`. Save `scrollTop` per entry on scroll (one write per animation frame). On PUSH and on a REPLACE that changes the path: scroll to top. On a REPLACE that only changes params (filter, week): keep the position. On POP: restore, retrying on a `ResizeObserver` until the content is tall enough, for at most 2 s, and stop as soon as the user scrolls, touches or presses a key. Inner scrollers (chat lists, week list) opt in with their own `id`. |
| Query cache | Raise `gcTime` to 30 min in `App.tsx`'s `QueryClient`, so going back renders from cache at full height and the scroll can be restored at once. `staleTime` stays 10 s: the page still refetches in the background. |

Not using React Router's `<ScrollRestoration>`: it needs the data router (`createBrowserRouter`), only restores the window, and moving `App.tsx` and the prerender in `entry-server.tsx` off `BrowserRouter`/`StaticRouter` is a bigger change than this task needs. See Q4 for the one thing the data router would add.

### Docs

- `AGENTS.md`, Frontend: one bullet. "Page state that should survive back goes in `useUrlState` or `useEntryState`, never `useState`. Back buttons are `BackLink` with a parent route as fallback; never `navigate(-1)` or a hard-coded link to the list."
- An ADR "Back is browser back" with the table above and why not the data router. Add it to [docs/adr/INDEX.md](../docs/adr/INDEX.md).

## Parts

### 1. Hooks, `BackLink`, scroll restoration

Everything in [Part 1 pieces](#part-1-pieces), the `gcTime` change, the `AGENTS.md` bullet and the ADR. Fixes the scroll bug on every page at once, since `Layout` owns `<main>`. `ClassesPage` and `SchemaBuilderPage` are the first users, so the hooks are tested on a real flow before the other pages move: `expandedClass` to `useEntryState` (or keep `localStorage`, see Q3), the skema and ugeplan links stay plain navigations, and `SchemaBuilderPage`'s back link becomes `BackLink`.

`navigation.spec.ts` (one file for the flow, `data-testid` only):

1. Klasser: expand a class, scroll `app-main`, open its skema, click `back-link`: the class is expanded and `scrollTop` is within a few pixels of before.
2. Same with `page.goBack()`.
3. Fresh browser context straight to `/klasser/:id/skema/:schemaId`: `back-link` says "Klasser" and goes to `/klasser`; `page.goBack()` then doesn't return to the skema.
4. A new page opened from a scrolled list starts at the top.

Debugskolen may not have enough classes to scroll on a laptop viewport; use a short viewport in the test rather than seeding more data.

### 2. Back buttons and leaving

Replace the back buttons on the other 8 pages with `navigate(-1)` or a fixed list link from [Why](#why) with `BackLink`, each with its parent as fallback, and the after-delete `navigate` calls with `useLeave`. Remove the hand-written arrow SVGs. The in-page back buttons wait for the history entries they go back to: "← Tilbage til versioner" moves in part 3 with `version`, the master/detail ones in part 4. If task 73 part 7 has landed, the back link goes in `PageHeader`'s slot.

| Page | Fallback |
|---|---|
| `WeekPlanPage`, `ClassTimeSlotsPage` | Klasser `/klasser` (ClassTimeSlots: the skema, if `schemaId`) |
| `StaffSchedulePage` | Medarbejdere `/medarbejdere` |
| `RoomSchedulePage` | Lokaler `/lokaler` |
| `SubstituteCoverDetailPage` | Vikardækning `/vikardaekning` |
| `VacationRegistrationDetailPage` | Ferieindmelding `/ferieindmelding` |
| `AttendancePage` | Fravær `/fravaer` |
| `BackofficeTenantDetailPage` | Alle skoler `/backoffice/tenants` |

Check the routes against `App.tsx` and the titles against each page's `usePageTitle` while doing it. Grep `Link to=` and `navigate(` for list routes again; this table is from 2026-11-17.

### 3. Page state into the URL and the history entry

| Page / component | State | Moves to |
|---|---|---|
| `WeekPlanPage`, `ParentWeekPlanPage`, `SfoPage` | `isoYear`, `isoWeek` | `uge`. If #89 merged, make `useWeekNavigation` URL-backed and every week page gets it at once. |
| `SfoPage` | `allDays` | `useEntryState` |
| `WeekPlanList` | `openDays` | `useEntryState` |
| `AttendanceTab` | `date` | `dato` (same name `AttendancePage` uses) |
| `AbsenceRegisterTab` | `from`, `to` | `fra`, `til` |
| `AbsenceStatsTab` | `year`, `quarter` | `aar`, `kvartal` |
| `CalendarPage`, `ParentCalendarPage` | `schoolStartYear` | `aar` |
| `StudentsPage` | `filterClassId` | `klasse` |
| `FileSystemBrowser` | current folder, `folderTrail`, `filterCourseId` | `mappe` with `push` (back goes up a folder); trail from the API if it returns ancestors, else `useEntryState`; course filter `fag`. `FilesPage` and `BoardFilesPage` both use it: check two browsers on one page don't share params. |
| `ComplianceCoveragePage` | `tab`, `selectedSnapshotId` | `fane`, `version` (push: "← Tilbage til versioner" becomes browser back) |
| `ParentSchemaPage` | `selectedStudentId` | `elev` |
| `ParentDirectoryPage` | `expandedStudentId` | `useEntryState` |
| `ParentContactBookPage` | `search`; the `<details>` | `soeg`; controlled `<details>` with `useEntryState` |
| `ClassesPage` | `expandedClass` | done in part 1 |
| Lists from #89 (`PersonList`, data table) | search, filter, sort | `soeg` and one param per filter, inside the shared component, so every list gets it |

Not in scope: wizard steps (`SchoolSetupWizardPage`, `ImportPage`, `ClassRolloverPage`): back in the middle of a wizard is a different question. `SchemaBuilderPage`'s side panel and drag state stay `useState`.

Extend `navigation.spec.ts`: ugeplan in week +2, open another page, back: same week; reload: same week. Fravær tab with a chosen date: back keeps it.

### 4. Phone master/detail

`MessagesPage` (`tab` → `fane`, `selectedId` → `besked`), `ContactBookPage` (`selectedThreadId` + `showMobileMessages` → `traad`), `ParentContactBookPage` (`selectedThreadId` → `traad`), `ClassChatPage` (`selectedClassId` → `klasse`). Selecting a thread pushes, so the phone back gesture returns to the list. On laptop, where list and thread are side by side, selecting replaces, so back leaves the page instead of stepping through every thread read. The in-page "Tilbage" buttons become `BackLink` with the list (same route, no param) as fallback. Each chat's message list opts into scroll restoration with its own `id`, and keeps scrolling to the newest message on a fresh open.

Test at 375 px in `navigation.spec.ts`: open a thread, `page.goBack()`, the list is shown and the page is still Beskeder.

### 5. Back closes dialogs on phone (waits for Q4)

Only if Niels says yes to Q4. In task 73's `Dialog`: opening on phone pushes an entry with a marker in `history.state`; POP to an entry without the marker closes the dialog; closing by X, "Annuller" or after save calls `history.back()` when the marker entry is on top. With `dirty`, back shows "Luk uden at gemme?" and re-pushes the marker if the user stays. Needs 73 part 2 merged.

## Decisions for Niels

Recommendations first. An agent can do parts 1 to 4 with the recommendations if Niels doesn't answer.

- **Q1. Back label: where you came from, or the parent page?** Coming from Oversigt into a vikardækning, the button says "← Oversigt" and goes there, like the phone gesture would. The alternative, "← Vikardækning" going to the list, means the in-app button and the phone gesture disagree, and the list has no state to restore. *Recommended: where you came from*, parent only as fallback. Task 73's laptop breadcrumb (H3) still shows the hierarchy and links to the parent.
- **Q2. Do tabs, week steps and filters add history entries?** If they do, Thomas clicks "næste uge" four times and needs four backs to leave the ugeplan. *Recommended: no (replace)*; only steps that feel like a new screen push (phone thread, folder).
- **Q3. `ClassesPage` remembers the last expanded class across visits in `localStorage`.** Keep that as the default for a fresh visit, or start folded? *Recommended: keep it*, Hanne usually works on the same class, and back restores exactly what she had anyway. Move it to a named preference key so it's not mistaken for page state.
- **Q4. Should the phone back gesture close an open dialog** (task 73's bottom sheet) instead of leaving the page? It's what Android users expect, but it means every dialog touches the history. *Recommended: yes*, as part 5, after 73 part 2. Unsaved-change guards on leaving a page (React Router's `useBlocker`) need the data router and are not in this task; `LessonEditor` autosaves and 73's `dirty` prop covers dialogs.

## Migration checklist

Run in `web/src` when parts 1 to 4 are done. Each should only hit `lib/navigation/`:

```bash
grep -rn "navigate(-1)\|history.back" --include=*.tsx .
grep -rn "← \|aria-label=\"Tilbage\"" --include=*.tsx pages components
grep -rn "scrollTo(0" --include=*.tsx --include=*.ts .
```

And this should come back empty: page state from [part 3](#3-page-state-into-the-url-and-the-history-entry) left in `useState`.

```bash
grep -rnE "useState\(\(\) => getISOWeek|useState\(todayIso|useState\(currentSchoolStartYear" --include=*.tsx .
```

`localStorage` stays for what it's used for today (cookie consent, sidebar groups, markdown drafts, billing and signup), plus the `ClassesPage` preference from Q3. Nothing new goes there.

## Done when

- On laptop and on a 375 px phone, with both the in-app button and the browser or gesture back:
  - Klasser → skema → back: same class open, same scroll.
  - Ugeplan week 49 → another page → back: week 49. Reload: week 49.
  - Filer → folder → subfolder → back: parent folder, same scroll.
  - Beskeder on phone → thread → back: the inbox.
  - Fravær, Fremmøde tab with another date → class attendance → back: same date.
- A detail page opened from an email link or a new tab has a back link to its parent, and never leaves the app.
- A new page opened from a scrolled list starts at the top.
- `navigation.spec.ts` passes; the [migration checklist](#migration-checklist) greps are clean; the `AGENTS.md` bullet and ADR are in.
- `/verify` and `/test` pass (in a cloud session: lint, build and `dotnet build` locally, the rest in PR CI).

---
title: 'Student absence register (fravær) — staff registration, categories, stats'
purpose: 'Turn parent-reported absence into a full student absence register the school can stand behind, so "fravær" can go in the marketing leading line.'
description: >-
  Rework the existing parent→student AbsenceReport feature into a school-kept
  absence register following BEK 1063/2019: teachers note fremmøde at the start
  of the day (and at the end for 7.–10. klasse), every absence has a legal
  category (sygdom / ekstraordinær frihed / ulovligt), and admin gets
  per-student/quarter stats with 10% and 15% ulovligt flags. Staff absence and
  vikar stay in task 38.
status: 'Proposed'
---

# Student absence register (fravær)

## TL;DR

Today only parents can report absence (free-text reason, staff confirm/dismiss).
A child who just doesn't show up is invisible. Build four things:
(1) teacher-noted daily fremmøde per class, (2) the three legal absence
categories replacing Confirmed/Dismissed, (3) stats per calendar quarter with
10% / 15% ulovligt flags and same-day parent notice, (4) half-day absence for
7.–10. klasse. School days come from the calendar. Data is kept for the current
+ previous school year, shown clearly in the UI, downloadable as Excel, with a
1 July warning before deletion. When 1–2 ship, add "fravær" to the leading line
(landing page, meta, og-image).

## Context

Current feature (`AbsenceController.cs`, `AbsenceReport.cs`, `AbsencePage.tsx`,
`parent/ParentAbsencePage.tsx`, tests in `AbsenceTests.cs`):

- Parent reports `Date..EndDate` + optional free-text `Reason` for own child.
- Staff with `EditClassRequirement` on the child's class confirm or dismiss;
  parent is notified (`AbsenceConfirmed` / `AbsenceDismissed`).
- Admin lists reports filtered by class/date. `StatsController` also reads
  absence data.
- Controller writes `AppDbContext` directly — predates the service rule in
  `AGENTS.md`, so this task extracts an `AbsenceService`.

What's missing, as a Danish school hears the word "fravær":

- **No staff registration.** Unreported absence (the ulovligt case schools
  care most about) cannot be recorded at all.
- **No categories.** "Dismissed" is ambiguous: was the child actually present,
  or was the reason not accepted?
- **No overview.** No absence %, no follow-up triggers.
- **No half days.** Leaving before the end of the day can't be recorded.

Not related: ferieindmelding (`VacationRegistration*`) is SFO care during
school holidays. Holidays are not school days, so it never touches absence.

### Legal basis (folkeskoler)

[BEK 1063/2019](https://www.retsinformation.dk/eli/lta/2019/1063) +
[VEJ 9008/2023](https://www.retsinformation.dk/eli/retsinfo/2023/9008):

- **Three categories**: ulovligt fravær; sygdom/funktionsnedsættelse o.l.;
  ekstraordinær frihed (granted by the principal, restrictively, on prior
  application).
- **Notering** (observing presence) daily at the start of the school day, and
  for 7.–10. klasse also at the end. Per-lesson noting is allowed, not required.
- **Registrering** (category in the system) completed within one week of the
  absence. Wrong registrations corrected by the end of the quarter.
- **Ulovligt fravær counts in whole days only**, except 7.–10. klasse: present
  at start + absent at end = half day. No hours.
- **Late arrival**: the principal sets guidelines. Late but within the first
  lesson → as a rule not registered as absence. Later → case-by-case
  (excusable reasons such as public transport delays). Must be correctable
  after the fact.
- **Percentage** = ulovligt fraværsdage / afholdte skoledage in the *calendar
  quarter* (Jan–Mar, Apr–Jun, …). School days vary per quarter because of
  holidays. Always round absence up to the nearest half day.
- **Follow-up**: contact parents *straks* on ulovligt fravær (not necessarily
  same day, well before 10%). At **10%** in a quarter: inform parents and warn
  about sanctions. At **15%**: principal notifies the kommune, which stops
  børne- og ungeydelse for that quarter.
- Only ulovligt fravær has sanctions. Sygdom and ekstraordinær frihed count in
  total absence but not in the 10%/15% figures.

### Frie grundskoler (primary market)

- Per [Danmarks Private Skoler](https://privateskoler.dk/vaerktoej/elever-med-saerlige-behov-og-udfordringer/fravaer-elever/):
  not legally obliged to register absence, but should have a systematic
  overview.
- No numeric 15% notification duty found for frie grundskoler. Staff have the
  general skærpet underretningspligt when ulovligt fravær suggests a child
  needs support ([Friskoleforeningen](https://www.friskolerne.dk/vaerktoejer/undervisning-elever-tilsyn/trivsel-og-underretning/underretningspligten-i-friskoler)).
- **Decision**: build to the folkeskole rules, show the same flags to all
  schools, no per-school-type toggles. Flag copy is neutral ("15% ulovligt
  fravær i kvartalet"). Never state a legal duty in the UI or marketing.

## Decisions (proposed — confirm before implementing)

- **One register, two sources.** Keep a single absence record per student
  per period, created either by a parent (report) or by staff (fremmøde).
  Extend `AbsenceReport` rather than adding a parallel table:
  - `ReportedByParentId` becomes nullable; add `RegisteredByStaffId?`.
    Exactly one of the two is set.
  - Add `Category`: `Illness`, `ExtraordinaryLeave`, `Unauthorized`.
  - Add `HalfDay` (bool, single-day records only; see scope 4).
  - Replace `AbsenceStatus { Reported, Confirmed, Dismissed }` with an
    approval state that only applies to leave requests (see below).
  - New migration via `/add-migration`, including a simple data migration:
    `Confirmed` → `Illness`, `Dismissed` → `Unauthorized`, `Reported` →
    `Illness` (confirmed). No real customer reports exist yet, so no
    backfill care or rollout plan is needed.
- **Parent sick report is final on submit.** No confirm step: it shows up
  pre-filled in the teacher's fremmøde as "Syg — meldt af forælder". Removes a
  click per sick child per day for teachers.
- **Parent leave request needs approval.** Parent chooses "Syg" or "Fri"
  when reporting. "Fri" = `ExtraordinaryLeave` with state
  `Pending → Approved | Rejected`, decided by admin (the principal grants
  frihed, not the class teacher). If rejected and the child is absent anyway,
  fremmøde records it as `Unauthorized`.
- **Staff-registered absence defaults to `Unauthorized`.** The teacher can
  change the category (e.g. parent called in sick by phone → `Illness`).
  Changes allowed until the end of the quarter.
- **Fremmøde is recorded explicitly.** A new `AttendanceCheck` row
  (ClassId, Date, TakenByStaffId, TakenAt) marks "fremmøde noteret for denne
  klasse i dag", so admin can see which classes haven't done it yet. Absent
  students get absence rows. Present students get nothing: no row per
  present child per day.
- **Who registers and who gets follow-up notifications**: admins + staff with
  `EditClassRequirement` on the class (same as today's confirm/dismiss).
  See `docs/AUTHORIZATION.md`.
- **School days** = weekdays minus days covered by a `CalendarEntry` of type
  `Ferie`, `Lukkedag` or `Arbejdsdag` (planlægningsdag / pædagogisk dag, no
  students). Respect `RecurrenceRule` / `ExcludedDates`. `Begivenhed` is still
  a school day. Only count up to today for the running quarter. Stats page
  shows "N skoledage i kvartalet" so a school with a missing holiday entry
  notices.
- **Parents see everything registered on their own child**, including
  staff-registered ulovligt fravær, and are notified when it happens (scope 3).
- **Retention (confirmed).** GDPR sets no fixed period; data may be kept only
  while it serves a purpose ([Friskoleforeningen](https://www.friskolerne.dk/vaerktoejer/administration-og-ledelse/persondata/hvor-laenge-skal-personoplysninger-opbevares):
  delete for former students, keep for a student with significant absence
  to follow up next year).
  - Absence records and `AttendanceCheck` rows are kept for the current +
    previous school year, then deleted automatically (nightly job). The
    school year boundary is 1 August: on 1 August 2027, everything dated
    before 1 August 2025 is deleted.
  - Deleted with the student (already cascades via `StudentId`).
  - Reason field hint: "Skriv ikke diagnoser". Category `Illness` is enough.
  - Coordinate the job with `tasks/06-data-retention.md`; state the period
    in the privatlivspolitik.
  - Download and visible retention: see scope 5.
- **Staff absence stays in task 38.** Different actor, different downstream
  effect. `/fravaer` may later host both as tabs, but models stay separate.

## Scope

### 0. Service extraction

- New `AbsenceService` in `api/Skoleoverblikket.Api/Services/`, registered in
  `AddDomainServices`. It is the only writer of `AbsenceReport` and
  `AttendanceCheck`. `AbsenceController` becomes thin (see `AGENTS.md` →
  "Encapsulation").
- Parent-owns-student check moves into the service.
- `TestDataBuilder` creates absences via the service; update
  `AbsenceTests.cs` / `NotificationsTests.cs` / `StatsControllerTests.cs`
  accordingly.

### 1. Daily fremmøde (staff registration)

- Teacher opens "Fremmøde" for a class + date (default today). They see the
  student list with parent-reported absence pre-filled, tap absent students,
  then press "Gem fremmøde". "Alle er her" is one tap.
- Must work on a phone: Thomas notes it standing in the classroom. Big tap
  targets, no table.
- Entry points: teacher dashboard ("Fremmøde mangler: 3.A") and the class
  page.
- Admin view: today's classes with fremmøde noted / not noted.
- Editing after the fact is allowed (parent calls at 10:00, child arrives
  late within first lesson → remove the absence). Keep `RegisteredByStaffId`
  + an updated-at timestamp; no full audit log in v1.
- Endpoints (English routes): `GET /api/v1/attendance/classes/{classId}?date=`,
  `PUT /api/v1/attendance/classes/{classId}?date=` (body: absent students +
  category + half-day), `GET /api/v1/attendance/overview?date=` (admin).

### 2. Categories + leave approval

- Parent form (`ParentAbsencePage`): choose "Syg" or "Fri" (+ optional
  reason). "Fri" shows "Skolens leder skal godkende".
- Admin `/fravaer`: "Anmodninger om fri" list with Godkend / Afvis. Parent is
  notified. Reuse/rename `NotificationType.AbsenceConfirmed/Dismissed` →
  leave approved/rejected.
- Category shown everywhere as Danish labels: Sygdom, Ekstraordinær frihed,
  Ulovligt fravær.

### 3. Stats + follow-up

- `/fravaer` → "Statistik": per class and per student, for a chosen calendar
  quarter (default: current). Columns: skoledage, sygdom, ekstraordinær
  frihed, ulovligt (days, half days count 0.5) and ulovligt %. Filters: class,
  quarter, category. Charts: absence over time (school-wide / per class),
  category split. Load the `dataviz` skill before building charts.
- Ulovligt % = ulovligt days (rounded up to nearest half day) / school days in
  the quarter so far.
- Flags on the stats list and the student:
  - **≥10% ulovligt**: amber flag + in-app notification to admins + staff with
    class permission ("Orientér forældrene"). "Markér som orienteret" clears it
    for that quarter.
  - **≥15% ulovligt**: red flag + CSV export per quarter listing flagged
    students (for the principal's kommune notification; we send nothing to
    the kommune).
- **Same-day parent notice** (supports the "straks" contact rule): when
  fremmøde registers `Unauthorized` for a child with no parent report that
  day, the parents get a notification/email ("{Barn} er ikke mødt i skole i
  dag, og vi har ikke hørt fra jer"). On for all schools, no setting.

### 4. Half-day absence (7.–10. klasse)

- For classes with `GradeLevel >= 7`, fremmøde has a second step "Ved
  dagens slutning": tap students who have left. Present in the morning +
  absent at the end = half-day absence (`HalfDay = true`).
- Lower grades: whole days only, no end-of-day step. `GradeLevel` null →
  treat as lower grade.
- "Kom for sent" is not a category and not an absence: the teacher removes
  the morning absence if the child arrives within the first lesson, or keeps
  it if the delay was substantial. No separate late-arrival field in v1.

### 5. Retention: download + make it visible

- **Download**: admin can download the full absence register for a chosen
  school year as Excel (reuse `ExcelReportBuilder`): one row per absence
  (student, class, date(s), half day, category, source parent/staff, reason)
  plus a per-student summary sheet per quarter. Available for every school
  year still retained. The quarterly 15% CSV in scope 3 stays separate.
- **Visible everywhere absence data is shown**, one short line, no link maze:
  - `/fravaer` (admin + teachers): "Fraværsdata gemmes i indeværende og
    forrige skoleår. Skoleåret {2024/25} slettes automatisk 1. august {2026}."
    Next to it: "Download skoleår" button.
  - Parent absence page: "Vi gemmer fravær i indeværende og forrige skoleår."
  - Privatlivspolitik: same rule in plain Danish.
- **Advance warning**: 1 July each year, admins get an in-app notification +
  email: "Fravær for skoleåret {2024/25} slettes 1. august. Download det nu,
  hvis I skal bruge det." Link goes to `/fravaer` with the download.
- Nothing is deleted without that warning having gone out at least once for
  that school year (guards against a tenant created in July).

### 6. Marketing copy (after 1 + 2 ship)

- Leading line → "Skema, SFO, ugeplan, fravær og forældrekontakt samlet i ét
  system" in `web/index.html` (description + og:description),
  `LandingPage.tsx` (JSON-LD, SeoMeta, hero), and re-render
  `web/public/og-image.png` (1200×630, same layout).
- Landing page feature card for fravær.
- Update `docs/PRD.md` "Fraværsregistrering" bullet + `AGENTS.md` built
  features list.

## Open questions

- **15% rule for friskoler**: no numeric duty found (see above). If a
  customer or lawyer says otherwise, nothing changes in the product; only
  marketing copy could then mention it.

## Out of scope

- Per-lesson (lektion) attendance.
- Sending notifications to the kommune / Udbetaling Danmark.
- Staff absence and vikar assignment (task 38).
- SFO komme/gå check-in.

## Testing

- Integration: parent report (syg/fri), leave approve/reject, fremmøde
  save + edit, category change, half day for grade ≥7 only, cross-class
  authorization (teacher without edit rights → 403), parent can only see own
  child, tenant isolation, school-day count against calendar entries
  (Ferie/Lukkedag/Arbejdsdag incl. recurrence), ulovligt % with half-day
  rounding, 10%/15% flags, quarterly CSV export, school-year Excel download
  (admin only, own tenant only), retention job deletes only records older
  than previous school year and only after the 1 July warning went out,
  warning notification sent once per school year.
- Playwright: teacher notes fremmøde on phone viewport → parent sees ulovligt
  fravær + notification; parent requests fri → admin approves → parent sees
  approved.

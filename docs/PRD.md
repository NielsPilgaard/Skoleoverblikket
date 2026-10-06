---
title: 'PRD'
description: >-
  Full product requirements — target segments, core v1 feature scope,
  competitive positioning, migration paths, and out-of-scope boundaries.
status: 'Living'
purpose: Canonical source for what the product is, who it's for, and what it does — the document to read before proposing any new feature.
---

# PRD.md — Skoleoverblikket Product Requirements

## Product overview

Skoleoverblikket is a multi-tenant SaaS schema planner for Danish schools. It is sold B2B to schools, not to end users. The target market is all Danish school types: folkeskoler, friskoler, privatskoler, and efterskoler. The product is positioned for schools that want a simpler, lower-cost alternative to Docendo, Skoleplan, Skoleintra, or Forældreintra — or schools setting up a timetable tool for the first time.

## Problem being solved

Danish schools plan their weekly schedules (skemaer) using spreadsheets, whiteboards, or paper. Conflict detection — double-booked teachers, rooms, or aides — is entirely manual. No single tool handles teacher/aide assignment, room allocation, and course-hour tracking in one place at a price point that makes sense for small schools. Existing tools are either expensive (Docendo, Skoleintra), opaque in pricing (Skoleplan), or not available to all school types (Aula is limited to folkeskoler via municipal procurement).

Folkeskoler are not vendor-locked into any single timetable tool. Aula (the national school-home communication platform) is separate from timetable software — folkeskoler choose their timetable tool independently. Skoleoverblikket can serve both school types.

## Target customer

Danish schools that want simple, low-cost timetable administration without requiring IT support or training. This includes:

**Segment A — New schools with no existing system**
- No migration cost or complexity — onboard from scratch
- 50–300 students typical

**Segment B — Schools migrating from Skoleintra / Forældreintra / Elevintra**
- Want something cheaper and easier
- Low technical sophistication among admin users
- Guided migration is a paid consulting add-on service (~3,000 kr depending on school size) — not a self-serve product feature

**Segment C — Schools migrating from Docendo**
- Want to save money
- Already familiar with digital timetabling
- 100–500 students typical

**Segment D — Schools with low technical experience**
- Must be operable without training, without IT department, without support calls
- Admin user is often a school secretary managing all admin tasks solo

**Segment E — Schools wanting something simpler than Skoleplan or Docendo**
- Frustrated by complexity and opaque pricing
- Value transparency and self-serve trial

All five segments span school types. Folkeskoler, friskoler, privatskoler, and efterskoler are all valid customers. Folkeskoler are not the primary focus (they have more vendor inertia) but are not excluded.

## Target end user

Staff at the school. Range from school secretaries managing the full schema to substitute teachers who just need to know where to be. Primary devices are laptops (admin) and phones (teachers checking schedules) — see [Design principles](#design-principles).

---

## Design principles

See [docs/PERSONAS.md](PERSONAS.md) for the concrete users these principles are written for.

**Laptop-first for admin, responsive for all**: the primary admin user builds schemas on a laptop. The schema builder must work well on a laptop-sized screen. Teachers and aides view their schedules on phones and tablets. No feature may require a specific screen size to operate — but the schema builder is optimised for laptop use.

**No surprises**: UI must be predictable and obvious. Prefer one clear action over multiple options. Never use jargon the user has not seen before without an immediate explanation.

**Simplicity over features**: the admin user is often a school secretary with limited time and low technical sophistication. Every feature must be operable without training. Prefer fewer options over configurability.

---

## Core features (v1)

### Schema planner

The core product. Each class (klasse) has its own weekly schema (skema). The school defines a default time slot template — lesson durations and breaks — and each class inherits it. Classes can override individual time slots. See [docs/SCHEMA_FEATURES.md](SCHEMA_FEATURES.md) for full detail.

- Weekly grid view per class
- Assign course (fag) + teacher (lærer) + room (lokale) to each time slot
- Real-time conflict detection: teacher double-booked, room double-booked, aide double-booked
- Per-class time slot overrides (classes are not forced to align)

### Time slot wizard

On school setup, admin defines the default lesson structure via a guided wizard:

- Default lesson duration (e.g. 45 min)
- Breaks between lessons (optional — some schools have fixed breaks, others don't)
- Generates the school's default weekly time slot grid
- Per-class overrides allowed after initial setup

### Staff management

- Staff register: teachers (lærere), aides (pædagoger), substitutes (vikarer)
- Each staff member has a role, contact info, and assigned courses
- Staff onboarding via invitation flow (email invite → Keycloak account)

### Class and course management

- CRUD for classes (klasser): e.g. 2.b, 5.a, 9.a
- CRUD for courses (fag): e.g. dansk, matematik, idræt
- Courses are linked to classes via the schema

### File explorer

- Upload files (PDFs, documents) to the platform
- Link files to courses for easy reference
- Browse files by course
- Storage: OVHCloud Object Storage (S3-compatible, EU)

### Ugeplan (weekly plan)

- Per-class weekly plan, separate from the schema grid
- File attachments per time slot (worksheets, materials)
- Visible to parents in the parent portal

### Vikardækning (substitute coverage)

- One-click lookup of free vs. busy staff for a given time slot when a teacher or aide is absent
- Busy staff shown with the reason (which class/slot they're already booked on)
- Assign a substitute teacher or aide directly from the lookup

### Bestyrelse (board module)

- Separate space for the school board, distinct from staff and parent access
- Board member invitations (email invite → Keycloak account)
- Board-only file storage, not visible to staff or parents

### Stå mål med (compliance publishing)

- Lets friskoler publish their own teaching goals and plans per course and grade level on the school's website, satisfying the Friskoleloven §1c website-publication duty — §1a itself defines what the goals and teaching plans must contain, and publication alone does not establish §1a compliance
- Fælles Mål applies automatically whenever the school has not published its own goals and plans
- See [tasks/completed/19-staa-maal-med.md](../tasks/completed/19-staa-maal-med.md) for the legal background and [Friskoleloven, LBK nr 1245 af 08/10/2024](https://www.retsinformation.dk/eli/lta/2024/1245/pdf)

### CSV import

- Bulk import parents and students onto existing classes
- Admin-only, with per-row validation warnings
- Reduces manual data entry when onboarding a new class or migrating from a spreadsheet

### Stats and reporting

- Admin dashboard: classes with complete schemas, unassigned slots, staffing gaps at a glance
- Hours per course per class (towards minimumstimetal)
- Hours per teacher / aide
- Excel export of staff/teacher hours and UVM timetal comparisons

### Printable schema

- Print-friendly weekly schema views: per class, per teacher, per room
- Schools print and physically post schedules — this must work without the app

### Admin dashboard

- School overview: class count, staff count, schema status
- Quick access to schema builder, staff list, course list

### SFO week plan

Weekly SFO schedule (separate from the academic schema). Used by SFO staff to plan their week.

- Weekly grid with shifts per day
- Print view for posting in the SFO
- Separate from the class schema builder

### Parent module

Parents log in to view their children's schedule and communicate with school staff.

- Parent invitation flow (email invite → Keycloak account)
- Read-only views: class schema, school calendar, SFO week plan
- **Kontaktbog**: per-child message thread between parents and class teacher
- **Klassechat**: one group thread per klasse for its parents, the staff on its schema, and admins, with file attachments — replaces the per-klasse Facebook group
- **Fraværsregistrering**: a full student absence register following BEK 1063/2019. Teachers note fremmøde from their phone at the start of the day (and at the end for 7.–10. klasse, giving half days). Every absence has a legal category: sygdom, ekstraordinær frihed or ulovligt fravær. Parents report sickness (final on submit) or request fri (the principal approves). Staff-noted absence defaults to ulovligt, and parents are told the same day. Stats per calendar quarter flag 10% and 15% ulovligt fravær, with a "forældre orienteret" mark and a CSV of 15% students. Data is kept for the current and previous school year, downloadable as Excel, with a 1 July warning before deletion.
- **Kontakt directory**: role-filtered directory of parents (respects ShareContactInfo consent)
- **Beskeder**: flat inbox — any tenant user can message any other (parent → staff always; parent → parent requires consent)
- **Notifications**: in-app bell + email alerts for new messages, absence confirmations, contact book replies. Per-type opt-out in settings.
- **Contact info onboarding**: parent provides phone, address, and consent during invite acceptance
- **Avatar uploads**: optional profile photo for parents, staff, and students
- **Ferieindmelding**: parents submit vacation requests for their children via `ParentVacationRegistrationPage`; admin creates registration windows (with week/day granularity and a deadline), views all submissions, and exports responses to CSV; windows can be opened/closed independently

### Payments and billing

- **Stripe Checkout**: self-serve signup, card payment, auto-renew monthly or yearly
- **30-day free trial**: full access, no payment required upfront
- **Basis plan + optional modules**: see [docs/PRICING.md](PRICING.md) for current tier, module, and pricing detail — do not restate numbers here, they drift
- Schools are never invoiced manually — everything is self-serve

---

## Tech stack

See [docs/adr/](adr/) for full rationale. Summary:

- **API**: ASP.NET Core Web API (C# / .NET 10)
- **ORM**: Entity Framework Core + PostgreSQL (self-hosted)
- **Frontend**: React + Vite + TypeScript + Tailwind CSS
- **Auth**: Keycloak (Docker Compose)
- **Hosting**: OVHCloud VPS + Dokploy + Docker Compose
- **Object storage**: OVHCloud Object Storage (S3-compatible, EU)
- **Email**: Scaleway TEM via SMTP (MailKit)

---

## Competitive context

| Competitor | Strengths                                                   | Weaknesses                                                              |
| ---------- | ----------------------------------------------------------- | ----------------------------------------------------------------------- |
| Skoleplan  | Established, known in friskole market                       | Opaque pricing, no public feature list, no self-serve trial             |
| Docendo    | Feature-rich, integrates into AULA for folkeskoler          | Expensive, complex, requires AULA/STIL integration — overkill for smaller schools of any type |
| Skoleintra | Solid product, good reputation                              | High price, heavy for small schools, unclear friskole-specific support  |
| Aula       | 2.3M users, government-backed, school-home communication    | Communication platform only (not a timetable tool) — procured at municipality level, not available to friskoler or privatskoler. May add timetable features in the future. |

### Why Aula is not a competitor

Aula is a **school-home communication platform** (messaging, calendars, news, attendance), not a timetable builder. It has no schedule builder, no conflict detection, and no course/room/teacher allocation. Folkeskoler that need timetable building use a separate tool (typically Docendo) that embeds into Aula as a widget.

More importantly: Aula is procured by KOMBIT on behalf of municipalities. **Friskoler have no access to Aula** — this is a structural governance exclusion (Brugerportalsinitiativet), not a pricing issue. The entire friskole market is unserved by Aula.

Docendo is the closest functional analog — it is a schema builder used by folkeskoler — but it targets a different institution type and requires AULA/STIL integration to deploy.

## Differentiators

1. Transparent pricing — listed on the website, no sales calls required
2. Simple enough for a school secretary to run without training
3. Real-time conflict detection built into the schema builder
4. Clean, modern UI — no bloat, no legacy design
5. EU-only data storage (OVHCloud, Scaleway)
6. 30-day free trial with full access
7. Serves all Danish school types — folkeskoler, friskoler, privatskoler, efterskoler — with no assumptions about municipal IT infrastructure
8. One tool instead of many — schema, SFO, ugeplan, vikardækning, forældrekontakt, kontaktbog, ferieindmelding, fraværsregistrering, filarkiv, bestyrelse, and stå-mål-med-publicering all live in the same tenant, so a school is not stitching together a spreadsheet, a mail thread, and three separate logins to run its admin

## Product positioning

Skoleoverblikket is affordable school admin software that is dead simple to use. The premise: schools should spend less money and less time on admin, and more time on what staff actually went into education to do — not paperwork, not syncing data between tools that don't talk to each other. Every feature added must reduce total admin burden for the school, not just add a checkbox to a feature comparison table.

## Migration from competitors

The most likely migration paths into Skoleoverblikket:

| Source      | Data available                                         | Migration approach                                                   |
| ----------- | ------------------------------------------------------ | -------------------------------------------------------------------- |
| Skoleplan   | CSV import/export (staff, classes); STIL XML (WS17)    | Provide a CSV import template matching Skoleplan's semicolon format  |
| Docendo     | Per-user iCal export; CSV schedule import              | CSV staff/class import; no bulk schedule export — expect manual work |
| Skoleintra  | Excel schedule import format; JSON/PDF export via SFTP | Excel import template; parse exported data where structured          |
| Spreadsheet | N/A — current state for many schools                   | CSV import template is sufficient                                    |

There is **no national standard timetable exchange format** in Denmark. STIL's SkoleGrunddata (WS17 XML) covers identity and enrollment data only — not schedule structure. Migration from all competitors requires a guided CSV onboarding flow, not an automated sync.

### Recommended migration UX (future feature)

A structured onboarding wizard with CSV upload steps:
1. Import staff list (navn, rolle, email)
2. Import class list (klassenavn, klassetrin)
3. Import course list (fag)
4. Manual schema rebuild (no competitor exports structured timetable data we can reliably parse)

**Migration consulting add-on**: guided migration from legacy tools (Skoleintra, Forældreintra, Docendo) is offered as a paid consulting service (~3,000 kr, depending on school size). This is a sales-driven, manual service — not a self-serve product feature. Do not build automated migration tooling as a free feature.

## AULA integration

Aula has a **widget vendor program** (managed by KOMBIT) that lets third-party tools embed widgets into the Aula SPA. Docendo uses this to display schedules inside Aula for folkeskoler.

**This channel is not viable for Skoleoverblikket's core market.** Friskoler have no Aula instance to embed a widget into — the widget distribution model requires a municipal Aula administrator to activate it per institution. There is no friskole pathway.

However, if Skoleoverblikket ever expands to serve folkeskoler or mixed schools, the AULA widget program is the right integration path. KOMBIT controls vendor approval. Documentation: [aulainfo.dk/footer/widgetleverandoer](https://aulainfo.dk/footer/widgetleverandoer).

**For now**: Aula integration is out of scope. The framing to use with customers is: "Skoleoverblikket is a timetable tool. Aula is a communication platform — they serve different purposes and can be used side by side."

## Future features (post-launch)

Open implementation tasks are tracked in [tasks/todo.md](../tasks/todo.md). Feature-specific task files live in [tasks/](../tasks/).

## Out of scope

- Student grading
- LMS / learning management
- SMS notifications
- Native mobile app
- Aula widget integration (deferred — low priority for v1; relevant only for folkeskoler who want schedule data embedded in Aula)
- STIL/WS17 integration (low priority for v1; targets folkeskoler specifically)
- Accounting software integration

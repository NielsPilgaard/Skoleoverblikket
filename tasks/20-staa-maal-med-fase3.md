---
title: 'Stå mål med — Fase 3 (Årsplan / Undervisningsplan)'
purpose: 'Scope the long-term feature for storing and publishing undervisningsplaner and læringsmål per Friskoleloven §1a, merged into the governance (board) module.'
description: >-
  Fase 3 of the "stå mål med" compliance feature: structured storage and
  public-URL publishing of per-grade-per-course teaching plans and learning
  goals, plus registration of the school's chosen §1a compliance path (A–E).
  Merged into the board module as a combined "Bestyrelse & Tilsyn"
  governance offering rather than a standalone module. No code exists yet.
status: 'Ready'
---

# Stå mål med — Fase 3 (Årsplan / Undervisningsplan)

## TL;DR

Fase 1+2 (structural subject-coverage view + UVM timetal comparison) are
**built and live** — `StaaMaalMedController`. Fase 3 adds `TeachingPlan`,
`TeachingGoal`, `CompliancePath` entities plus a public compliance page.

**2026-08-30**: Landing page and billing copy previously promised Fase 3
("undervisningsplaner", "offentliggørelse af §1a-tilsynsgrundlag"). Those
claims were removed since Fase 3 is unbuilt — marketing now only mentions
the Fase 1+2 coverage/timetal overview. Re-add the copy when this ships.

Key decisions from scoping:

- **Billing**: merged into `SubscriptionModule.BoardModule` (reframed as
  governance module) — no new enum value, one price covers board + compliance.
- **Plan key**: `GradeLevel + Course + SkoleaarStartYear`, not `Class` — matches
  how curriculum actually works and survives årsrul (5.A → 6.A) with zero
  migration since GradeLevel is what rolls.
- **Publishing**: public URL only (`/s/{slug}/staa-mal-med`), gated by a
  year-scoped `PublishedComplianceSnapshot` row per `(TenantId,
  SkoleaarStartYear)` — no school-level published flag. No PDF export.
- **Sti mutability**: one `CompliancePath` per skoleår, freely editable until
  published; published requires explicit re-publish to change.
- **Goals**: freeform text list, no Fælles Mål reference table.
- **Authoring**: Admin (any plan) + lærer (plans for courses/grades they
  teach this skoleår, derived from active `SchemaSlot` assignments).
- **Tilsynsstøtte (doc §3, dropped)**: the public page doubles as the
  tilsynsførende-facing artifact — no separate export.

## Context

Fase 1+2 are done — see `StaaMaalMedController.cs`
(`api/Skoleoverblikket.Api/Controllers/StaaMaalMedController.cs`), which
serves structural coverage data derived entirely from existing `SchemaSlot`
data. `Class.GradeLevel` already exists (task 19's prerequisite #1 is closed).

Fase 3 stores actual teaching plans and goals per grade/course/year, plus
which of five recognized §1a compliance paths (A–E) the school follows —
none of which exists in current schema data. See task 19 §6 ("Fase 3") for
the original framing and §4 ("Regelbaseret vs. AI-assisteret check") for why
this stays structural/documentary rather than an automated quality judgment.

No `Skoleaar` entity exists in the codebase — Fase 1/2 derive year implicitly
per-schema from `Schema.StartDate/EndDate`. Fase 3 needs an explicit year key
since plans exist independent of any one schema; resolved to a plain
`int SkoleaarStartYear` rather than a new entity (cheapest option, consistent
with how `UvmTimetableService` already keys yearly data).

## Scope

### 1. Årsplaner pr. klassetrin og fag

`TeachingPlan` keyed by `(GradeLevel, CourseId, SkoleaarStartYear)` — one plan
per grade-level curriculum per year, covering all parallel classes at that
grade (5.A and 5.B share one "Dansk, 5. klasse" plan). Unique constraint on
`(TenantId, GradeLevel, CourseId, SkoleaarStartYear)`. Keying on `CourseId`
(not `CourseCategory`) matches how `TeachingGoal` authoring is actually
scoped — a course, not a category, has an underviser assigned via
`SchemaSlot` — and means a course rename doesn't change the plan's identity.
If a course's `Category` is later changed, existing plans keyed on that
`CourseId` are unaffected (identity is the course row, not its category);
a lærer's authoring rights are still derived live from their current
`SchemaSlot` assignments for that course, so a recategorization only affects
*future* authorization checks, never orphans a stored plan. This matches how
curriculum is actually organized and avoids duplicate authoring for schools
with multiple classes per grade. It also means the plan survives årsrul
automatically: årsrul increments `Class.GradeLevel`, and since the plan
isn't keyed on `Class` at all, nothing needs to migrate.

Each plan holds:

- Læringsmål for skoleåret — `TeachingGoal`, freeform ordered text list
  (no structured Fælles Mål linkage; tilsynsførende judges adequacy, not
  the system — consistent with task 19 §4)
- Valgte metoder og materialer
- Evalueringsform

**Authoring rights**: Admin can edit any plan. A lærer can edit a plan for
`(gradeLevel, course, year)` if they're assigned as underviser on any active
`SchemaSlot` this skoleår where `Course` matches and `Class.GradeLevel`
matches. Reuses existing schema data — no new teacher↔fag↔trin assignment
table. Board role stays read-only, same as Fase 1/2.

### 2. §1a compliance-stier

`CompliancePath` — one row per skoleår, unique on `(TenantId, SkoleaarStartYear)`,
recording which of the five paths (A–E) the school follows plus supporting
documentation:

- **Sti A**: Folkeskolens Fælles Mål anvendes direkte
- **Sti B**: Skolens egne mål, der svarer til Fælles Mål
- **Sti C**: Mål der i omfang og niveau svarer til folkeskolens
- **Sti D**: Internationale programmer (f.eks. IB)
- **Sti E**: Anden dokumenteret tilgang

Freely editable while unpublished. Once the school publishes (see §3 below),
changing sti requires an explicit re-publish action — the public/tilsyn-facing
record shouldn't silently mutate mid-year, but schools aren't locked out of
fixing a mistake either.

`PublishedComplianceSnapshot` — one row per `(TenantId, SkoleaarStartYear)`,
same uniqueness as `CompliancePath`. Re-publishing must not create a second
snapshot for a year already published: publish is an upsert keyed on
`(TenantId, SkoleaarStartYear)` — replace the existing row's content in the
same transaction rather than inserting a new one, so the public URL for a
given year always resolves to exactly one snapshot.

### 3. Publishing (public URL)

§1a requires publishing on the school's website. New anonymous (no-JWT)
route: `/s/{slug}/staa-mal-med` (or under whatever the existing public-slug
convention is). This is an explicit, scoped exception to "all endpoints
require auth" — resolves tenant via slug→TenantId at the controller boundary
exactly like other slug usage elsewhere.

Publishing is **year-scoped**, not a single school-level bool. A school-level
`IsPublished` flag would leak next year's in-progress drafts the moment last
year's page goes live, and would let a published plan mutate silently if an
admin edits it after publishing. Instead: a `PublishedComplianceSnapshot` row
per `(TenantId, SkoleaarStartYear)`, unique on that pair, written via atomic
upsert when the admin publishes (or re-publishes) that skoleår — insert if no
snapshot exists for the year, else replace the existing row's contents in the
same transaction, so re-publishing can never create a duplicate year-scoped
snapshot. Each row holds an explicit, allowlisted public snapshot payload —
not a raw serialized copy of the `TeachingPlan`/`CompliancePath` records —
projecting only publication-safe fields (læringsmål, metoder/materialer,
evalueringsform, chosen sti + supporting docs). Freeform text fields
(læringsmål, metoder/materialer, evalueringsform, sti documentation) are
validated/sanitized before persistence; the publish action rejects invalid
content rather than persisting it. Same pattern as the coverage-snapshot
feature in
[40-staa-maal-med-annual-snapshot.md](completed/40-staa-maal-med-annual-snapshot.md).
The public page serves the snapshot for the current skoleår if one has been
published; otherwise it serves the latest snapshot with a `SkoleaarStartYear`
prior to the current skoleår. It never serves a snapshot for a future
skoleår, even if one exists (e.g. an admin pre-publishing next year's plan
early). Drafts for the current or next skoleår stay fully editable regardless
of publish state; publishing/re-publishing only ever affects the snapshot for
the skoleår being published, never other years' snapshots. The anonymous GET
resolves slug→TenantId then applies this selection rule against that
tenant's `PublishedComplianceSnapshot` rows — nothing else about the auth
model changes, and the slug is never itself trusted as an authorization
token per AGENTS.md.

Page shows the published snapshot's `TeachingPlan`s grouped by trin, plus
the snapshotted `CompliancePath`, for the skoleår it was published for. This
page **is** the tilsynsstøtte artifact — doc §3 ("Tilsynsstøtte" as a
separate deliverable) is dropped. No PDF export in this phase; revisit only
if real tilsynsførende feedback says the web page isn't enough.

### 4. Billing / module gating

Folded into `SubscriptionModule.BoardModule`, reframed as a combined
"Bestyrelse & Tilsyn" governance module — one Stripe Product with one Price
per billing interval (Monthly/Yearly), one enum value.
Board members, board files, and stå-mål-med Fase 3 all gate on the same
entitlement check. No new `SubscriptionModule` enum value. Rationale:
bestyrelse and tilsyn are the same buyer persona (school governance/oversight),
and this avoids fragmenting entitlement checks across two module flags for
what's ultimately one purchasing decision.

### 5. Data model (new entities)

- `TeachingPlan` — undervisningsplan tied to `GradeLevel` + `Course` +
  `SkoleaarStartYear` (int)
- `TeachingGoal` — freeform læringsmål under a plan, ordered text list
- `CompliancePath` — school's chosen §1a-sti per `SkoleaarStartYear`
- `PublishedComplianceSnapshot` — one row per `(TenantId,
  SkoleaarStartYear)`, unique on that pair, holding the allowlisted public
  snapshot payload; its existence for a year is what gates the public page
  for that year (see §3 selection rule above)

Requires new EF Core migrations and new API endpoints.

## Hvad der IKKE skal implementeres

- Automatisk "stå mål med"-certificering
- AI-vurdering af undervisningskvalitet
- Integration med STUK eller UVM's systemer
- Juridisk rådgivning eller certifikater
- Structured Fælles Mål reference/lookup table
- Separate PDF/tilsynsstøtte export (public page covers this)

## Referencer

Se [task 19](completed/19-staa-maal-med.md), afsnit 6 "Fase 3" og afsnit 4
"Regelbaseret vs. AI-assisteret check" for baggrund og afgrænsning.

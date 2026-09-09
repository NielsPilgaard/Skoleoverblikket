---
title: 'Task 45: Weekplan recap field (Generelt)'
description: >-
  Add a per-week free-text "Generelt" markdown field to WeekPlan and
  SfoWeekPlan so staff can note trips, reminders, and upcoming themes at a
  glance, shown above the day list in parent, print, and staff-edit views.
status: 'Ready for implementation'
purpose: >-
  Finalized spec for task 45, resolved via grilling session — records the
  scope decisions so implementation doesn't re-derive them.
---

# Task 45: Weekplan recap field (Generelt)

## TL;DR

Parents reading a class's weekly plan have no place to see week-level notes
(trips, "remember gym clothes", upcoming themes, callouts) — only per-slot
content exists. Add one nullable markdown field, `Generelt` (max 8000 chars,
same convention as `Beskrivelse`/`Lektier`), to both `WeekPlan` and
`SfoWeekPlan`. Render it as a single block above the day list in parent view,
print views, and the staff edit view. Hidden when empty in read-only views;
always visible (as an editable textarea) in the staff edit view. Same edit
permission as existing slot content — no new permission tier.

The second half of the original task ask — "should weekplan be list-style
with expandable days" — is already true today
(`web/src/pages/parent/ParentUgeplanPage.tsx`): days are stacked vertically,
slot content renders inline, no click-to-expand needed. No work required
there.

## Scope

### In scope

- New column `Generelt` (nullable `string(8000)`, markdown) on `WeekPlan`
  and `SfoWeekPlan`.
- New EF Core migration. Nullable, no backfill/default — old rows read as
  empty and render as hidden.
- API: expose `Generelt` on the relevant DTOs (`WeekPlanDto`, SFO
  equivalent), read + write.
- Frontend, read-only views — render as a single block, top of week (above
  Monday), hidden entirely when null/empty:
  - `web/src/pages/parent/ParentUgeplanPage.tsx`
  - `web/src/pages/UgeplanPrintPage.tsx`
  - SFO's equivalent print view
- Frontend, staff edit view — editable textarea, top of page, above the day
  grid, always visible regardless of content:
  - `web/src/pages/WeekPlanPage.tsx`
  - SFO's equivalent edit view
- Permissions: same as existing slot editing (ClassPermission-gated), no new
  role/permission tier.

### Out of scope

- The "list-style expandable days" ask — already implemented, no changes
  needed.
- Structured/typed callout items (trip vs. reminder vs. theme as separate
  entities) — rejected in favor of a single free-text field, matching the
  existing `Beskrivelse`/`Lektier` pattern.
- Schoolwide (cross-class) announcements — different concept, would overlap
  with Calendar/Beskeder; not this task.

## Decisions (from grilling session, resolved one at a time)

1. **Content model**: single markdown field, not a structured list. Matches
   existing per-slot fields, zero new entity/CRUD.
2. **Scope of field**: per-class `WeekPlan` (and per `SfoWeekPlan`), not
   schoolwide — trips/reminders are class-specific.
3. **UI placement**: one block at top of week, not repeated per day.
4. **Field name**: `Generelt`, 8000 char cap — matches `Beskrivelse`
   convention rather than a shorter bespoke limit.
5. **Edit permission**: same as slot editing, no new tier.
6. **Empty state (read-only views)**: hidden entirely, no placeholder text.
7. **Empty state (staff edit view)**: always visible as an editable
   textarea — staff need an obvious place to add it.
8. **Migration**: plain nullable column, no backfill.
9. **SFO**: included in scope, same field/rules as class `WeekPlan`.
10. **Print views**: included in scope, same placement/hide-when-empty rule.

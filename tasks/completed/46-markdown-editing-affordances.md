---
title: 'Task 46: Markdown editing affordances for weekplan free-text fields'
description: >-
  The weekplan/SFO free-text fields (Generelt, Beskrivelse, Lektier) store
  markdown and render it in parent views, but the editors are bare textareas
  with no formatting help and the print views render the raw markdown source
  as plain text. Add one shared MarkdownTextarea component (taller default,
  tiny B/I/• toolbar, Ctrl/Cmd+B/I, smart list continuation, `* `→`- `
  normalisation) and switch the print views to render markdown so what staff
  type matches what parents and print readers see.
status: 'Ready for implementation'
purpose: >-
  Finalised spec for task 46, resolved via grilling session — records the
  scope and behaviour decisions so implementation doesn't re-derive them.
---

# Task 46: Markdown editing affordances for weekplan free-text fields

## TL;DR

Task 45 added the `Generelt` field and, alongside the existing per-slot
`Beskrivelse` / `Lektier` fields, all three store markdown. Parent views
(`ParentUgeplanPage`) already render them through `ReactMarkdown` with a
restricted tag allowlist. Two gaps remain:

1. **The editors give no formatting help.** They are plain `<textarea>`
   elements. A 58-year-old secretary has no way to know `**bold**` or `- `
   lists work, and typing a multi-line list by hand (re-typing the `- ` on
   every line) is tedious.
2. **Print views render the markdown source as plain text.** `UgeplanPrintPage`
   and `SfoPrintPage` drop the raw string into a `white-space: pre-wrap`
   block, so `**Tur fredag**` prints with the asterisks visible and `- ` lists
   print as literal dashes. Same field, two different renderings.

This task adds one shared `MarkdownTextarea` component, swaps it in for all six
textareas, and switches the two print views to the same `ReactMarkdown` +
allowlist rendering the parent view already uses.

No API changes. No migration. No new fields. Pure frontend.

## Context

- Field inventory (all markdown, all `string(8000)` nullable):
  - `WeekPlan.Generelt` — edited in `web/src/pages/WeekPlanPage.tsx`
    (`GenereltEditor`)
  - `WeekPlanSlot.Beskrivelse`, `WeekPlanSlot.Lektier` — edited in
    `web/src/pages/WeekPlanPage.tsx` (`EditSlotModal`)
  - `SfoWeekPlan.Generelt` — edited in `web/src/pages/SfoPage.tsx`
    (`SfoGenereltEditor`)
  - `SfoWeekPlanShift.Beskrivelse` — edited in `web/src/pages/SfoPage.tsx`
    (`CellModal`)
  - (that is five editors; `Beskrivelse` + `Lektier` in `EditSlotModal` are
    two textareas → six textareas total)
- Existing render allowlist, duplicated in three files today —
  `['p', 'strong', 'em', 'ul', 'ol', 'li', 'br']`. This task centralises it.
- Read-only renderers:
  - `web/src/pages/parent/ParentUgeplanPage.tsx` — already markdown. No change
    beyond importing the shared allowlist.
  - `web/src/pages/UgeplanPrintPage.tsx` — **currently plain text**, switch to
    markdown.
  - `web/src/pages/SfoPrintPage.tsx` — **currently plain text**, switch to
    markdown.

## Scope

### In scope

#### 1. `MarkdownTextarea` component

New file `web/src/components/markdown/MarkdownTextarea.tsx`. A controlled
textarea (`value` / `onChange`, plus `onBlur`, `placeholder`, `rows`,
`maxLength`, `className`, `aria-label`, `data-testid` pass-through) with:

- **Taller default**: `rows` defaults to `5` when not supplied. `resize-y`
  allowed so staff can drag it larger; no autosize.
- **Tiny toolbar** above the textarea: three buttons — **B**, *I*, and a
  bullet-list button (•). Icon-only, `title` tooltips in Danish
  ("Fed", "Kursiv", "Punktliste"). Buttons act on the current selection /
  caret in the textarea below them. Rendered as one bordered segmented
  control (square 8×8 buttons, shared borders, brand focus ring) so it reads
  as a real formatting toolbar rather than loose glyphs.
- **Live preview** below the textarea: renders the current `value` through the
  shared `<Markdown>` wrapper in a bordered gray box labelled
  "Forhåndsvisning" (empty → "Ingenting endnu"). **Shown by default.** A
  "Skjul forhåndsvisning" / "Vis forhåndsvisning" text toggle top-right of the
  toolbar row hides/shows it. The choice is persisted in `localStorage`
  (`markdown-preview-hidden`, `'1'`/`'0'`) and shared across every
  `MarkdownTextarea` instance — hide it once, it stays hidden everywhere until
  shown again. `localStorage` access is wrapped in try/catch (falls back to
  shown).
- **Bold / italic**:
  - Toolbar **B** and `Ctrl/Cmd+B` wrap the selection in `**…**`.
  - Toolbar *I* and `Ctrl/Cmd+I` wrap the selection in `*…*`.
  - No selection → insert the markers and place the caret between them.
  - No toggle-off behaviour — wrapping an already-wrapped selection just
    nests. Keep it simple.
- **Bullet button**: prefixes each selected line (or the current line) with
  `- `. If every targeted line already starts with `- `, still just adds
  another — no toggle.
- **Smart list continuation** (on `Enter`):
  - Current line matches `^(\s*)- ` or `^(\s*)\d+\. ` and has content after
    the marker → new line, same indent, same marker; for numbered lists
    increment the number.
  - Current line is an *empty* marker (`- ` / `1. ` with nothing after) →
    remove the marker and outdent (standard editor behaviour: Enter on an
    empty bullet ends the list).
- **`* ` → `- ` normalisation**: when the user types a space right after a
  leading `*` at the start of a line (i.e. the line becomes `* `), rewrite it
  to `- ` so stored markdown uses one bullet marker consistently. Only at
  line start, only the `*<space>` case — never touch `*` used for emphasis.
- **Undo-safe editing**: all mutations go through `textarea.setRangeText(...)`
  (or `setSelectionRange` + `setRangeText`) and then dispatch a synthetic
  `input` event so React's `onChange` fires and the browser undo stack is
  preserved. Never assign `textarea.value` directly.
- The component is presentation-only: no data fetching, no mutations. Callers
  keep their existing save-on-blur / save-on-Ctrl+S logic.

#### 2. Shared markdown constants

New file `web/src/components/markdown/allowed.ts` (or co-located in the
component file) exporting `MD_ALLOWED_TAGS = ['p', 'strong', 'em', 'ul', 'ol',
'li', 'br']` and a small `<Markdown>` wrapper component
(`<ReactMarkdown allowedElements={MD_ALLOWED_TAGS} unwrapDisallowed>`).
Replace the three local copies (`WeekPlanPage`, `SfoPage`,
`ParentUgeplanPage`) and any others found during implementation.

#### 2b. Drop the amber tint + add save confirmation on Generelt

The `Generelt` editors on `WeekPlanPage` (`GenereltEditor`) and `SfoPage`
(`SfoGenereltEditor`) currently render on an amber wash — `bg-amber-50/60`,
`border-amber-100`, `text-amber-800` uppercase label, `focus:ring-amber-400`.
Remove all of it. Style the block identically to the `Beskrivelse` / `Lektier`
textareas: white background, `border-gray-300`, plain
`text-sm font-medium text-gray-700` label (keep the "Generelt for ugen"
text), `focus:ring-brand-500`. No fill, no accent bar. The `MarkdownTextarea`
from item 1 is used here too, so the toolbar comes for free.

Same de-amber in the print views (item 4/5): `.print-generelt` currently has
`background: #fffbeb; border: 1px solid #fde68a`. Change to a neutral box —
`border: 1px solid #e5e7eb`, no background — or drop the box entirely and just
give it a bottom rule. Match whatever reads cleanest next to the schema table.

**Save confirmation.** The `Generelt` editors save on blur and today the only
feedback is a transient "Gemmer..." and an error line — nothing on success, so
staff can't tell a blur-save landed. Add a small status line under the
textarea with three states:

- pending → `Gemmer…` (gray, `text-xs`)
- success → `Gemt ✓` (green, `text-xs`) that **fades out after ~2s** (render
  it, then clear via `setTimeout`; clear the timer on unmount and on the next
  edit)
- error → `Kunne ikke gemme — prøv igen` (red, `text-xs`) that **stays** until
  the next successful save

This lives in `GenereltEditor` / `SfoGenereltEditor` (or, cleaner, as an
optional `saveStatus?: 'idle' | 'saving' | 'saved' | 'error'` prop on
`MarkdownTextarea` that renders the line — decide during implementation).
The slot / shift modal saves are **not** in scope here — closing the modal on
success is adequate signal for those.

#### 3. Swap editors

Replace the raw `<textarea>` in all six spots with `MarkdownTextarea`:

- `WeekPlanPage.tsx` — `GenereltEditor` (`rows` → default 5),
  `EditSlotModal` Beskrivelse + Lektier
- `SfoPage.tsx` — `SfoGenereltEditor` (`rows` → default 5), `CellModal`
  Beskrivelse

Pass each caller's styling via `className`: the `Generelt` editors get the
neutral white / `border-gray-300` / `focus:ring-brand-500` treatment (per item
2b — no amber), while the per-slot `Beskrivelse` / SFO shift `Beskrivelse`
stay neutral and `Lektier` keeps its blue (`border-blue-200`, blue focus ring).
Keep the existing autosave (`EditSlotModal`) and blur/Ctrl+S save wiring
untouched.

#### 4. Print views render markdown

- `UgeplanPrintPage.tsx` — render `generelt`, `slot.beskrivelse`,
  `slot.lektier` via the shared `<Markdown>` wrapper instead of raw text in
  `pre-wrap` blocks. Keep the print CSS colours (`.print-generelt` amber box,
  `.print-lektier` blue) by wrapping the `<Markdown>` output in the same
  container class; drop `white-space: pre-wrap` where markdown now handles
  line breaks.
- `SfoPrintPage.tsx` — same for `generelt` and `shift.beskrivelse`.
- Verify list / bold rendering survives the print stylesheet (tight
  `margin` on `ul`/`p`, no page-break mid-list is out of scope — just make it
  not look broken).

#### 5. Print pagination: Generelt on its own page, schema on page 2

Task 45's `Generelt` box pushed the landscape schema table over one A4 page —
it now bleeds onto a second page mid-table. Fix by splitting deliberately:
**when `Generelt` is present, page 1 is header + Generelt box only, and the
schema table starts on page 2 with its own compact repeated header.** When
`Generelt` is empty/null, nothing changes — no page break, schema stays on
page 1 exactly as today.

Applies to both `UgeplanPrintPage.tsx` and `SfoPrintPage.tsx`.

- **Page break**: the `.print-generelt` block gets `break-after: page`
  (`page-break-after: always` fallback) — but only render that block, and the
  break, when `generelt` is non-empty (existing `{generelt && …}` guard
  already does this).
- **Repeated header on page 2**: extract the existing `.print-header` markup
  into a small local `PrintHeader` component / helper and render a second,
  compact instance directly above the `<table>` — title + `Uge {isoWeek},
  {isoYear}` only (skip the "Udskrevet …" date on the repeat). Give it a
  class like `.print-header-repeat` with smaller font / thinner rule. This
  instance is always rendered (harmless single line when there's no
  Generelt / no page break — it just sits above the table on page 1).
  - Guard against a stray blank page: the repeated header must not carry its
    own `break-before`. The break comes only from `.print-generelt`'s
    `break-after`.
- **SFO height hack**: `SfoPrintPage` currently forces
  `@media print { .print-table { height: calc(100% - 72px) !important; } }`
  so the table fills the page. With a page break before the table this
  stretches the page-2 table oddly. Gate it: add a `has-generelt` class to
  `.print-page` when `generelt` is present and change the rule to
  `.print-page:not(.has-generelt) .print-table { height: calc(100% - 72px) }`
  — i.e. only stretch-to-fill when the table shares page 1 with nothing else.
  When Generelt forces page 2, let the table height be `auto` and flow
  naturally.
- `UgeplanPrintPage` has no such height hack — just the `break-after` +
  repeated header.

Rationale for the deliberate split over "try to fit one page": Generelt is
free text of unknown length (up to 8000 chars, lists, multiple paragraphs).
Any fit-on-one-page scheme is fragile the moment a teacher writes a long note.
A guaranteed "Generelt = page 1, schema = page 2 (self-contained, own
header)" is predictable, and the schema page stays pinnable to a staffroom
board on its own. The blank lower half of page 1 is acceptable.

### Out of scope

- Autosize / grow-with-content textareas — fixed `rows={5}` + `resize-y`.
- Toggle-off for bold/italic/bullets — wrap-only, keep the logic small.
- A full rich-text / WYSIWYG editor or editor library — keyboard + tiny
  toolbar over a native textarea only.
- Markdown link / heading / blockquote / code support — the render allowlist
  stays `p/strong/em/ul/ol/li/br`; the editor offers nothing the allowlist
  won't render.
- Backend validation changes — 8000-char cap already enforced, unchanged.
- Touching the parent view's existing markdown rendering beyond swapping in
  the shared allowlist constant.
- Migrating already-stored `* ` bullets in existing rows — normalisation is
  input-time only.

## Decisions (from grilling session)

1. **Editor scope**: one shared `MarkdownTextarea`, applied to all six
   free-text weekplan/SFO textareas (both `Generelt` fields + per-slot
   `Beskrivelse`/`Lektier` + SFO shift `Beskrivelse`), not just `Generelt` —
   so parents see consistent formatting everywhere.
2. **Feature set**: bold/italic (wrap, no toggle), bullet-line prefix, smart
   Enter list continuation with empty-marker outdent, `* `→`- ` line-start
   normalisation.
3. **Discoverability**: a tiny icon-only toolbar (B / I / •) above the
   textarea — Ctrl+B alone is invisible to the target user (Hanne, 58,
   secretary).
4. **Undo**: `setRangeText` + synthetic `input` event, no editor library.
5. **Height**: fixed `rows={5}`, `resize-y` allowed, no autosize.
6. **Print parity** (added during implementation review): print views
   currently render the markdown source as plain text — switch them to the
   same `ReactMarkdown` + allowlist the parent view uses, so all three
   read-only surfaces match.
7. **Print pagination**: `Generelt` present → its own page 1, schema on
   page 2 with a compact repeated header; `Generelt` empty → unchanged.
   Deliberate split, not a fit-on-one-page scheme (Generelt length unknown).
8. **De-amber**: the amber tint on the `Generelt` editors and the
   `.print-generelt` box is dropped — style it like the other textareas
   (white, gray border, brand focus ring).
9. **Save confirmation**: `Generelt` blur-save shows `Gemmer…` → `Gemt ✓`
   (green, fades after ~2s) → error line stays until next success. Slot /
   shift modal saves unchanged (modal close is signal enough).
10. **Toolbar styling + live preview** (added post-implementation on review):
    the B/I/• toolbar is a bordered segmented control, not loose glyphs; a
    "Forhåndsvisning" markdown preview renders under the textarea, shown by
    default, toggleable, with the visibility choice persisted in `localStorage`
    (`markdown-preview-hidden`) and shared across all instances.

## Test plan

Per `docs/TESTING.md` — Playwright e2e for the flow that would silently break
Hanne:

- One Playwright spec (`web/tests/` — one file per flow): on `WeekPlanPage`,
  open the Generelt editor, click the bullet button, type two lines, assert
  the textarea value has `- ` on both lines; type `**x**` via the B button on
  a selection; press Enter on an empty bullet and assert the marker is
  removed. Selectors via `data-testid` only.
- No unit tests for the key-handler (private) — exercised through the
  rendered textarea per the "never test private methods" rule.
- `cd web && npm run build` must pass (new component is typed).
- Existing weekplan / SFO e2e specs must still pass unchanged.

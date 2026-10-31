---
title: 'UI variants, round 2: ten more shared components'
purpose: 'Brief for the second design-exploration round: 10 variants each of 10 more UI building blocks, in the same showcase format as round 1, so Niels can pick one per component before they become shared React components.'
description: >-
  Round 1 (2026-10-31) mocked up 10 variants each of tables, selects, the
  parent weekplan, the lesson editor and the file picker. This round does the
  same for modal/bottom sheet, delete confirmation, toast, empty/loading/error
  states, buttons, form field, status badges, tabs/segmented control, the week
  navigator and the page header. Output is one published showcase page, not
  app code.
status: 'Ready'
---

# UI variants, round 2: ten more shared components

## TL;DR

- Build one HTML showcase page with **10 variants for each of the 10 components below** (100 variants), in the same format as round 1, and publish it as a private artifact.
- Mark one recommended variant per component and say why, tied to the personas ([PERSONAS.md](../docs/PERSONAS.md)).
- No app code in this task. The chosen variants become shared components under `web/src/components/ui/` in a follow-up task, together with the round 1 picks.
- Done when: the page is published, every component has 10 working variants with "Bedst til" and pros/cons, and the reply lists the recommended pick per component.

## Why

Every page builds its own buttons, dialogs and states, so the same thing looks and behaves differently from one page to the next. Hanne has to learn each screen anew. Measured in `web/src` on 2026-10-31:

- 66 distinct `className` strings containing `bg-brand-600` (primary buttons and similar).
- 20 files use `<Modal>`, each with its own header, footer and button layout.
- 5 files use `window.confirm` for destructive actions (for example `ClassChatPage`, `ClassTimeSlotsPage`): a browser dialog with no styling and an "OK" button that doesn't say what it deletes.
- No shared toast. "Gemt" feedback is hand-rolled in 5 files; elsewhere saving happens silently, which breaks Hanne's "Gemt!" requirement.
- 28 files hand-roll `animate-pulse` loading skeletons.
- Week navigation (`prevWeek`/`nextWeek`) is implemented separately in at least 3 pages.

## Round 1 format (copy it)

Round 1 is published at <https://claude.ai/artifact/Jw7yR1mhfDjXt1FEgo5x1s> (private to Niels, slug `9139d15e-b6c3-4f85-9732-47f801a8da0e`). Read its HTML with the artifacts `read_artifact` tool and reuse the scaffold (head, icon sprite, switcher script) rather than rebuilding it.

- **One self-contained HTML page.** Tailwind Play CDN (`cdn.tailwindcss.com?plugins=container-queries`) with the `brand` colors and the Lato/Playfair Display fonts from [tailwind.config.js](../web/tailwind.config.js). Danish UI text and realistic data (Hanne Jensen, Thomas Nielsen, 3.A, uge 45).
- **Variant switcher** in each section: numbered tabs, ‹ › buttons, ←/→ keys, a "Vis alle" toggle, and a `#id` per variant (round 1 used `t`, `v`, `u`, `l`, `f`; pick unused prefixes).
- **Telefon / Computer toggle.** Each variant renders in a `.frame` with `@container`; use container variants (`@2xl:`) where the app would use `sm:`/`md:`.
- **Per variant:** number, name, "Bedst til" line, an interactive mockup, 2–3 pros (+) and 1 con (–). Recommended variants get a green "Anbefalet" badge.
- **Shared rules:** touch targets at least 44 px, inputs `text-base` (stops iPhone zoom), text no lighter than `gray-500` on white, color always paired with text or an icon, inline SVG icons (no emoji), real `<button>`/`<label>`/`<input>` elements.
- Plain Tailwind utilities inside the frames, so a picked variant can be lifted straight into React.

## Components

For each one, explore genuinely different approaches, not ten color tweaks. Ideas to start from:

| # | Component | Explore |
|---|---|---|
| 1 | **Modal / bottom sheet** | One shell for every dialog: centered modal vs. bottom sheet on phone vs. full-screen on phone, side drawer on computer, sticky header/footer, close placement, stacked dialogs, scroll behavior with long content. |
| 2 | **Delete confirmation** | Replaces `window.confirm`. Plain-Danish consequence text ("Sletter 3.A's skema for uge 45. Andre klasser påvirkes ikke."), danger button that names the thing, type-to-confirm for big deletes, undo toast instead of a dialog, two-step button, showing what else is affected. |
| 3 | **Toast / "Gemt!"** | Placement on phone vs. computer, auto-dismiss timing, undo action, error toast with "Prøv igen", inline "Gemt ✓" next to the button instead of a toast, stacking several. |
| 4 | **Empty, loading and error states** | Skeletons vs. spinner vs. "Indlæser …" text, empty states with one clear next action, first-use empty vs. "no search results", plain-Danish error with what to do, offline. |
| 5 | **Buttons** | Primary, secondary, danger, ghost, icon-only (with `aria-label`), loading state, disabled with reason, full-width on phone, button groups, one obvious primary per screen. |
| 6 | **Form field** | Label, help text, error text, required/optional marking, character count, inline validation timing, grouped fields, read-only vs. disabled. Ties to Hanne's "forms with too many fields" fear. |
| 7 | **Status badges** | Aktiv, Inviteret, Vikar, Ændret, Syg, Ferie, Udløbet: pill vs. dot+text vs. icon+text, sizes, how many colors, a legend, how they read for color-blind users. |
| 8 | **Tabs / segmented control** | Underline tabs, pill segments, scrollable tabs on phone, tabs with counts, tabs that collapse into a select on phone, tabs vs. a sidebar sub-menu. |
| 9 | **Week navigator** | Round 1 V10 is the baseline. Variants for schema, ugeplan, fravær and SFO: dates under the week number, "I dag"/"Denne uge" jump, week picker on tap, day/week switch, swipe between weeks, showing holidays. |
| 10 | **Page header** | Title, primary action, back link, breadcrumb, the week navigator slot, and how it collapses on phone (sticky, actions into a bottom bar, overflow menu). Must hold Hanne's "one obvious button, not a menu" rule. |

## Steps

1. Read [PERSONAS.md](../docs/PERSONAS.md) and skim round 1's HTML for the scaffold.
2. Look at how each component is done today: grep `web/src/pages` and `web/src/components` (`Modal.tsx`, `window.confirm`, `animate-pulse`, `prevWeek`, `role="tablist"`). Note the worst examples in each section's intro, as round 1 did.
3. Build the page in the scratchpad, one section per component.
4. Check it in a browser once (no console errors, switcher works, Telefon/Computer both render).
5. Publish as a new private artifact titled "Skoleoverblikket – UI-varianter, runde 2". Don't overwrite round 1.
6. Reply with the link, the recommended variant per component, and any current-code problems found on the way.

## Out of scope

- Writing the React components or migrating pages. That's the follow-up task, after Niels picks.
- Dark mode and a print stylesheet.
- New brand colors or fonts.

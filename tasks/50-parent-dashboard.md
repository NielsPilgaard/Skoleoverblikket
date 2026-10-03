---
title: 'Parent dashboard'
purpose: 'Stub for a parent landing page with quick access to the parent features they use most.'
description: >-
  Parents currently land on /foraeldrevisning/skema. Add a parent dashboard as
  the landing page with quick links to skema, ugeplan, kalender, beskeder and
  the school's Links (task 43). Scope to be grilled before implementation.
status: 'Proposed'
---

# Parent dashboard

## TL;DR

New parent landing page replacing the skema redirect. Tiles/quick links to Skema, Ugeplan, Kalender, Beskeder and the school's Links card from [43-links](43-links.md). Phone-first. Needs grilling before build.

## Context

Admin, staff and board each have a dashboard; parents land directly on the schema view (`App.tsx` routes parents to `/foraeldrevisning/skema`). Surfaced while planning [43-links](43-links.md), where parent-facing links needed a home beyond the sidebar.

## Open questions

- Per-child view or combined for parents with several children?
- Show today's schema inline, or only a tile?
- Unread counts (beskeder, kontaktbog) on tiles?
- Which tiles hide when the parent module is not active?

## Depends on

- [43-links](43-links.md) — `LinksCard` component reused here.

---
title: 'Bestyrelse under Stamdata'
purpose: 'Move board member management out of school settings and into its own Stamdata page, next to Medarbejdere and Forældre.'
description: >-
  Board members are managed today by a card at the bottom of Skoleindstillinger,
  where nobody looks for them. Move the card to a new admin page,
  /bestyrelsesmedlemmer, listed as "Bestyrelse" in the sidebar's Stamdata group.
  Frontend only: same API, same features, better place, works on a phone.
status: 'Done'
---

# Bestyrelse under Stamdata

## TL;DR

Hanne looks for the board in the same place as staff and parents: **Stamdata**. Today it hides at the bottom of **Skoleindstillinger**, below logo upload and the school day template. Move `BoardMembersCard` out of [SchoolSettingsPage.tsx](../../web/src/pages/SchoolSettingsPage.tsx) into a new `BoardMembersPage` at `/bestyrelsesmedlemmer`, add a "Bestyrelse" sidebar entry under Stamdata (admin only), and remove the card from settings. No API changes.

## Context

- The card `BoardMembersCard` ([SchoolSettingsPage.tsx:222](../../web/src/pages/SchoolSettingsPage.tsx#L222)) lists board members, shows Konto oprettet / Afventer, toggles *Læreradgang*, removes a member and invites a new one via a modal.
- The sidebar Stamdata group ([Sidebar.tsx](../../web/src/components/Sidebar.tsx)) has Medarbejdere (20), Fag (21), Lokaler (22), Elever (23), Forældre (24), Importer data (25). Board is missing, so admins can't find it.
- Board users' own pages live under `/bestyrelse/...` (`BoardRoute`, `boardOnly` sidebar items). The admin page must not share that prefix, or admin and board routes get mixed up.
- API: `BoardMembersController` (`GET /api/v1/board-members`, `POST invite`, `DELETE {id}`, `PATCH {id}/teacher-data-access`), all `Roles.Admin`. Bulk import already exists as a tab on `/import`.
- No e2e test covers the card today.

## Decisions

- **D1 — Route `/bestyrelsesmedlemmer`**, admin only (`AdminRoute`). Not `/bestyrelse`, which is the board user's own area.
- **D2 — Sidebar label "Bestyrelse"**, group `Stamdata`, `order: 24.5` (after Forældre, before Importer data), `adminOnly: true`, `module: 'board'`: hidden unless the school has `BoardModule` (sold at 300 kr/md, trial counts as active). Sidebar `moduleGated` became `module: 'parent' | 'board'`. Opening the URL without the module shows the page with invites disabled and a link to Abonnement. Server-side enforcement is [58](../58-enforce-board-module.md).
- **D3 — Remove the card from Skoleindstillinger.** No duplicate, no "moved to" notice. One place for each thing.
- **D4 — Frontend only.** Moving the UI doesn't change endpoint logic, so per [AGENTS.md](../../AGENTS.md#encapsulation-thin-controllers-feature-services) extracting a `BoardMemberService` is not required here. `BoardMembersController` still writes to `AppDbContext` directly; that stays a separate refactor.

## Scope

### 1. New page — `web/src/pages/BoardMembersPage.tsx`

- [ ] Move `BoardMembersCard` (state, queries, mutations, invite modal) out of `SchoolSettingsPage.tsx` into the new page file. Delete it and its now-unused imports from settings.
- [ ] Page shell matching `StaffPage` / `ParentsPage`: `usePageTitle('Bestyrelse')`, page heading "Bestyrelse", one line of help text: "Bestyrelsesmedlemmer får adgang til bestyrelsens filer, oversigt og Stå mål med. Slå *Læreradgang* til, hvis de også skal kunne se skemaer og medarbejdere."
- [ ] Primary button "Inviter bestyrelsesmedlem" in the page header (same placement as the invite button on the other Stamdata pages), opening the existing modal.
- [ ] Empty state: "Ingen bestyrelsesmedlemmer endnu" plus the invite button, so Hanne knows the next step.
- [ ] Remove confirmation: the trash button removes access immediately today with no confirm. Add a `confirm()` like `StaffPage` / `ParentsPage` do: `Fjern "{navn}" fra bestyrelsen? Adgangen fjernes.`
- [ ] **Phone layout**: on narrow screens the row (name/e-mail, status badge, Læreradgang, trash) wraps. Name + e-mail on the first line, badge / toggle / trash on the second. No horizontal scroll at 375 px.
- [ ] `data-testid`s: `board-members-page`, `board-invite-button`, `board-invite-name`, `board-invite-email`, `board-invite-submit`, `board-member-row`, `board-member-teacher-access`, `board-member-remove`.

### 2. Routing and sidebar

- [ ] [App.tsx](../../web/src/App.tsx): lazy `BoardMembersPage`, route `path="bestyrelsesmedlemmer"` wrapped in `<AdminRoute>`, next to `foraeldre`.
- [ ] [Sidebar.tsx](../../web/src/components/Sidebar.tsx): new item per D2. Icon: the existing "users" style (two heads) or a simple people-in-a-row icon; must differ from Medarbejdere and Elever.
- [ ] [features.tsx](../../web/src/content/features.tsx): add `'/bestyrelsesmedlemmer': 'board'` to `sidebarRouteFeatures` (tsc fails otherwise). Check the `board` feature card text still matches; update it if it mentions settings.

### 3. Leftovers

- [ ] Grep `web/src` for links or help text that send admins to Skoleindstillinger for the board (setup wizard, dashboard hints, `BillingPage`, `LandingPage`, `BackofficeTenantDetailPage`). Point them to `/bestyrelsesmedlemmer`.
- [ ] Docs: if [docs/AUTHORIZATION.md](../../docs/AUTHORIZATION.md) or [docs/PRD.md](../../docs/PRD.md) say board members are managed in settings, update the sentence.

## Testing

- **Playwright** (new `web/tests/e2e/board-members.spec.ts`, one flow): admin logs in → clicks "Bestyrelse" in the sidebar → invites a member → row appears with "Afventer" → toggles Læreradgang → removes the member after confirming → row is gone. Also assert Skoleindstillinger no longer shows the card. Selectors: `data-testid` only.
- **API**: no change, existing [BoardMemberTests.cs](../../api/tests/Skoleoverblikket.Api.IntegrationTests/BoardMemberTests.cs) stays green.
- Manual: the page at 375 px wide (Chrome devtools), and a staff (non-admin) user doesn't see the sidebar item and is redirected from `/bestyrelsesmedlemmer`.

## Done when

- "Bestyrelse" shows in Stamdata for admins only, and the page does everything the old card did.
- Skoleindstillinger has no board card.
- Removing a member asks for confirmation.
- Page works on a phone without horizontal scroll.
- `/verify` and `/test` pass.

## Non-goals

- Extracting `BoardMemberService` from `BoardMembersController` (separate task).
- Deep link from the new page to the board tab on `/import` (`ImportPage` has no tab query param today).
- Resending invitations or editing a member's name/e-mail.

---
title: 'Links — admin-managed external links for parents, staff and board'
purpose: 'Implementation plan for a per-school list of external links (Aula, fravær, buskort, Facebook groups) shown to the right audience.'
description: >-
  Admins maintain one ordered list of external links per school. Each link has
  title, URL, optional description, an audience (Forældre / Medarbejdere /
  Bestyrelse, any combination) and an optional class restriction that narrows
  which parents see it. Shown on a "Links" sidebar page for every role and as a
  card on the existing admin/staff/board dashboards. Basis feature, not gated.
status: 'Proposed'
---

# Links

## TL;DR

New `ExternalLink` entity (title, URL, description, three audience flags, optional classes, `SortOrder`) owned by `ExternalLinkService`. Admin manages links on the same "Links" page everyone else reads, with Tilføj / Rediger / Slet / ↑↓. Parents see school-wide parent links plus links restricted to their children's classes. Staff and board see links for their audience regardless of class. http(s) only, opens in new tab. Included in Basis.

## Context

Schools send parents to a handful of external tools: Aula, fravær forms, buskort, class Facebook groups, the school's website, SFO payment. Today that knowledge lives in welcome letters and Hanne's head. One place in Skoleoverblikket where the school curates these links saves repeat questions to the office and gives parents a reason to open the app.

## Decisions (from grilling 2026-10-03)

| Question | Decision |
|---|---|
| Audience | Per link: any combination of Forældre, Medarbejdere, Bestyrelse. At least one required. |
| Class targeting | Optional class picker, default "Hele skolen". Only filters **parents**: they see the link if any child is in one of the classes. Staff/board with the matching audience always see it. Picker only shown when Forældre is ticked; server clears classes when parents audience is off. |
| Who manages | Admin only. Class teachers do not manage class links (keeps auth surface small). |
| Fields | Title (required, ≤100), URL (required, ≤2000), description (optional, ≤300, e.g. "Meld fravær før kl. 8"). Generic external-link icon, no icon picker, no favicon fetching. |
| Ordering | `SortOrder` int, ↑/↓ buttons. No drag and drop. |
| URL rules | Absolute `http`/`https` only. Input without scheme (`aula.dk`) gets `https://` prepended. Everything else (`javascript:`, `mailto:`, `tel:`) rejected with 400. Rendered with `target="_blank" rel="noopener noreferrer"`. |
| Placement | Sidebar item "Links" for every role + "Links" card on `DashboardPage`, `StaffDashboardPage`, `BoardDashboardPage`. Parents have no dashboard yet — see [50-parent-dashboard](50-parent-dashboard.md). |
| Admin UI | Admin view of the same Links page (edit controls + audience/class chips), not a Skoleindstillinger section. |
| Billing | Basis for all, no `moduleGated`. |
| Label | "Links". Routes `/links` (staff/admin), `/foraeldrevisning/links` (parent), board equivalent under the existing board route prefix. |

## Scope

### API

1. **Entity** `Models/ExternalLink.cs`: `Id`, `TenantId`, `Title`, `Url`, `Description?`, `VisibleToParents`, `VisibleToStaff`, `VisibleToBoard`, `SortOrder`, `CreatedAt`, `UpdatedAt`, many-to-many `Classes` (EF skip navigation, join table cascades on class and link delete). Tenant query filter in `AppDbContext` like other tenant-scoped entities. Migration via `/add-migration`.
2. **Service** `Services/ExternalLinkService.cs` (sealed, primary ctor, registered in `AddDomainServices`). Only writer of `ExternalLink`.
   - `GetVisibleLinksAsync(keycloakSubject, roles)` → links the caller may see, ordered by `SortOrder`. Rule: admin sees staff-audience links (admin is staff) on read views; parent filter = `VisibleToParents && (no classes || any class ∈ caller's children's classes)`; staff = `VisibleToStaff`; board = `VisibleToBoard`. A user with several roles gets the union.
   - `GetAllLinksAsync()` → admin list with audience flags and class ids/names.
   - `CreateLinkAsync`, `UpdateLinkAsync`, `DeleteLinkAsync`, `ReorderAsync(IReadOnlyList<Guid> orderedIds)`. Create appends at end (`max(SortOrder)+1`). URL normalization + validation in one private helper.
   - Request/response records next to the service.
3. **Controller** `Controllers/ExternalLinksController.cs`, `/api/v1/external-links`, thin per AGENTS.md:
   - `GET /` — any authenticated tenant user → visible links.
   - `GET /all` — admin.
   - `POST /`, `PUT /{id}`, `DELETE /{id}` — admin.
   - `PUT /order` — admin, body = ordered id list (frontend computes ↑/↓ swap).
   - Validation failures → `ValidationProblemDetails` 400.
4. Check [docs/AUTHORIZATION.md](../docs/AUTHORIZATION.md) for how "staff" is identified (no `staff` role constant — staff = non-parent, non-board tenant user?) and add the endpoint to its summary table.

### Web

5. `/codegen` after API.
6. `pages/LinksPage.tsx`: card list (title, description, host shown small, external icon), phone-first, one column. Empty state: "Skolen har ikke tilføjet nogen links endnu." Admin mode: "Tilføj link" button, per-row Rediger / Slet / ↑ / ↓, chips for audience and classes. Edit dialog: Titel, URL, Beskrivelse, checkboxes Forældre / Medarbejdere / Bestyrelse, class multi-select shown when Forældre ticked ("Hele skolen" default).
7. Mount at `/links`, `/foraeldrevisning/links`, board route. Sidebar entries for staff, parent, board groups; hide the item for non-admins when their visible list is empty.
8. `components/LinksCard.tsx` on the three dashboards, hidden when empty, max ~6 links + "Se alle".
9. `data-testid` on list, rows, buttons, dialog fields.

### Tests

10. API integration `ExternalLinksTests.cs` (one file, through HTTP):
    - admin CRUD + reorder roundtrip;
    - parent sees school-wide parent link and own-class link, not other-class link, not staff-only or board-only link;
    - staff sees class-restricted link that has staff audience; does not see parent-only link;
    - board sees board links only;
    - non-admin `POST`/`PUT`/`DELETE`/`PUT /order` → 403;
    - `javascript:alert(1)` and `mailto:` → 400; `aula.dk` stored as `https://aula.dk`;
    - no audience ticked → 400;
    - tenant isolation: other tenant's link invisible and not editable (404).
11. Playwright e2e (one flow): admin creates class-restricted parent link → parent of that class sees it on `/foraeldrevisning/links` with `target="_blank"`; parent of another class does not.

### Docs

12. AGENTS.md "Built features" list: one line for `ExternalLinksController`.
13. [docs/PRICING.md](../docs/PRICING.md) Basis "Included" list: add Links.

## Edge cases

- **Class deleted / all restricted classes deleted**: join rows cascade. A link whose class list became empty must **not** widen to the whole school — store an explicit `RestrictToClasses` flag (or equivalent) so "had classes, now none" means hidden from parents. Admin list shows a warning chip "Ingen klasser".
- **Årsrul**: links follow the class entity through rollover, so a 3.A Facebook group stays with the cohort. Verify rollover keeps class ids rather than recreating classes.
- **Multi-role users** (parent who is also staff/board): union of audiences, no duplicates.

## Out of scope

- Parent dashboard — [50-parent-dashboard](50-parent-dashboard.md).
- Click tracking, favicons, icon picker, link categories/folders.
- Class teachers managing their own class links.
- Per-student targeting, scheduling links to appear/disappear.

## Done when

`/verify` and `/test` pass, admin can add/reorder/delete a link in under a minute without help, and a parent on a phone sees only the links meant for them.

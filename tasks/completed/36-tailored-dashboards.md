---
title: 'Task 36: Tailored Dashboards per User Type'
status: 'Proposed'
description: >-
  Sidebar has grown to ~25 nav items; admin and staff can't tell what they're most
  likely to need. Turn the dashboard into a role-tailored action hub — admin's
  existing /dashboard gets quick actions + attention alerts, staff gets a new
  /mig/oversigt landing with today's schedule and unread counts.
purpose: >-
  Implementation spec for role-tailored dashboards (admin + staff scope).
  Board and parent dashboards are explicitly out of scope for this task.
---

# Task 36: Tailored Dashboards per User Type

## Context

Sidebar ([Sidebar.tsx](../web/src/components/Sidebar.tsx)) has ~25 nav items across 5 groups (Planlægning, Stamdata, Filer & Eksport, Bestyrelse, Kontakt). Too many for a school secretary (Hanne) or a teacher to scan and know where to go. Rather than restructure the sidebar itself, make the dashboard the answer: land the user on a page tailored to their role that surfaces the actions they actually need and flags what needs attention, with the full sidebar as secondary/reference navigation.

**Scope this round: Admin and Staff only.** Board (3 routes already, low complexity) and Parent are out of scope — revisit later if needed.

Roles come from [AuthContext.ts](../web/src/auth/AuthContext.ts): `isAdmin`, `isParent`, `isBoard`, `isSuperAdmin`, `staffRole` (Teacher/Aide/Vikar for non-admin staff). No principal/secretary split exists in the system — admin is a single role.

## Admin dashboard (`/dashboard`, existing — [DashboardPage.tsx](../web/src/pages/DashboardPage.tsx))

Keep existing `OnboardingCard` and stat cards/tables, but restructure page order and add two new sections.

**Layout order (top to bottom):**
1. Onboarding card (existing, unchanged — already hides when complete)
2. **New: attention alerts** — dynamic tiles, each hidden entirely when count is zero:
   - Pending absence reports (parent-reported, awaiting confirm/dismiss)
   - Open vacation registration window (with deadline)
   - Unread beskeder count
   - Unread kontaktbog count
3. **New: quick actions** — fixed icon-tile grid (always shown, not data-dependent):
   - Klasser (opret skema) → `/klasser`
   - Medarbejdere → `/medarbejdere`
   - Kalender → `/kalender`
   - Importer data → `/import`
4. Existing stat cards (Klasser, Medarbejdere, Fag, Lokaler, Skemaer, Klasser u. skema)
5. Existing tables (Timer pr. medarbejder, Klasser med mangler)

**Module gating**: beskeder and kontaktbog alert tiles only apply/query if `hasParentModule` is true (see [useSubscription.ts](../web/src/hooks/useSubscription.ts)) — matches existing `moduleGated` sidebar pattern. Pending absence and vacation registration are not module-gated — absence reporting works independent of the parent module (see backend section below).

## Staff dashboard (new — `/mig/oversigt`)

New landing page for non-admin authenticated users (Teacher/Aide/Vikar), replacing `/mig/skema` as the default landing. `/mig/skema` stays as the full schema view, linked from the dashboard's schedule snapshot.

**Content:**
1. Today's schedule snapshot — simple vertical list of today's lektioner (time, fag, klasse/lokale), link to full `/mig/skema`
2. Unread beskeder count (module-gated, hidden if 0 or module inactive)
3. Unread kontaktbog count (module-gated, hidden if 0 or module inactive)

**Explicitly dropped from this task**: a staff self-report-absence/vikar-request flow. No such feature exists anywhere in the codebase today (absence reporting is parent-reports → admin-confirms only, see [AbsenceController.cs](../api/Skoleoverblikket.Api/Controllers/AbsenceController.cs)). Already tracked separately as [teacher-report-abscence.md](teacher-report-abscence.md) — do not fold into this task.

## Routing changes

### `App.tsx`

- `HomeRedirect`: non-admin, non-parent, non-board authenticated users (i.e. staff) → `/mig/oversigt` (was `/mig/skema`)
- `AdminRoute` fallback (non-admin hitting an admin-only route) currently `Navigate to="/mig/skema"` → `/mig/oversigt`, since a non-admin falling through an admin route is staff, parent, or board — parent/board are redirected away from their own guarded routes by `ParentRoute`/`BoardRoute` before reaching here in practice, so this fallback is effectively staff-only.
- `ParentRoute` and `BoardRoute` fallbacks change from `Navigate to="/mig/skema"` to `Navigate to="/"` (`HomeRedirect`) — `/mig/oversigt` is staff-only (Teacher/Aide/Vikar); a non-parent/non-board caller here may be staff or admin, neither of which should be routed to `/mig/skema` as a final landing, but must never land on the other role's page either. `HomeRedirect` already resolves each role correctly, so route there instead of hard-coding a guess.
- New route: `path="mig/oversigt"` → `StaffDashboardPage` (new component), itself guarded to staff roles only (not admin, not parent, not board)

### `Sidebar.tsx`

The top `Oversigt` nav item (`navItems[0]`, currently `adminOnly: true`, `to: '/dashboard'`) becomes role-aware: same label/position, but `to` resolves to `/dashboard` for admin or `/mig/oversigt` for staff. Remove `adminOnly: true`; filter logic in `visibleNavItems` needs a small adjustment since this item should now show for admin AND staff (not parent/board, which already branch out earlier in the filter).

## Backend changes

### `StatsController.cs`

Extend `DashboardStats` record with new nullable fields (null when parent module inactive):

```csharp
public record DashboardStats(
    int ClassCount,
    int StaffCount,
    int CourseCount,
    int RoomCount,
    int SchemasComplete,
    int SchemasTotal,
    IReadOnlyList<HoursPerCourse> HoursPerCourse,
    IReadOnlyList<HoursPerStaff> HoursPerStaff,
    IReadOnlyList<UnassignedClass> UnassignedClasses,
    int PendingAbsenceCount,
    OpenVacationWindowDto? OpenVacationWindow,
    int? UnreadMessageCount,
    int? UnreadKontaktbogCount);

public record OpenVacationWindowDto(Guid WindowId, DateOnly RegistrationDeadline, int EntryCount);
```

- `PendingAbsenceCount`: count of `AbsenceReport` where `Status == AbsenceStatus.Reported` for the tenant (see `AbsenceController.cs:243` for the status enum usage). Not module-gated — absence reporting works independent of parent module per existing controller auth.
- `OpenVacationWindow`: reuse logic behind `VacationRegistrationController`'s `GET /open` endpoint (`VacationRegistrationController.cs:195-211`), take the earliest-deadline open window if any.
- `UnreadMessageCount` / `UnreadKontaktbogCount`: only computed when tenant has parent module active (check via existing module/subscription lookup used elsewhere for `SubscriptionModulesController`); otherwise `null`. Derive from `InboxMessageDto.ReadAt == null` aggregate (see `MessagesController.cs:105-148`) and per-thread unread logic in `ContactThreadsController.cs:76` — add efficient `CountAsync` queries rather than fetching full message bodies.

### New endpoint: `GET /api/v1/stats/my-dashboard`

New controller or new action in `StatsController.cs`, restricted at the API
boundary to authenticated staff roles only (Teacher/Aide/Vikar) — not just a
frontend guard. Parent, board, admin, and super-admin principals must be
rejected regardless of how the frontend routes them. Scoped to caller's own
`StaffId` via `ITenantContext`:

```csharp
public record MyDashboardStats(
    IReadOnlyList<TodayLektion> TodaySchedule,
    int? UnreadMessageCount,
    int? UnreadKontaktbogCount);

public record TodayLektion(Guid SlotId, TimeOnly StartTime, TimeOnly EndTime, string CourseName, string ClassName, string? RoomName);
```

- `TodaySchedule`: today's `SchemaSlot`s where caller is `TeacherId` or `AideId`, active schema only (same `StartDate <= today && EndDate >= today` pattern as `StatsController.GetDashboard`).
- Unread counts: same module-gating rule as admin endpoint, scoped to caller.

### Codegen

After controller/DTO changes, run `/codegen` to regenerate OpenAPI spec + typed client — never hand-edit `web/src/api/generated/*`.

## Frontend changes

- `DashboardPage.tsx`: add `AlertsSection` and `QuickActionsGrid` components, reorder per layout above, consume new `DashboardStats` fields.
- New `StaffDashboardPage.tsx`: today's schedule list + unread count tiles, consumes `getApiV1StatsMyDashboardOptions()`.
- `Sidebar.tsx`: role-aware `Oversigt` target as described above.
- `App.tsx`: routing changes as described above.
- Tailwind utility classes only, functional components + hooks, generated typed API client — per existing conventions.

## Testing

- API integration tests (tUnit + Testcontainers): `StatsControllerTests.cs` — extend for new `DashboardStats` fields (module-gated null behavior, pending absence count, open vacation window selection) and new `GetMyDashboard` endpoint (schedule scoped to caller, unread counts).
- Playwright e2e: admin dashboard shows quick actions + alerts (seed a pending absence/open vacation window, assert tile appears; assert it's absent at zero); staff lands on `/mig/oversigt` after login and sees today's schedule; sidebar `Oversigt` link resolves correctly per role.

## Known gaps

- **Staff-login Playwright e2e not implemented.** `web/tests/e2e/global-setup.ts`
  seeds only an admin `storageState`, so there is no non-admin staff principal for
  Playwright to log in as. Asserting that staff land on `/mig/oversigt` after login
  needs a seeded non-admin Keycloak user plus a second `storageState` — a shared
  test-harness change beyond this task. Staff routing and access control are covered
  at the API layer instead (`StatsControllerTests.GetMyDashboard_*`), and the admin
  dashboard e2e (`web/tests/e2e/dashboard.spec.ts`) covers quick actions, alert tiles
  and the sidebar `Oversigt` target.

## Out of scope

- Board dashboard tailoring
- Parent dashboard tailoring (parent already lands on `/foraeldrevisning/skema`, unchanged)
- Staff self-report-absence/vikar-request flow (tracked in [teacher-report-abscence.md](teacher-report-abscence.md))
- Sidebar restructuring/regrouping itself — this task solves navigation confusion via the dashboard, not by changing the ~25-item nav tree
- Upsell/teaser treatment for module-gated tiles when parent module is inactive — tiles are simply omitted, no marketing surface

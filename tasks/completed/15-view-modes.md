# View Modes Task

In order to make the app more user friendly, we should introduce view modes to show people exactly what we expect them to want to see:

## View modes:

- Parent (future)
- Board (future)
- Administrator (Secretary, school leader, office)
- Teacher
- Aide (probably same as Teacher)

## Admin

Admins should be able to see everything, including how other view modes perceive the app, and also the dashboard
Vikar dækning, årsrul.

## Parents (future)

Want to see calenders and week plans.

## Teachers (+aides)

Want to see calenders, week plans, missing entries from week plans, their daily routine (where to be when). Also files
Same for aides maybe, but with more focus on just daily routine?
Vikar dækning?

## Board (future)

UVM requirements, stats

It should be possible to assign admins to classes, a teacher can be an admin, any role can be an admin really.

---

## Implementation Plan

### Context

Current state: all authenticated users see identical sidebar and all routes. `Staff.IsAdmin` controls Keycloak `admin` realm role (used for backend `[Authorize(Roles = "admin")]`). `Staff.Role` is `Teacher | Aide | Substitute`. No frontend role-awareness exists.

### Step 1 — Backend: `GET /api/v1/staff/me` endpoint ✅

Already existed. Returns `StaffDto(Id, Name, Email, Phone, Role, IsAdmin, KeycloakSubject)`. Resolves staff by `sub` claim via `ClaimsPrincipalExtensions.GetKeycloakSubject()`. Returns 404 if no match.

### Step 2 — Frontend: current user context ✅

Extended `AuthContext` with `staffRole: StaffRole | null` and `staffId: string | null`. `AuthProvider` fetches `/api/v1/staff/me` on auth via plain `fetch` (outside QueryClientProvider). Values available everywhere via `useAuth()`.

### Step 3 — Sidebar: role-filtered nav items ✅

`navItems` now has `adminOnly?: boolean` field. Sidebar filters to `visibleNavItems` based on `isAdmin`. Admin sees all 9 nav items + footer links. Teacher/Aide sees: Kalender, Mit skema, Filer. Footer setup/abonnement/indstillinger links also hidden from non-admins. Logo links to `/dashboard` for admins, `/mig/skema` for teachers.

### Step 4 — Route protection ✅

`AdminRoute` component in [web/src/App.tsx](../../web/src/App.tsx) redirects non-admins to `/mig/skema`. Wrapped routes: dashboard, klasser, medarbejdere, fag, lokaler, eksporter, abonnement, indstillinger, and all sub-routes. Non-admins at `/` redirect to `/mig/skema`.

### Step 5 — Admin "view as" switcher (optional, lower priority)

Not implemented. Future work.

### Step 6 — Teacher's "My Schedule" page ✅

Already existed as `MySchedulePage` at `/mig/skema`. Full weekly timetable grid, grouped by day, mobile-friendly. Added "Mit skema" to sidebar nav for non-admins.

### Step 7 — Tests ✅

New file: `ViewModeTests.cs`
- `GetMe_ReturnsCorrectStaff_ForAuthenticatedUser` — verifies correct staff returned by subject
- `GetMe_Returns404_WhenSubjectNotLinkedToStaff` — verifies 404 for unknown subject
- `AdminOnlyEndpoint_Returns403_ForTeacherRole` — verifies POST /staff blocked for non-admin
- `AdminOnlyEndpoint_Returns201_ForAdminRole` — verifies admin can create staff
- `GetMe_Returns404_WhenNoSubjectClaim` — verifies 404 when no staff matches default subject

`TestAuthHandler` extended with `X-Test-Roles` and `X-Test-Subject` headers for per-test role control.

All 45 integration tests pass. TypeScript build clean. dotnet format clean. dotnet build 0 warnings.

### What stays future

- Parent view mode
- Board view mode
- Vikar dækning access for teachers (depends on task 14)

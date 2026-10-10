---
title: 'School roles in the API, one login with several roles'
purpose: 'Let one person be staff, parent and board member at the same school with one login, by deciding roles from our own data instead of Keycloak realm roles.'
description: >-
  Today a login holds exactly one role in practice, and which one wins depends on
  invite order. A teacher with a child at the school loses either the staff or the
  parent view. Move every school role (admin, staff, board, parent) to the API:
  roles come from Staff, BoardMember and Parent rows matched on the login, and the
  frontend picks the active role with a switcher and an X-Acting-As header. Keycloak
  keeps login, tenant_id and superadmin.
status: 'Ready'
---

# School roles in the API, one login with several roles

## TL;DR

- **Keycloak authenticates, the API authorizes.** A claims transformation adds `admin`, `staff`, `board` and `parent` role claims from the database for the token's `sub` in the token's school. Those roles in the token are ignored. Only `superadmin` still comes from Keycloak. That is the end state: during D10 Phase A the API still accepts Keycloak `admin` alongside `Staff.IsAdmin` until the backfill is done.
- **One login, several roles.** `GET /api/v1/me` lists the person's roles. A person with more than one role gets a "Vis som" switcher, and the frontend sends `X-Acting-As: staff|parent|board`. The API then applies only that role.
- **Fixes three bugs along the way:**
  - Invites that land on an existing account never add the role.
  - Deleting a board member deletes the shared login.
  - Signup leaves `Staff.IsAdmin = false` for the school's first admin.

## Context

Grilled 2026-10-10 while finishing [58](58-enforce-board-module.md). What happens today when one person has two roles:

| Order | Result |
|---|---|
| Staff first, then a board or parent invite | `KeycloakAdminService.CreateUserAsync` gets 409, returns the existing user, and never assigns the role ([KeycloakAdminService.cs](../api/Skoleoverblikket.Api/Auth/KeycloakAdminService.cs)). Board and parent pages fail. The invite email also shows a "midlertidig adgangskode" that was never set. |
| Board or parent first, then added as staff | The account keeps `board`/`parent`. About 15 `User.IsInRole(Roles.Board/Parent)` branches (Schemas, Staff, StaffAbsence, Substitute, Attendance, ContactThreads, Messages, Notifications, …) treat them as board/parent, and [App.tsx](../web/src/App.tsx) redirects them to the parent/board area. Since 58, they are blocked from everything when that module is off. |

There is also no staff role. "Staff" means "no app role", so code can only ask "is this person not board/parent?".

Other drift: [SchoolSignupService](../api/Skoleoverblikket.Api/Services/SchoolSignupService.cs) creates the admin's `Staff` row without `IsAdmin = true`. Every signup school therefore has a Keycloak admin whose `Staff.IsAdmin` is false. `EditClassAuthorizationHandler` trusts `Staff.IsAdmin`.

Realm roles are global per person, but school roles are per school. A realm role can't say "admin at this school", so Keycloak can't store these roles correctly without Organizations and organization-scoped roles. The database already stores the memberships (`Staff`, `BoardMember`, `Parent` with `KeycloakSubject`) and the invariants (last admin, own admin).

## Decisions

- **D1 — Every school role comes from the database.**

  | Role | Comes from |
  |---|---|
  | `admin` | `Staff.IsAdmin` |
  | `staff` | a `Staff` row |
  | `board` | a `BoardMember` row |
  | `parent` | a `Parent` row |

  Each match is on `KeycloakSubject == sub` and `TenantId == tenant_id` claim. `superadmin` stays a Keycloak realm role, because it's the operator, not a school role. `admin`, `board` and `parent` from `realm_access` are ignored, so a stale Keycloak role grants nothing. Keycloak only does login, `tenant_id` and superadmin.
- **D2 — Where the roles are worked out.** Extend [KeycloakRolesClaimsTransformer](../api/Skoleoverblikket.Api/Auth/KeycloakRolesClaimsTransformer.cs), or replace it with a `SchoolRolesClaimsTransformer`.
  - **The query:** one per request, with all three lookups combined. Cache the result in `HttpContext.Items`, because ASP.NET can call `IClaimsTransformation` more than once per request.
  - **Tenant filter:** `HttpTenantContext` may not see the principal yet at this point. Use `IgnoreQueryFilters()` with an explicit `TenantId == tenant_id claim` filter and a comment, plus a tenant-isolation test: a subject with rows in school A gets no roles when its token says school B.
  - **Export links:** `ExportLinkAuthHandler` sets the `admin` claim itself. Keep it working: its subject is an admin, so the DB lookup gives admin back. Test it.
- **D3 — `X-Acting-As` narrows, never widens.**
  - **Valid values:** `staff`, `parent`, `board`. Any other value → 400.
  - **A role the person holds:** the transformation keeps that role, drops the other two, and keeps `admin` only when acting as `staff`. The header comes from the client, but it can only choose among the roles the database already granted, so forging it gains nothing.
  - **A role the person doesn't hold:** 403.
  - **No header:** the first held role in the order `staff > parent > board`, skipping roles whose module is off. If every role is off, use the first one, so R1 in 58 returns its explanation.
  - **Superadmin:** unaffected.
- **D4 — `ModuleAccessFilter` R1 becomes simpler.** With exactly one acting role per request, R1 is: acting role `board` without `BoardModule`, or `parent` without `ParentModule` → 403. The multi-role logic in 58's D1 (`CanActWith`, reading `[Authorize(Roles)]`) goes away. Staff is never gated.
- **D5 — Role checks.** Branches that mean "not staff" become `!User.IsInRole(Roles.Staff)`. Examples: `IsParentOrBoard()` in `StaffAbsenceController` and `SubstituteController`, and the Board checks in `StaffController`, `SchemasController` and `ClassesController`. Branches that mean "acting as parent" stay `IsInRole(Roles.Parent)`; D3 makes that unambiguous. Add `Roles.Staff = "staff"`.
- **D6 — Frontend.**
  - **`GET /api/v1/me`** (`[Authorize]`, ungated by modules) returns `{ roles: [{ role, active }], isAdmin, isSuperAdmin }`. `active` is false when that role's module is off.
  - **[AuthProvider](../web/src/auth/AuthProvider.tsx)** reads `isAdmin`/`isParent`/`isBoard` from `/me` and the acting role, not from `realm_access`.
  - **Switcher:** a "Vis som" control in the sidebar, shown only when there are two or more roles. A role whose module is off is shown disabled, with "Skolen har ikke længere Bestyrelsesmodulet". The choice is stored in `localStorage` (per device, try/catch), and the generated client sends it as `X-Acting-As`.
  - **Default view:** parent for a parent and board member.
  - **Superadmin:** its existing `viewAs` toolbar ([ViewModeToolbar](../web/src/components/ViewModeToolbar.tsx)) stays as it is, frontend only.
- **D7 — Removing a role never deletes a shared login.** Deleting a `BoardMember`, `Parent` or `Staff` deletes its Keycloak user only when no other `Staff`/`BoardMember`/`Parent` row (in any school) uses that `KeycloakSubject`. Today `BoardMembersController.Delete` and the staff/parent deletes always delete the user.
- **D8 — Invites to an existing account.**
  - **Same school:** link the row to the existing account and assign no realm role. The email says "Log ind med din eksisterende konto" and shows no temporary password.
  - **Different school** (`tenant_id` attribute differs): the invite fails with 409 ProblemDetails "Denne e-mail bruges allerede på en anden skole". The same login in several schools is a separate task (out of scope below).
  - **`realmRole`:** the parameter goes away from `CreateUserAsync`.
- **D9 — Admin moves to `Staff.IsAdmin`.**
  - **Signup:** sets `IsAdmin = true`.
  - **`PATCH /staff/{id}/admin`:** only changes `IsAdmin`. Move it into a `StaffService` per AGENTS.md. `KeycloakAdminService.SetAdminRoleAsync` and `CreateAdminUserAsync`'s role go away.
  - **`EditClassAuthorizationHandler` step 1** ("no Staff row + admin role") goes away. Its step 2 then becomes the same as the admin claim.
- **D10 — Ship in two phases so no admin is ever locked out.**
  - **Phase A:**
    - Signup sets `IsAdmin`.
    - A one-time idempotent startup job lists the members of Keycloak's `admin` role (new Refit method `GET /roles/admin/users`) and sets `IsAdmin = true` on matching `Staff` rows. If Keycloak is down it logs and retries next start.
    - The transformer combines Keycloak `admin` with `Staff.IsAdmin`. Staff/board/parent are already DB-only.
    - Deploy, then check in prod that every school has at least one `IsAdmin` staff member.
  - **Phase B:** stop reading Keycloak `admin` and remove the backfill job. Niels deletes the `admin`, `board` and `parent` realm roles in prod and in [the dev realm](../infrastructure/keycloak/realms/Skoleoverblikket-realm.json) afterwards; the dev seed admin keeps `superadmin`.

## Header forgery (checked 2026-10-10)

- **`X-Test-*` headers:** only read by `TestAuthHandler` in the test project. Production registers only JWT bearer and `ExportLinkAuthHandler`, which needs a single-use token signed with data protection.
- **JWT:** validated against Authority and Audience. `MapInboundClaims = false`.
- **`tenant_id`:** a Keycloak user attribute whose user-profile permissions are `edit: ["admin"]` in the dev realm, so users can't change it in the account console. **Check that the prod realm has the same user-profile config.**
- **`X-Acting-As`:** can only narrow (D3).

## Files to Create/Modify

- `api/Skoleoverblikket.Api/Auth/AuthConstants.cs`: add `Roles.Staff`.
- `api/Skoleoverblikket.Api/Auth/KeycloakRolesClaimsTransformer.cs`: DB roles, acting-as, keep only superadmin from the token (Phase A: combine with admin).
- `api/Skoleoverblikket.Api/Auth/KeycloakAdminService.cs`, `IKeycloakAdminApi.cs`: drop `realmRole`/`SetAdminRoleAsync`, add the admin role members lookup (Phase A), and add a "does this email already exist, and in which tenant" check for D8.
- `api/Skoleoverblikket.Api/Auth/EditClassRequirement.cs`, `EditWeekPlanRequirement.cs`: drop the "no Staff row + admin" path.
- `api/Skoleoverblikket.Api/Tenancy/ModuleAccessFilter.cs`: R1 on the acting role (D4).
- `api/Skoleoverblikket.Api/Services/SchoolSignupService.cs`: `IsAdmin = true`.
- `api/Skoleoverblikket.Api/Services/BoardMemberInvitationService.cs`, `ParentInvitationService.cs`, `StaffInvitationService.cs`: D8 existing-account path and email text.
- New `api/Skoleoverblikket.Api/Services/StaffService.cs` (admin toggle, delete), plus deletes in `BoardMembersController`/`ParentsController`: D7.
- New `MeController` + DTO: D6.
- Controllers listed in D5: replace the "not staff" checks.
- New hosted service for the Phase A backfill.
- `web/src/auth/AuthProvider.tsx`, `AuthContext.ts`, `web/src/App.tsx`, `web/src/components/Sidebar.tsx`, and a new switcher component. The generated client gets `X-Acting-As` through an interceptor. Run `/codegen` after `MeController`.
- `docs/AUTHORIZATION.md`: rewrite the role table (no "no role = staff"), document acting-as, update "Paid modules" R1.
- `api/tests/.../Infrastructure/TestAuthHandler.cs`, `TestDataBuilder.cs`: see Tests.

## Tests

- **Test churn:** 35 test files send `X-Test-Roles` (default `admin`). Once roles come from the database, a test client needs a matching row.
  - **Admin by default:** `TestDataBuilder.CreateSchoolAsync` creates an admin `Staff` row for the default test subject.
  - **Parent/board tests:** those sending `parent`/`board` must create the row through the owning service. Most already do through invites.
  - **`X-Test-Roles`:** goes away, or only feeds `superadmin`.
- **New flow file `MultiRoleTests.cs`:**
  - Staff who is also invited as a parent keeps the staff view without the header, and sees their child with `X-Acting-As: parent`.
  - Deleting that parent keeps the login, and staff access still works.
  - `X-Acting-As` naming a role the person doesn't hold → 403.
  - Acting as board with `BoardModule` off → 403, acting as staff still works.
  - Inviting an email that belongs to another school → 409.
  - Tenant isolation: rows in school A, token for school B → no roles.
  - Export link still works.
  - Signup admin has `IsAdmin = true`, and admin endpoints work without a Keycloak admin role.
- **Playwright:** a teacher-parent switches with "Vis som" and sees the parent schedule, then switches back.

## Out of scope

- One login across several schools (parent at A, teacher at B). `tenant_id` is single-valued; D8 refuses it for now. That needs a school picker and a token per school or a `tenant_id` from the API, so it's its own task.
- Superadmin `viewAs` sending `X-Acting-As` to see a school's data as a parent.
- Moving superadmin out of Keycloak.

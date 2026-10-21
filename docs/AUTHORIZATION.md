---
title: 'Authorization Model'
description: >-
  JWT + role/ClassPermission authorization: school roles (admin, staff, parent,
  board) read from the database with one acting role per request (X-Acting-As),
  the superadmin-mode/restricted-mode class-editing logic, tenant isolation, paid
  module enforcement, and the endpoint authorization summary.
status: 'Living'
purpose: Reference before touching any endpoint's auth — the ClassPermission superadmin/restricted-mode interaction is non-obvious and easy to get wrong.
---

# Authorization Model

## Overview

Two layers protect every request:

1. **JWT authentication** — Keycloak-issued bearer token required on all endpoints except one: the school ZIP download (`GET /api/v1/exports/school.zip/download`), which is authenticated by a single-use `ExportLink` token instead (see the endpoint table). The `tenant_id` claim is mandatory; a missing claim returns 401.
2. **Role- and resource-based authorization** — what an authenticated user can do depends on their school role, which the API reads from its own database, and, for schema editing, whether `ClassPermission` rows exist.

---

## Roles

Keycloak authenticates, the API authorizes. `SchoolRolesClaimsTransformer` (`Auth/`) adds the role claims on every request from the rows that match the token's `sub` in the token's `tenant_id` school. School roles in the token are ignored, so a stale Keycloak role grants nothing.

| Role | Comes from | What it means |
|---|---|---|
| `staff` | a `Staff` row | Staff member (lærer, pædagog, vikar). Read-only or week-plan write access, plus `EditClass` per the ClassPermission rules. |
| `admin` | `Staff.IsAdmin` | Full administrative access. Only present while acting as staff. See below for interaction with ClassPermissions. |
| `parent` | a `Parent` row | Parent portal. Needs `ParentModule` (R1 under Paid modules). |
| `board` | a `BoardMember` row | Board area. Needs `BoardModule`. |
| `superadmin` | Keycloak realm role | The operator, not a school role. Back office only. |

A login with none of these rows has no role and is refused by every endpoint that names one.

`StaffRole` (Teacher / Aide / Substitute) is an informational field on the `Staff` model. It has no effect on authorization — it is only used for display.

**Phase A of task 61 (current).** Keycloak's `admin` realm role still counts alongside `Staff.IsAdmin`, but only for a login that has a Staff row or no other school row. `AdminRoleBackfillJob` copies the Keycloak role to `Staff.IsAdmin` at startup, and revoking admin also removes the Keycloak role. Inside `EditClassAuthorizationHandler`, `Staff.IsAdmin` stays authoritative for anyone with a Staff row. Phase B stops reading the Keycloak role.

### One login, several roles

A person can be staff, parent and board member at the same school with one login, for example a teacher with a child at the school. Each request acts as exactly one school role:

- **`X-Acting-As: staff|parent|board`** picks it. Any other value is 400. A role the person doesn't hold is 403. The header can only narrow to a role the database already grants, so forging it gains nothing.
- **Without the header** the request acts as the first held role in the order staff, parent, board, skipping roles whose module is off. If every role is off, the first one, so R1 can explain why.
- **`admin`** is only kept when acting as staff.
- **`GET /api/v1/me`** lists the held roles, whether each one's module is on, the default acting role, `isAdmin` and `isSuperAdmin`. The frontend shows a "Vis som" switcher in the sidebar for two or more roles and sends the choice on every request.
- **Removing a role never deletes a shared login** (`SchoolLoginService`): deleting a `Staff`, `BoardMember` or `Parent` row deletes the Keycloak user only when no other row in any school uses it.
- **Inviting an existing login** links the new row to it and emails "Log ind med din eksisterende konto" instead of a temporary password. A login that belongs to another school gets 409 "Denne e-mail bruges allerede på en anden skole"; one login across several schools isn't supported yet.

---

## What admins can access

Admins can always:

- Create, update, and delete **classes**, **staff**, and **ClassPermissions** (`[Authorize(Roles = "admin")]` endpoints)
- Read all classes and staff in the tenant
- Grant or revoke admin permission on other staff members (cannot remove own admin or last admin)

For **schema editing** (creating schemas, setting date ranges, upserting/deleting slots, copying schemas), admin access is governed by the `EditClass` policy — see ClassPermissions below.

---

## ClassPermissions

`ClassPermission` rows grant a specific staff member the right to edit a specific class's schema and week plan. The presence or absence of *any* rows in the tenant changes the entire authorization mode.

### Superadmin mode (no ClassPermission rows exist)

When the `ClassPermissions` table has **zero rows** for the tenant, all authenticated admins can edit all classes. This is the default state for a new tenant — no setup required to get started.

### Restricted mode (ClassPermission rows exist)

Once the first `ClassPermission` row is created, only admins with an **explicit row** for that class can edit it. Admins without a row for a given class receive 403.

Revoking the **last** permission row returns the tenant to superadmin mode.

### Decision logic (evaluated per request in `EditClassAuthorizationHandler`)

```
1. Not acting as staff (a parent or board member, or staff acting as one)
   → Deny (403)

2. No Staff row for this Keycloak subject AND has admin role claim
   → Succeed (admin not enrolled as staff; Phase A only)

3. Staff row found AND Staff.IsAdmin = true
   → Succeed (admin always has full access)

4. No ClassPermission rows exist for this class (class-level open mode)
   → Succeed for all authenticated staff

5. ClassPermission row exists for (StaffId, ClassId)
   → Succeed

6. Otherwise → Deny (403)
```

Step 4 is a **class-scoped** check: a class with no permission rows is editable by any authenticated staff member, even when other classes in the tenant are restricted. This lets admins lock down individual classes without affecting unrestricted ones.

This policy (`Policies.EditClass`) is enforced on all mutating schema endpoints in `SchemasController`:
- Create / rename / delete schema
- Set date range, copy schema
- Upsert / delete schema slots

---

## What non-admin staff can access

Non-admin staff can always:

- **Read** all classes and their schemas (`GET /api/v1/classes`, `GET /api/v1/classes/{id}`)
- **Read** week plans (`GET /api/v1/classes/{classId}/ugeplan`)
- **Write** week plan slots (`PUT /api/v1/classes/{classId}/ugeplan/slots`) and attach/remove files — these endpoints only require `[Authorize]`, not the `EditClass` policy
- **Cannot** create, update, or delete classes or staff

For **schema editing**, non-admin staff follow the class-scoped `EditClass` policy:
- A class with **no** `ClassPermission` rows → any authenticated staff can edit its schemas
- A class with `ClassPermission` rows → only staff with an explicit row for that class can edit its schemas

When ClassPermission rows exist for any class, the class list is filtered for non-admin staff: classes that have permissions rows but none matching this staff member are hidden from `GET /api/v1/classes`. Classes with no permission rows remain visible.

### Staff assigned to a timeslot/module without a ClassPermission

A staff member can appear in a schema slot (as the assigned teacher) without having a `ClassPermission` row for that class. In that case:

- They **can** read the week plan for that class — the week plan endpoint only requires authentication, not a class permission
- They **cannot** edit the underlying schema (no `EditClass` access) — they would need an explicit `ClassPermission` row for that class
- They **can** write week plan annotations (beskrivelse, lektier, file attachments) on their own slots, because `WeekPlanController` does not enforce the `EditClass` policy

In practice: a teacher assigned to a class can annotate lessons week-by-week, but cannot restructure the schema itself.

---

## Tenant isolation

All data is tenant-scoped. Every entity implementing `ITenantScoped` has a global EF Core query filter applied:

```csharp
HasQueryFilter(e => e.TenantId == tenantContext.TenantId)
```

`TenantId` is resolved from the `tenant_id` JWT claim at the middleware boundary. It is never passed as a method parameter through business logic and never inferred from URL slugs.

`ClassPermission` rows are also tenant-scoped — the superadmin-mode check (`AnyAsync()`) only considers rows within the current tenant.

---

## JWT claims required

| Claim | Required | Purpose |
|---|---|---|
| `sub` | Yes | Identifies the user; matched against `KeycloakSubject` on `Staff`, `BoardMember` and `Parent` to work out the school roles |
| `tenant_id` | Yes | Resolves the tenant; missing → 401. Only rows in this school give roles |
| `realm_access.roles` | No | Only `superadmin` is read (and `admin` during Phase A of task 61), by `SchoolRolesClaimsTransformer` |
| `email` | No | Display only |

---

## Endpoint authorization summary

| Endpoint group | Required | Note |
|---|---|---|
| `GET /api/v1/classes` | `[Authorize]` | Results filtered by ClassPermission when rows exist |
| `POST/PUT/DELETE /api/v1/classes` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/me` | `[Authorize]` | The caller's school roles and acting role. Not gated by modules |
| `GET /api/v1/staff/me` | `[Authorize]` | Returns own staff record |
| `POST/PUT/DELETE /api/v1/staff` | `[Authorize(Roles = "admin")]` | |
| `PATCH /api/v1/staff/{id}/admin-permission` | `[Authorize(Roles = "admin")]` | Sets `Staff.IsAdmin`. Cannot remove self or last admin |
| `GET/POST/DELETE /api/v1/classes/{id}/permissions` | `[Authorize(Roles = "admin")]` | |
| Schema mutations (`SchemasController` writes) | `EditClass` policy | Enforces ClassPermission logic |
| `GET /api/v1/classes/{id}/ugeplan` | `[Authorize]` | Any authenticated user |
| `PUT /api/v1/classes/{id}/ugeplan/slots` | `[Authorize]` | Any authenticated user — no EditClass check |
| `POST/DELETE /api/v1/classes/{id}/ugeplan/slots/{slotId}/files` | `[Authorize]` | Any authenticated user |
| `POST /api/v1/absence`, `GET mine`, `DELETE {id}` | `[Authorize(Roles = "parent")]` | Own children only, checked in `AbsenceService` |
| `GET /api/v1/absence`, `GET stats` | `[Authorize(Roles = "staff")]` | Filtered to classes the caller passes `EditClass` for (admins: all) |
| `PUT /api/v1/absence/{id}/category`, `POST follow-ups` | `EditClass` on the student's class | |
| `GET leave-requests`, `POST {id}/approve`, `POST {id}/reject`, `GET export`, `GET flagged-export` | `[Authorize(Roles = "admin")]` | The principal grants ekstraordinær frihed |
| `GET/PUT /api/v1/attendance/classes/{classId}` | `EditClass` on the class | Requires a staff row, so parents/board are rejected |
| `GET /api/v1/attendance/overview` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/attendance/mine/pending` | `[Authorize(Roles = "staff")]` | The whole `AttendanceController` is staff-only |
| `POST /api/v1/staff-absences`, `GET mine`, `DELETE {id}` | `[Authorize(Roles = "staff")]` | Staff report themselves; admins may report for anyone and delete any |
| `GET /api/v1/staff-absences`, `GET {id}`, `PUT {id}/substitute` | `[Authorize(Roles = "admin")]` | Whole-school action, no ClassPermission check |
| `GET /api/v1/staff/available`, `GET /api/v1/substitutions/mine` | `[Authorize(Roles = "staff")]` | |
| `ContactThreadsController`, `ContactDirectoryController`, `MessagesController`, `ClassChatController` | `[Authorize(Roles = "staff,parent")]` | Board members don't use these |
| `PUT /api/v1/week-plans/{id}/slots/{slotId}/substitute` | `EditClass` on the week plan's class | |
| `GET /api/v1/exports/school.zip`, `POST school.zip/link` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/exports/school.zip/download?token=…` | `ExportLink` scheme, admin | The only endpoint not authenticated by a Keycloak JWT. The token comes from `POST school.zip/link`, carries the admin's tenant and subject (data protection, signed and encrypted), lasts one minute and works once (`ExportLinkAuthHandler`) |

---

## Paid modules

`BoardModule` and `ParentModule` are enforced in the API by `ModuleAccessFilter` (`Tenancy/`), a global filter that runs after `SubscriptionAccessFilter`. A trial counts as having every module, and so does a school with no `Subscription` row yet. Every 403 is a `ProblemDetails` titled "Modulet er ikke aktivt".

- **R1, users.** A request acting as board needs `BoardModule`, one acting as parent needs `ParentModule`. Each request acts as one role (see "One login, several roles"), so a teacher who is also on the board keeps working as staff when the board module is off. Staff is never gated. Exempt: `GET /api/v1/modules`, `GET /api/v1/me` and `[AllowAnonymous]` actions.
- **R2, features.** Actions marked `[RequiresModule(X)]` reject `POST`/`PUT`/`PATCH` without module X. `GET` and `DELETE` still work, so a school that drops a module can read and clean up. `GatesReads = true` also rejects `GET`, for a view that computes the feature from Basis data rather than returning stored data. An expired subscription blocks `DELETE` first, in `SubscriptionAccessFilter`.
- **R3, invitations.** `BoardMemberInvitationService.AcceptAsync` and `ParentInvitationService.AcceptAsync` check the inviting school's module, because the accepting user has no tenant claim yet.

Marked with `[RequiresModule]`:

| Module | Controller | Gated writes |
|---|---|---|
| Board | `BoardMembersController`, `BoardFilesController` | all writes |
| Board | `ImportsController` | `POST board-members` |
| Board | `ComplianceCoverageController` (Stå mål med) | all writes, and `GET coverage` (the live view) |
| Parent | `ParentsController`, `StudentsController`, `AttendanceController`, `ContactThreadsController`, `MessagesController`, `ClassChatController` | all writes |
| Parent | `ParentInvitationsController` | `POST {parentId}/resend` |
| Parent | `ImportsController` | `POST students-and-parents` |
| Parent | `AbsenceController` | `PUT {id}/category`, `POST {id}/approve`, `POST {id}/reject`, `POST follow-ups` |

The frontend mirrors this. A board member or parent whose module is off, and who holds no other role with its module on, sees "Modulet er ikke aktivt" (`Layout.tsx`) instead of failing pages. Someone with another role is switched to it, and "Vis som" shows the off role disabled. Admins keep Elever, Forældre, Bestyrelse and Stå mål med in the sidebar, with a notice and only read and delete actions. Stå mål med then shows only saved versions.

Not gated: vacation registration windows and the contact directory, which are Basis. A new controller for a paid feature needs the attribute, or the module stops being something a school pays for.

---

## Known gaps

`WeekPlanController` write endpoints (`PUT slots`, `POST/DELETE files`) enforce `[Authorize]` only — any authenticated tenant user can annotate any class's week plan, regardless of ClassPermissions. This is intentional for the teacher annotation flow but means ClassPermissions do not fully restrict week-plan writes.

One login can't reliably hold two roles. Staff has no realm role, so code treats "has `board` or `parent`" as "is not staff". An invite that reuses an existing account doesn't add the role either. A teacher who is also a parent or board member therefore gets only one role, depending on invite order, and is blocked entirely when that role's module is off. [Task 61](../tasks/61-school-roles-in-api.md) moves school roles to the API to fix this.

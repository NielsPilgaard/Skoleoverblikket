---
title: 'Authorization Model'
description: >-
  JWT + role/ClassPermission authorization: admin vs. staff roles, the
  superadmin-mode/restricted-mode class-editing logic, tenant isolation, and
  the endpoint authorization summary.
status: 'Living'
purpose: Reference before touching any endpoint's auth — the ClassPermission superadmin/restricted-mode interaction is non-obvious and easy to get wrong.
---

# Authorization Model

## Overview

Two layers protect every request:

1. **JWT authentication** — Keycloak-issued bearer token required on all endpoints. The `tenant_id` claim is mandatory; a missing claim returns 401.
2. **Role- and resource-based authorization** — what an authenticated user can do depends on their Keycloak role and, for schema editing, whether `ClassPermission` rows exist.

---

## Roles

| Role | Source | What it means |
|---|---|---|
| `admin` | Keycloak realm role, mapped to `ClaimTypes.Role` | Full administrative access. See below for interaction with ClassPermissions. |
| _(no role)_ | Any authenticated JWT | Staff member (lærer, pædagog, vikar). Read-only or week-plan write access only. |

`StaffRole` (Teacher / Aide / Substitute) is an informational field on the `Staff` model. It has no effect on authorization — it is only used for display.

`Staff.IsAdmin` mirrors the Keycloak admin role in the database. It is the authoritative flag inside `EditClassAuthorizationHandler` (guards against Keycloak/DB desync).

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
1. No Staff row for this Keycloak subject AND has admin role claim
   → Succeed (superuser account not enrolled as staff)

2. Staff row found AND Staff.IsAdmin = true
   → Succeed (admin always has full access)

3. No ClassPermission rows exist for this class (class-level open mode)
   → Succeed for all authenticated staff

4. ClassPermission row exists for (StaffId, ClassId)
   → Succeed

5. Otherwise → Deny (403)
```

Step 3 is a **class-scoped** check: a class with no permission rows is editable by any authenticated staff member, even when other classes in the tenant are restricted. This lets admins lock down individual classes without affecting unrestricted ones.

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
| `sub` | Yes | Identifies the user; matched against `Staff.KeycloakSubject` |
| `tenant_id` | Yes | Resolves the tenant; missing → 401 |
| `realm_access.roles` | No | Contains `["admin"]` for admin users; transformed to `ClaimTypes.Role` by `KeycloakRolesClaimsTransformer` |
| `email` | No | Display only |

---

## Endpoint authorization summary

| Endpoint group | Required | Note |
|---|---|---|
| `GET /api/v1/classes` | `[Authorize]` | Results filtered by ClassPermission when rows exist |
| `POST/PUT/DELETE /api/v1/classes` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/staff/me` | `[Authorize]` | Returns own staff record |
| `POST/PUT/DELETE /api/v1/staff` | `[Authorize(Roles = "admin")]` | |
| `PATCH /api/v1/staff/{id}/admin` | `[Authorize(Roles = "admin")]` | Cannot remove self or last admin |
| `GET/POST/DELETE /api/v1/classes/{id}/permissions` | `[Authorize(Roles = "admin")]` | |
| Schema mutations (`SchemasController` writes) | `EditClass` policy | Enforces ClassPermission logic |
| `GET /api/v1/classes/{id}/ugeplan` | `[Authorize]` | Any authenticated user |
| `PUT /api/v1/classes/{id}/ugeplan/slots` | `[Authorize]` | Any authenticated user — no EditClass check |
| `POST/DELETE /api/v1/classes/{id}/ugeplan/slots/{slotId}/files` | `[Authorize]` | Any authenticated user |
| `POST /api/v1/absence`, `GET mine`, `DELETE {id}` | `[Authorize(Roles = "parent")]` | Own children only, checked in `AbsenceService` |
| `GET /api/v1/absence`, `GET stats` | `[Authorize]` | Filtered to classes the caller passes `EditClass` for (admins: all) |
| `PUT /api/v1/absence/{id}/category`, `POST follow-ups` | `EditClass` on the student's class | |
| `GET leave-requests`, `POST {id}/approve`, `POST {id}/reject`, `GET export`, `GET flagged-export` | `[Authorize(Roles = "admin")]` | The principal grants ekstraordinær frihed |
| `GET/PUT /api/v1/attendance/classes/{classId}` | `EditClass` on the class | Requires a staff row, so parents/board are rejected |
| `GET /api/v1/attendance/overview` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/attendance/mine/pending` | `[Authorize]`, not parent/board | |
| `POST /api/v1/staff-absences`, `GET mine`, `DELETE {id}` | `[Authorize]`, not parent/board | Staff report themselves; admins may report for anyone and delete any |
| `GET /api/v1/staff-absences`, `GET {id}`, `PUT {id}/substitute` | `[Authorize(Roles = "admin")]` | Whole-school action, no ClassPermission check |
| `GET /api/v1/staff/available`, `GET /api/v1/substitutions/mine` | `[Authorize]`, not parent/board | |
| `PUT /api/v1/week-plans/{id}/slots/{slotId}/substitute` | `EditClass` on the week plan's class | |
| `GET /api/v1/exports/school.zip`, `POST school.zip/link` | `[Authorize(Roles = "admin")]` | |
| `GET /api/v1/exports/school.zip/download?token=…` | `ExportLink` scheme, admin | The only endpoint not authenticated by a Keycloak JWT. The token comes from `POST school.zip/link`, carries the admin's tenant and subject (data protection, signed and encrypted), lasts one minute and works once (`ExportLinkAuthHandler`) |

---

## Known gaps

`WeekPlanController` write endpoints (`PUT slots`, `POST/DELETE files`) enforce `[Authorize]` only — any authenticated tenant user can annotate any class's week plan, regardless of ClassPermissions. This is intentional for the teacher annotation flow but means ClassPermissions do not fully restrict week-plan writes.

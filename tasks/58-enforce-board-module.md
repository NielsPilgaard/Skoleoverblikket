---
title: 'Enforce paid modules in the API'
purpose: 'Make BoardModule and ParentModule actually gate their features server side, instead of only hiding sidebar items.'
description: >-
  Both add-ons are sold at 300 kr/md, but no endpoint checks them. Rule: board
  and parent users only exist while their module is active, so every request
  from those roles is refused without it. Admin and staff features of a module
  block writes without it but still allow reads and deletes, as long as the
  subscription itself is active.
  One global filter plus one attribute, no per-action checks.
status: 'Ready'
---

# Enforce paid modules in the API

## TL;DR

A global `ModuleAccessFilter`, next to `SubscriptionAccessFilter`, enforces three rules. A trial counts as having every module, as `SubscriptionService.GetActiveModulesAsync` already does.

- **R1 (users)**: a request made as a `Board` user without `BoardModule`, or as a `Parent` user without `ParentModule`, gets 403 (D1 covers principals with several roles). Board and parent users can't exist without their module.
- **R2 (features)**: actions marked `[RequiresModule(X)]` reject `POST`/`PUT`/`PATCH` with 403 without module X. `GET` and `DELETE` still work, so a school that drops a module can see its data and clean up. An expired subscription still blocks `DELETE` first (D2).
- **R3 (invitations)**: accepting a board or parent invitation gets 403 when the inviting school lacks the module.

No new frontend. The UI already hides the sidebar items and disables the invite buttons.

## Context

- `SubscriptionAccessFilter` ([Tenancy/SubscriptionAccessFilter.cs](../api/Skoleoverblikket.Api/Tenancy/SubscriptionAccessFilter.cs)) is the model: a global action filter that blocks writes when the subscription has expired and returns a Danish `ProblemDetails` 403.
- Today only `BillingController`, `StatsController` (a parent-related count) and `SubscriptionModulesController` read modules.
- Board and Parent role access is spread across `ClassesController`, `SchemasController`, `StaffController`, `StatsController`, `ComplianceCoverageController`, `AttendanceController`, `SubstituteController`, `StaffAbsenceController`, `AbsenceController`, `VacationRegistrationController`, `ContactThreadsController`, `MessagesController`, `ClassChatController`, `ContactDirectoryController` and `NotificationsController`. That is why R1 checks the role in one filter instead of adding attributes to each action.
- Since task 55 the sidebar uses `module: 'parent' | 'board'`. R2 below follows what that hides today.

## Decisions

- **D1 — Gate on the role, not the endpoint (R1).** If the principal is in `Roles.Board` and `BoardModule` isn't active, return 403. The same goes for `Roles.Parent` and `ParentModule`. The only exemption is `GET /api/v1/modules`, so the frontend can still read the module state. A principal can hold several realm roles (a staff member who accepts a board invitation keeps `Staff` and gains `Board`), so R1 decides per request which role the principal acts as. It reads the roles allowed by the action's `[Authorize(Roles = …)]` attributes and drops the gated roles whose module is off. If the principal still holds one of the remaining roles, R1 lets the request through. Otherwise it returns 403. An action with no role list counts every authenticated role, so it is only blocked when all of the principal's roles are gated and off. Example without `BoardModule`: an Admin-and-Board principal gets through on an Admin-only action and on an action open to Admin and Board. The same principal gets 403 on a Board-only action such as `GET board-members/me`.
- **D2 — Without a module its features become read-only, except deletes (R2).** R2 blocks `POST`/`PUT`/`PATCH` and allows `GET` and `DELETE`, so an admin can remove leftover board members and parents. This is not the expired-subscription rule. `SubscriptionAccessFilter` runs first and blocks `DELETE` too when the subscription has expired. So a `DELETE` on a gated feature only works while the subscription itself passes that filter (active or trialing). With an expired subscription everything except reads is blocked, whatever the modules.
- **D3 — Turning a module off blocks, it doesn't delete.** Board members, parents, files and messages stay. Turning the module on again restores access. Deleting data after cancellation stays with `SchoolRetentionJob`.
- **D4 — Invitation accept checks the module in the service (R3).** On accept, the user has no tenant claim yet, so the filter can't see the school. `BoardMemberInvitationService` and `ParentInvitationService` look up the invitation's school, check the module, and return a failure that the controller maps to 403. `preview` keeps working.
- **D5 — One query per gated request.** Call `GetActiveModulesAsync` once per request, and only when R1 or R2 applies. No cache until it shows up in a profile.

## R2 endpoint list

Writes on these actions need the module. Any action not listed here isn't gated by R2. Parent and board users are still covered by R1.

| Controller | Gated writes | Still works without the module |
|---|---|---|
| **BoardModule** | | |
| `BoardMembersController` | `POST invite`, `PATCH {id}/teacher-data-access` | `GET`, `GET {id}`, `DELETE {id}` |
| `BoardFilesController` | `POST presign`, `POST confirm`, `POST folders`, `PATCH folders/{id}` | `GET`, `DELETE {id}`, `DELETE folders/{id}` |
| `ImportsController` | `POST board-members` | — |
| **ParentModule** | | |
| `ParentsController` | `POST invite`, `POST {id}/students/{studentId}`, `PATCH {id}/adresse-beskyttelse`, `PATCH {id}/contact` | `GET`, `GET {id}`, `DELETE {id}`, `DELETE {id}/students/{studentId}` |
| `ParentInvitationsController` | `POST {parentId}/resend` | `GET preview` (`POST accept` falls under R3) |
| `StudentsController` | `POST`, `PUT {id}`, `POST {id}/avatar/presign`, `POST {id}/avatar/confirm` | `GET`, `DELETE {id}` |
| `ImportsController` | `POST students-and-parents` | — |
| `AttendanceController` | `PUT classes/{classId}` | all `GET` |
| `AbsenceController` (staff/admin actions) | `PUT {id}/category`, `POST {id}/approve`, `POST {id}/reject`, `POST follow-ups` | all `GET`, including the exports |
| `ContactThreadsController` | `POST`, `POST {threadId}/messages`, `POST {threadId}/read` | all `GET` |
| `MessagesController` | `POST`, `POST {id}/read`, `POST group/preview`, `POST group` | all `GET` |
| `ClassChatController` | `POST {classId}/messages`, `POST {classId}/attachments/presign`, `POST {classId}/attachments/confirm` | all `GET`, `DELETE {classId}/messages/{messageId}` |

Not gated: `VacationRegistrationController` admin windows, `ContactDirectoryController` (read only), `ComplianceCoverageController` admin side (Stå mål med is Basis). The sidebar doesn't gate these either.

The `StudentsController` row follows today's sidebar, which hides Elever without the parent module. If students should be part of Basis, drop that row and remove `module: 'parent'` from `/elever` in the same PR.

## Scope

- [ ] `RequiresModuleAttribute(SubscriptionModule module)` (class or method level) in `Tenancy/`.
- [ ] `ModuleAccessFilter` in `Tenancy/`, registered globally after `SubscriptionAccessFilter` in `ServicesExtensions.cs`. It implements R1, then R2. 403 `ProblemDetails`:
  - R1: title `Modulet er ikke aktivt`, detail `Skolen har ikke længere {Bestyrelsesmodulet|Forældremodulet}. Kontakt skolens kontor.`
  - R2: detail `Kræver {bestyrelsesmodulet|forældremodulet}. Aktivér det under Abonnement.`
- [ ] Put the attributes on the actions in the table.
- [ ] R3 in both invitation services, mapped to 403 in their controllers.
- [ ] [docs/AUTHORIZATION.md](../docs/AUTHORIZATION.md): new "Paid modules" section with R1–R3 and a link to this table (or move the table there).
- [ ] [docs/PRICING.md](../docs/PRICING.md): one line saying modules are enforced server side and what happens when one is turned off (D3).

## Testing

Integration tests only, through HTTP, in one new `ModuleAccessTests.cs`. Each test needs a school with an active, non-trial subscription and a chosen set of modules. Set that up through `SubscriptionService` from `TestDataBuilder`, not with `db.Add`.

- R1: a board user without `BoardModule` gets 403 on `GET board-members/me`, `GET board-files` and a `SchemasController` read. With the module, 200. Same for a parent user on `GET parents/me` and `GET absence/mine`.
- R1 exemption: `GET /modules` returns 200 for a blocked board user.
- R1 multiple roles (D1): an Admin-and-Board principal without `BoardModule` gets 200 on an Admin-only `GET` (e.g. `GET board-members`) and 403 on the Board-only `GET board-members/me`.
- R2: an admin without `BoardModule` gets 403 on `POST board-members/invite` and 200 on `GET board-members` and `DELETE board-members/{id}`. An admin without `ParentModule` gets 403 on `POST parents/invite` and `POST students`.
- R3: accepting a board invitation after the module was removed gets 403.
- Trial: a trialing school with no bought modules can invite board members and parents.
- Existing tests keep passing. They run on a trialing subscription, which has every module.

## Non-goals

- New frontend screens. Existing UI hides and disables gated items.
- Deleting data when a module is turned off (D3).
- Changing prices, the billing page, or what is in Basis.

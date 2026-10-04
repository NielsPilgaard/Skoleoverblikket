---
title: 'Migration CLI og agent-drevet flytning'
purpose: 'Make free migration help (task 56) cheap to repeat: a typed CLI that applies a reviewed migration file to a school through the public API, and later an EU-LLM agent that drafts that file from the school''s exports.'
description: >-
  A `migrate` CLI in web/scripts/migrate, built on the generated API client.
  Input is one migration.json per school (JSON Schema in the repo, filled from
  an Excel template or drafted by an agent). Commands: login (Keycloak device
  grant), validate, plan, apply. Idempotent, never deletes, never touches
  students or parents. Phase 2 adds `migrate draft` using the EU LLM, run
  locally, with a human approving the plan before apply.
status: 'Proposed'
---

# Migration CLI og agent-drevet flytning

## TL;DR

[Task 56](56-free-migration-help.md) promises free migration help. Done by clicking through the UI, a school costs hours. This task turns the work into **draft → plan → apply**. Someone fills in a `migration.json` (by hand via an Excel template now, by an agent later). `migrate plan` shows what will be created. `migrate apply` creates it through the normal admin API, logged in as the school's `support+<slug>` user. The file is the contract: the agent phase only adds a new way to draft it. Apply, review and the safety rules stay the same.

## Context

- [scripts/seed-data.ps1](../scripts/seed-data.ps1) already drives the live API with a password grant and hand-written calls. It's not idempotent, and it breaks silently when the API changes.
- `web/` already has the generated hey-api client (`src/api/generated`) and the `xlsx` package.
- Endpoints a migration needs, all admin-only and tenant-scoped: `PUT /api/v1/time-slot-template`, rooms, courses (`UpsertCourseRequest`), staff, classes, `POST /api/v1/classes/{id}/schemas`, `PUT .../schemas/{id}/slots` (`UpsertSlotRequest`: time slot, weekday, course, **one** teacher, optional room, optional aide), `GET .../conflicts`. Bulk imports exist for staff, rooms and classes (`/api/v1/imports/...`).
- Keycloak clients: `skoleoverblikket-web` (public, direct access grants on), `-api`, `-admin`. None has device grant enabled.
- [ai-data-boundary](../docs/adr/ai-data-boundary.md): personal data may only go to the EU zone (prod, OVH, Alexandra Instituttet LLM). Never to GitHub or Claude. Agents with code access run only in GitHub Actions, which is the public zone, so **an agent that sees school data can't run there**.
- Staff names and e-mails in a schema export are personal data. Low sensitivity, but in scope.

## Decisions

- **D1 — TypeScript in `web/scripts/migrate/`, on the generated client.** Not PowerShell. An API change then becomes a `tsc` error in CI instead of a failed migration at a customer. New `tsconfig.scripts.json`, included in `npm run build`. Run with `npm run migrate -- <command>` (via `tsx`).
- **D2 — `migration.json` is the contract.** A JSON Schema (`manifest.schema.json`) in the repo, with TS types generated from it. Everything that drafts (human, converter, agent) writes this file. Everything that applies reads it. Records are matched by **natural keys**, not IDs: room/course/class by name, staff by e-mail (else name), time slots by start time, schema by class + name, slot by class + schema + weekday + start time.
- **D3 — Contents follow task 56 D2, enforced by the schema.** `timeSlotTemplate`, `rooms`, `courses`, `staff`, `classes`, `schemas[] { class, name, startDate?, endDate?, slots[] { weekday, start, course, teacher, room?, aide? } }`. No fields for students, parents, CPR or addresses, and `additionalProperties: false` everywhere. The tool can't carry child data even by mistake.
- **D4 — Login with Keycloak device grant.** New public client `skoleoverblikket-cli` with only *OAuth 2.0 Device Authorization Grant* enabled. `migrate login` prints a code, the operator logs in as `support+<slug>@skoleoverblikket.dk` in the browser, and the token is kept in memory for the session. No passwords on disk, no password grant, and it still works if Keycloak MFA comes later.
- **D5 — Wrong-school guard.** `plan` and `apply` print the school's name and slug from the API. `apply` requires `--school <slug>`, which must match the logged-in tenant and the manifest's `school` field. If any of the three differ, it stops.
- **D6 — Plan/apply, idempotent, never deletes.** `plan` reads current state and prints `+ opret`, `~ ret`, `= uændret` per record. `apply` runs the plan in dependency order (template → rooms → courses → staff → classes → schemas → slots) and stops at the first error. Running it again after a fix continues safely. Done means `plan` shows 0 changes. Deleting is done in the UI by a human, never by the tool.
- **D7 — No invitations.** Staff are created without invitations. The school decides when its staff get mail. The hand-over email (task 56 D6) tells them how.
- **D8 — Data stays off the repo.** Manifests and source files live in `~/skoleoverblikket-migrations/<slug>/`. The CLI refuses to read a manifest inside the git working tree, and `*.migration.json` is git-ignored as a backstop. The only manifest in the repo is `fixtures/eksempelskolen.migration.json` with made-up data.
- **D9 — Only the normal admin API.** No superadmin bypass, no migration-only endpoints. If a step lacks an endpoint, add a normal admin endpoint through the feature's service ([AGENTS.md](../AGENTS.md#encapsulation-thin-controllers-feature-services)), so the UI benefits too.
- **D10 — Converters on the second occurrence.** A deterministic converter (`migrate from-skoleplan <csv>`, etc.) is written only once two schools have sent the same format. The first one is filled in by hand via the template.

## Scope — phase 1 (now)

### 1. Keycloak

- [ ] Realm JSON ([Skoleoverblikket-realm.json](../infrastructure/keycloak/realms/Skoleoverblikket-realm.json)): client `skoleoverblikket-cli`, public, `oauth2.device.authorization.grant.enabled: true`, standard flow / direct access grants off, same `tenant`/`roles` scopes as `-web`.
- [ ] Prod: same client created by hand in the admin console. Steps go in [DEPLOYMENT.md](../docs/DEPLOYMENT.md).

### 2. CLI — `web/scripts/migrate/`

- [ ] `manifest.schema.json` + generated types. `school`, `sourceSystem` and `notes` at the top level.
- [ ] Commands:
  - `login`: device grant (D4).
  - `template <out.xlsx>`: writes the Excel template, one sheet per section, Danish column names, an example row per sheet.
  - `from-xlsx <in.xlsx> <out.migration.json>`: template → manifest.
  - `validate <file>`: schema + references (every slot's course/teacher/room/class exists in the file or in the school) + weekday/time sanity + **conflicts inside the file** (same teacher/room/aide at the same time), all reported before anything is sent.
  - `plan <file>`: D6 diff, plus a summary line ("12 klasser, 31 medarbejdere, 412 lektioner").
  - `apply <file> --school <slug>`: D5, D6. Prints progress and finishes by running `plan` (expect 0) and fetching `GET .../conflicts` per schema.
- [ ] Errors from the API (ProblemDetails) are printed with the manifest path that caused them (`schemas[3].slots[17]`).
- [ ] `fixtures/eksempelskolen.migration.json`: made up, ~3 classes, used by the smoke test.

### 3. Runbook

- [ ] `docs/MIGRATION_HELP.md` (from task 56): steps 4–5 become `template` → fill → `validate` → `plan` → `apply`. Add a "first time on a new machine" box (`npm install`, `npm run migrate -- login`) and a **practice run** against local Aspire: apply the fixture to Debugskolen, then `plan` shows 0 changes.

### Testing

- No new test layer ([TESTING.md](../docs/TESTING.md)). API drift is a `tsc` error (D1). New or changed endpoints (D9) get integration tests the normal way.
- The practice run in the runbook is the CLI's smoke test. Run it before each real migration, and in the PR for this task.

## Phase 2 — agent-drafted migrations (later)

**Trigger**: the hours log from task 56 shows migrations taking more than about one day a week, or the same format arrives too varied for converters (D10).

- **`migrate draft <files...> -o <out.migration.json>`**: extracts text locally (xlsx/csv as tables, PDF via text extraction), sends it with `manifest.schema.json` as structured output to the **EU LLM** (Alexandra Instituttet, already approved in the ADR), and writes the manifest plus a `draft-notes.md` listing everything the model was unsure of (unknown abbreviations, double-teacher lessons, missing rooms).
- **Human gate unchanged**: `validate` → `plan` → read the notes → `apply`. The agent never runs `apply`.
- **Where it runs**: on the operator's machine, never in GitHub Actions (public zone), never via Claude. If we later want Claude for drafting, local pseudonymization (teacher names → `L01`… with the mapping kept local) needs an amendment to [ai-data-boundary](../docs/adr/ai-data-boundary.md) first. Pseudonymized data is still personal data under GDPR.
- **Agent loop, not just one call**: the agent may run `validate` and `plan` itself and fix its own draft until both are clean. It may not run `apply`. That's the "CLI access + agent" setup: the agent gets the read-only commands, the human keeps the write command.
- **Converge with [task 25](25-schema-import.md)**: if the drafting prompt and schema live server-side as an admin endpoint (`POST /api/v1/imports/schema-draft`, EU LLM, returns a manifest), the same code serves the CLI now and the school's own self-service import later. At that point "free help" costs us almost nothing. Decide when phase 2 starts.

## Open questions

1. Is `tsx` acceptable as a devDependency, or do we rely on Node's built-in type stripping (Node ≥ 23.6)? Check the Node version in CI.
2. Lessons with two teachers: `UpsertSlotRequest` has one teacher + one aide. Does a second teacher map to `aide`, or do we report it as "manuel" in `validate`? Look at how real exports do it before deciding.

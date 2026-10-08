---
title: 'Testing Strategy'
description: >-
  Two-layer test strategy — tUnit/Testcontainers API integration tests and
  Playwright e2e — what belongs in each layer, non-brittle rules, and what's
  explicitly skipped and why.
status: 'Living'
purpose: Reference before writing any test — determines which layer a test belongs in and what patterns keep it from becoming a maintenance burden.
---

# TESTING.md — Testing strategy

## Philosophy

Test behavior, not implementation. A test should break when product behavior breaks — not when a method is renamed or a service is extracted. This keeps tests maintainable and makes the 30-minutes-fixing-tests-per-PR problem go away.

- **Don't test**: "does `SchemaService.AssignLesson()` call `_repo.Save()`"
- **Do test**: "when an admin assigns a teacher to a time slot that conflicts, the API returns 409 with conflict details"

---

## Two layers

### 1. API integration tests (tUnit + WebApplicationFactory + Testcontainers)

The primary and highest-value test layer. Spin up the full ASP.NET Core pipeline against a real PostgreSQL instance via [Testcontainers for .NET](https://dotnet.testcontainers.org/). Test HTTP in → HTTP out.

**Framework**: [tUnit](https://tunit.dev/) (not xUnit). tUnit is built on `Microsoft.Testing.Platform` with async-first design and source-generated test discovery. It is greenfield-friendly and .NET 8+ only — both apply here. It is still pre-v1.0 (currently ~0.25.x); APIs are mostly stable but breaking changes before v1.0 are possible.

**Why tUnit over xUnit here:**

- `tUnit.AspNetCore` provides per-test isolation via `WebApplicationTest<TFactory, TProgram>` — each test gets its own isolated server instance, eliminating the shared-state flakiness that plagues xUnit `IClassFixture` setups
- Tests run in parallel by default with `[DependsOn]` and `[ParallelLimiter]` for fine-grained control
- Async-first: setup/teardown hooks are natively async, assertions are awaitable

**Critical footgun**: tUnit assertions do not execute until awaited. `Assert.That(x).IsEqualTo(1)` without `await` silently passes. Always `await Assert.That(...)`.

**Snapshot testing**: use [Verify.TUnit](https://github.com/VerifyTests/Verify) for stable, happy-path API responses where catching unintended contract drift matters (e.g. the schema assignment response shape, the staff list payload). Verify auto-sanitizes GUIDs and timestamps to deterministic placeholders. `.verified.txt` files are committed and reviewed in PRs like code — a snapshot diff is a contract change.

Do **not** use Verify for:

- Error/ProblemDetails responses (dynamic traceIds, correlationIds make sanitization noisy — assert manually)
- Any response that changes legitimately on every feature iteration
- Snapshots over ~50 lines (nobody reviews them carefully enough to be useful)

**Why this layer catches what matters most:**

- Tenant scoping correctness — the single most critical invariant in the system
- Auth/JWT enforcement
- Schema conflict detection logic
- EF Core query behavior (global query filters, includes, projections)
- Serialization and ProblemDetails error shape

**Location**: `api/tests/Skoleoverblikket.Api.IntegrationTests/`

**Example scope**: one test class per feature area (`SchemaBuilderTests`, `ConflictDetectionTests`, `StaffInvitationTests`), not one per controller or service.

### 2. Playwright e2e tests (critical flows only)

For user flows that span frontend + backend and where a broken UI is a broken product. Target the flows a non-technical user would hit and not know how to recover from.

**Covered flows:**

- Schema creation and lesson assignment
- Staff login and schedule view
- Printable schema generation
- School setup wizard

**Location**: `web/tests/e2e/`

**Run against**: real frontend + real API, using the Aspire dev stack with a seeded test database. No mocking.

**How to run:**

```bash
# Stack already running (e.g. you started Aspire manually):
cd web && npm run test:e2e:skip-aspire

# Let Playwright start the Aspire stack for you:
cd web && npm run test:e2e
```

---

## What is explicitly skipped

| Layer                                                | Reason                                                                                                        |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Unit tests on services/repos with mocked `DbContext` | Mocks bypass the EF Core global query filter — the exact invariant that matters most. Tests pass, prod leaks. |
| React component tests (RTL, Vitest)                  | High maintenance for low signal at this app's complexity. Covered by Playwright flows.                        |
| Contract/consumer-driven tests                       | Overkill for a monorepo where API and client are co-deployed.                                                 |

---

## Non-brittle rules

These rules are what keep tests from becoming a maintenance burden:

1. **No mocking `DbContext` or `ITenantContext`** — always use a real test DB via Testcontainers. Spin-up takes ~5s and is worth it.
2. **No testing private or internal methods** — only test via public HTTP endpoints (API) or rendered UI (Playwright).
3. **Playwright selectors must use `data-testid` attributes** — never CSS classes or DOM structure. Tailwind class names change; `data-testid` values are stable contracts.
4. **One test file per feature flow**, not per class — `schema-builder.test.ts`, not `SchemaController.Tests.cs`.
5. **Arrange test data via the API**, not by inserting directly into the DB — this keeps tests resilient to schema changes.

---

## What warrants a test

Write a test when:

- The feature involves a flow a user would be blocked on if it broke silently (schema creation, login, billing)
- The feature has a correctness invariant that is non-obvious (tenant isolation, conflict detection, billing state)
- The feature has failed before or is known to be fragile

Do not write a test for:

- CRUD endpoints with no business logic
- UI layout or styling
- Internal helper methods

---

## CI

Every PR runs:

1. API integration tests (Testcontainers spins PostgreSQL ephemerally — no shared state)
2. The full Playwright suite against the Docker Compose stack (`infrastructure/docker/docker-compose.staging.yml`), with images built from the PR's code (`.github/workflows/e2e.yml`)

After merge, the Staging workflow runs the same suite against the images CI published for that commit, and only deploys if it passes.

PRs do not merge if tests fail.

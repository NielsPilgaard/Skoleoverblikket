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

**Framework**: [tUnit](https://tunit.dev/) (not xUnit), with `TUnit.AspNetCore`. tUnit is built on `Microsoft.Testing.Platform` with async-first design and source-generated test discovery. The version is pinned in the test project's `.csproj`.

**How isolation works:**

- One `ApiFactory` (a `TestWebApplicationFactory<Program>`) per test session, shared via `[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]`. It starts the PostgreSQL, Silo (S3) and stripe-mock containers once.
- Each test isolates itself by creating its own school with a fresh `Guid` tenant id (`TestDataBuilder.CreateSchoolAsync`) and sending it in the `X-Test-TenantId` header. `TestAuthHandler` reads `X-Test-TenantId`, `X-Test-Roles` and `X-Test-Subject`, so each `HttpClient` carries its own identity.
- Tests run in parallel by default. Because the database is shared, never assert on data outside your own tenant (e.g. global row counts). A test that must touch every school (retention jobs, notices to all schools) gets `[NotInParallel(nameof(MyTests))]`.
- Emails sent during the session land in `factory.Emails`; filter by an address unique to your test.

**Critical footgun**: tUnit assertions do not execute until awaited. `Assert.That(x).IsEqualTo(1)` without `await` silently passes. Always `await Assert.That(...)`.

**No snapshot testing.** Assert on the fields that matter explicitly; there is no Verify package or `.verified.txt` files.

**Why this layer catches what matters most:**

- Tenant scoping correctness — the single most critical invariant in the system
- Auth/JWT enforcement
- Schema conflict detection logic
- EF Core query behavior (global query filters, includes, projections)
- Serialization and ProblemDetails error shape

**Location**: `api/tests/Skoleoverblikket.Api.IntegrationTests/`

**Example scope**: one test class per feature area (`ConflictDetectionTests`, `StaffInvitationTests`, `VacationRegistrationTests`), not one per controller or service.

### 2. Playwright e2e tests (critical flows only)

For user flows that span frontend + backend and where a broken UI is a broken product. Target the flows a non-technical user would hit and not know how to recover from.

**Covered flows** (one spec per flow in `web/tests/e2e/`), among others:

- Signup, login, password reset and the school setup wizard
- Staff invitation, board member invitation and "Vis som" role switching
- Schedule views (each lesson shows once, under the right weekday)
- Fravær and vikardækning
- Files, calendar, links, Klassechat, CSV import and the admin dashboard

**Location**: `web/tests/e2e/`

**Run against**: real frontend + real API, using the Aspire dev stack with a seeded test database. No mocking, with one exception: `deploy.spec.ts` stubs the maintenance container's 503 + `X-Maintenance` answer with `page.route`, because that container runs only in production. It tests how the web app waits out a deploy, not the API.

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
5. **Arrange test data via the API** where an endpoint exists — this keeps tests resilient to schema changes. For data the API can't create, `TestDataBuilder` resolves the owning feature service from `factory.Services` and calls its create method, never `db.Add(new Entity { ... })` (see [AGENTS.md](../AGENTS.md#encapsulation-thin-controllers-feature-services)).

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

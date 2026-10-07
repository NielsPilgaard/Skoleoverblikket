# AGENTS.md

This file defines how AI agents should work in this codebase.

## Project context

See [docs/VISION.md](docs/VISION.md) for the mission, why the scope is what it is, and why simplicity is non-negotiable. See [docs/PRD.md](docs/PRD.md) for the full product requirements — target segments, feature scope, competitive positioning. Product and architecture decisions are recorded as ADRs in [docs/adr/](docs/adr/) (index: [docs/adr/INDEX.md](docs/adr/INDEX.md)) — check there before assuming a design choice is arbitrary.

Skoleoverblikket is affordable, dead-simple admin software for Danish schools, multi-tenant SaaS, one tenant per school. The schema planner (weekly class schedules with real-time conflict detection) is the core, but the product has grown into the school's full admin backbone: SFO, ugeplan, vikardækning, parent communication (kontaktbog, beskeder, kontakt directory), fraværsregistrering, ferieindmelding, filarkiv, bestyrelse, and stå-mål-med compliance publishing. The point is to let schools spend less money and less time on admin and paperwork, so staff can focus on teaching instead of syncing data between disconnected tools. The platform is Danish-language only, targeting the Danish market.

**Primary market**: friskoler and private/independent schools. **Secondary market**: folkeskoler — folkeskoler are NOT vendor-locked to any timetable tool; Aula (the national school-home communication platform) is a separate communication product and folkeskoler choose their timetable tool independently.

Reference school profile: ~300 students, 25 staff, friskole in a small Danish town — use this as the mental model for a typical customer.

**Simplicity is a core product value.** The users are a 58-year-old school secretary, a teacher checking his schedule between classes, a principal who needs overview, and a part-time substitute who just needs to know where to be. See [docs/PERSONAS.md](docs/PERSONAS.md). Every UI decision must pass: can Hanne (the school secretary) complete this without asking for help?

## Architecture principles

**Multi-tenancy**: every database query must be scoped to a tenant. Never leak data across tenants. Tenant ID must be present on every query that touches school, staff, class, course, schema, room, or file data. Enforced via EF Core global query filter:

```csharp
HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)
```

Never bypass this filter. Never trust a slug string as an authorization signal — always resolve to a TenantId at the middleware boundary.

**Responsive UI**: the schema builder (admin) is laptop-first — it needs screen space. Staff schedule views must work fully on a phone. No feature may be unusable at any screen size.

**Simplicity first**: the primary user is a school secretary with limited time and low technical sophistication. Every feature must be operable without training. Prefer fewer options over more. Prefer obvious over clever.

**Billing via Stripe Checkout**: all billing is self-serve. 30-day free trial, then monthly Stripe Checkout. No manual invoicing. No MobilePay.

**Built features (beyond core schema planner)**. This product is far broader than "a timetable app" — it is becoming the full admin backbone for a small school, which is the point: less paperwork and fewer disconnected tools, not just a schema grid.

- SFO week plan (`SfoWeekPlanController`, `SfoController`) — weekly SFO schedule with print view
- Ugeplan / weekplan (`WeekPlanController`) — per-class weekly plan with file attachments per slot, shown to parents
- Vikar overview (`SubstituteController`) — free/busy staff lookup per time slot and one-click substitute assignment when a teacher or aide is out
- Staff absence + vikardækning (`StaffAbsenceController`, `StaffAbsenceService`, `SubstituteService`) — staff report themselves absent (or the office does it for them); admin sees each affected lektion with ranked free candidates and assigns a vikar per lektion (stored on `WeekPlanSlot`, shown in ugeplan and on the staff dashboard)
- Parent module (`ParentsController`, `ParentMeController`, `ParentInvitationsController`) — parent portal with schema/calendar/ugeplan views
- Fravær / student absence register (`AbsenceController`, `AttendanceController`, `AbsenceService`, `AbsenceStatsService`) — daily fremmøde per class, three legal categories, parent sick reports and leave requests, quarterly stats with 10%/15% ulovligt flags, retention of current + previous school year (`AbsenceRetentionJob`)
- Kontakt directory (`ContactDirectoryController`) — role-filtered parent directory with `ShareContactInfo` consent
- Kontaktbog (`ContactThreadsController`) — per-child parent↔teacher message threads
- Klassechat (`ClassChatController`, `ClassChatAttachmentSweeper`) — one group thread per klasse for its parents, schema staff and admins, with file attachments; membership is derived via `ClassMembershipService`, never stored
- Beskeder (`MessagesController`) — flat inbox for all tenant users with consent rules
- Notifications (`NotificationsController`) — in-app + email, per-type opt-out via `NotificationPreference`
- Calendar with recurrence (`CalendarController`) — school calendar events with recurrence and excluded dates
- Class permissions (`ClassPermissionsController`) — per-class edit grants (superadmin vs. restricted mode)
- File explorer (`FilesController`) — upload files, link to courses, browse by course, OVHCloud object storage
- Bestyrelse / board module (`BoardMembersController`, `BoardInvitationsController`, `BoardFilesController`) — board member invitations and a board-only file space, separate from staff/parent files
- Stå mål med / compliance publishing (`ComplianceCoverageController`) — lets friskoler publish teaching goals and plans per course/grade to satisfy Friskoleloven §1a public-disclosure requirements
- Stats dashboard (`StatsController`) — school-wide overview numbers (classes, staff, schema completeness) for the admin dashboard
- Reports (`ReportsController`) — Excel export of teacher/staff hours and UVM timetal comparisons
- CSV import (`ImportsController`) — bulk import of parents/students onto existing classes, admin-only, with per-row warnings
- Demo requests (`DemoRequestController`) — public "book a demo" form on the marketing site, emailed to sales
- Module billing (`SubscriptionModulesController`) — parent module gated behind Stripe subscription
- Backoffice (`SuperAdminTenantsController`, `SuperAdminEmailPreviewController`) — isSuperAdmin role, view-as mode
- Avatar uploads — presign+confirm pattern for Parent, Staff, Student avatars stored in OVHCloud
- Data retention (`SchoolDeletionService`, `SchoolRetentionJob`) — 90 days after Stripe cancellation, admins are warned 7 days ahead and then all school data (rows, files, Keycloak logins) is permanently deleted
- Databehandleraftale (`DataProcessingAgreementController`, `SuperAdminSubProcessorNoticeController`) — GDPR art. 28 agreement accepted at signup or via an admin banner, public `/databehandleraftale` and `/underdatabehandlere` pages, 30-day sub-processor change notice from the backoffice
- Vacation registration / ferieindmelding (`VacationRegistrationController`) — admin creates registration windows with granularity (weeks/days) and deadlines; parents submit vacation requests via `ParentVacationRegistrationPage`; admin reviews all entries and manages windows via `VacationRegistrationPage` / `VacationRegistrationDetailPage`; full CRUD on windows with open/closed toggle and CSV export of responses

## Coding conventions

### API (ASP.NET Core / C#)

- **API style**: RESTful, versioned at `/api/v1/`. OpenAPI spec is generated from code (Swashbuckle). Do not hand-write the spec.
- **Error responses**: all API errors use `ProblemDetails` format (RFC 7807). Never return plain strings or custom error shapes.
- **Auth**: all endpoints require a valid Keycloak-issued JWT bearer token unless explicitly decorated to allow anonymous. Never skip auth on endpoints that touch tenant data.
- **EF Core migrations**: never modify an existing migration file. Always generate a new migration for schema changes.
- **Tenant scoping**: the `ITenantContext` service is injected and used in the `DbContext` global query filter. Never pass TenantId as a method parameter through business logic — it must come from the context.
- **Authorization**: role and ClassPermission logic (admin vs. staff, superadmin-mode vs. restricted-mode class editing) is non-obvious — see [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) before touching auth on any endpoint.

### Encapsulation: thin controllers, feature services

The most common source of breakage here is logic in the wrong place: controllers that query `AppDbContext`, load an entity, set its fields and call `SaveChangesAsync`. The same entity then gets changed from several controllers and test helpers, each with its own idea of a valid state. Add a required field or change a rule, and every one of those call sites breaks at once.

The fix is deliberately small: **controllers handle HTTP, one plain service per feature handles data and rules.** This is not DDD. EF Core already is the repository and unit of work. We add one layer, not five.

**Controllers** do four things:

1. Bind the request.
2. Enforce role and policy authorization via attributes (`[Authorize(Roles = ...)]`, `EditClassRequirement`, `ParentClassAccessRequirement`).
3. Call one service method.
4. Map the result to `Ok`, `NoContent`, `CreatedAtAction`, or a `ProblemDetails` error.

A controller does not inject `AppDbContext` and does not call `SaveChangesAsync`. If an action has an `if` about business state (open/closed, expired, already accepted, conflicts), that `if` belongs in the service.

**Services** live in `api/Skoleoverblikket.Api/Services/` and are registered in `AddDomainServices`. `ClassMembershipService` and `StaffInvitationService` are the reference shape.

- **One concrete class per feature area.** `VacationRegistrationService`, `WeekPlanService`. A `sealed` class with a primary constructor taking `AppDbContext`, `ITenantContext` and whatever else it needs. Inject it as the concrete type.
- **The owning service is the only writer.** Only the vacation registration service creates, changes or deletes vacation registration entities. Another feature that needs to change them calls a method on that service. Reading another feature's tables with a projection is fine anywhere in a service. Move a read into the owner only when it is duplicated.
- **Entities are created in one method.** `new VacationRegistrationWindow { ... }` appears in the owning service's create method and nowhere else in production code or tests. That method sets `TenantId` and defaults, so a new required field is a one-line change.
- **Name methods after what happens when there is a rule or side effect.** `OpenWindowAsync` sends notifications, so it is its own method. A plain edit form with no rules is fine as `UpdateWindowAsync(id, request)`. Don't split CRUD into ceremony.
- **A method loads, checks, changes, saves once, then triggers side effects** such as notifications and email. Controllers never trigger side effects.
- **No HTTP in services.** No `IActionResult`, `ProblemDetails`, status codes or `HttpContext`. The controller passes in what the service needs, such as `User.GetKeycloakSubject()`.
- **Return DTOs, not tracked entities.** Use `.Select(...)` projections with `AsNoTracking` for reads. Request and response records are defined once, next to the service, and the controller uses them directly as its body and return types. No separate command objects, no mapping layer.
- **Failures: use the simplest return type that works.** Return `null` or `bool` when there is one failure case. Use a small enum only when the controller must tell apart several outcomes, such as not found, not your child and window closed. Throw only for bugs and infrastructure failures.
- **Data-level authorization lives in the service.** "Does this parent own this student?" must hold no matter who calls the method. Role and policy checks stay on the controller.
- **Tenant scoping is unchanged.** Services rely on the global query filter. `IgnoreQueryFilters()` in a service needs a comment explaining why and a tenant-isolation test.

A typical endpoint:

```csharp
[HttpPut("{id:guid}")]
[Authorize(Roles = Roles.Admin)]
public async Task<IActionResult> UpdateWindow(Guid id, UpdateWindowRequest req, CancellationToken ct) =>
	await vacationRegistrations.UpdateWindowAsync(id, req, ct) ? NoContent() : NotFound();
```

**What we deliberately do not do.** Each of these adds files and indirection without fixing the problem above. Don't introduce them, and push back if a task seems to need one:

- Repositories, `IRepository<T>`, or any wrapper around `AppDbContext`.
- An interface per service. Add one only for a real second implementation or an external system tests must replace, like `INotificationService` or `IEmailSender`.
- CQRS, MediatR, one handler class per operation, or pipeline behaviors.
- Aggregates, value objects, domain events, specifications, or behavior methods on entities. Entities stay plain EF classes.
- AutoMapper or other mapping libraries. Write the projection.
- Generic base services, base controllers, or separate Application/Domain/Infrastructure projects.
- Splitting a service before it hurts. One file per feature is fine until it passes roughly 500 lines. Then split by sub-feature, not by technical layer.

The test for any new abstraction: it must remove real duplication or protect a rule that exists today. "We might need it later" does not count.

**Tests.** Integration tests still go through HTTP only (see [docs/TESTING.md](docs/TESTING.md)). When a test needs data it can't create through the API, `TestDataBuilder` gets the owning service from `factory.Services` and calls its create method instead of `db.Add(new Entity { ... })`. Test data then goes through the same code as production, so a new required field breaks one place instead of twenty tests.

**Existing code.** About 29 controllers predate this rule and write to `AppDbContext` directly. Don't big-bang refactor them.

- **New endpoint or feature**: follow this section.
- **Changing an existing endpoint's logic**: move that endpoint's logic into the feature's service in the same PR, creating the service if needed.
- **Unrelated small fix** (typo, status code, attribute): no extraction needed.
- **Moved entity creation into a service**: switch `TestDataBuilder` and tests that build that entity by hand to the service in the same PR.

Before finishing API work, check whether any controller you touched still writes to `AppDbContext` for the entity you changed. If it does, you are not done.

### Frontend (React / TypeScript)

- **API client**: always use the generated typed client (hey-api/openapi-ts). Never hand-write fetch calls to API endpoints that are in the spec.
- **Styling**: Tailwind utility classes only. No CSS-in-JS, no inline `style` props, no separate `.css` files for component styles.
- **Components**: functional components with hooks only. No class components.
- **Landing page features**: the feature cards come from `web/src/content/features.tsx`. Every sidebar route must be mapped there in `sidebarRouteFeatures` (to a feature or `null`), or `tsc` fails. When you add a user-facing feature, add or update its card in the same PR. The `/nyheder` changelog (`web/src/content/changelog.ts`) is drafted weekly by `.github/workflows/changelog.yml` from `feat` commits, so write `feat:` subjects that describe what the user gets.

### General

- Style and naming conventions (indentation, casing, imports) are enforced by `.editorconfig` and the project linting setup. Do not duplicate those rules here.
- Danish domain terms: klasse (class), lokale (room), lektion (time slot), lærer (teacher), pædagog (aide), skema (schema/schedule), SFO (after-school care). These are the only Danish words allowed in code identifiers (class names, file names, routes). Everything else in code must be English. UI text and user-facing web page URLs stay Danish, since the product is Danish-language only; backend API route paths (`/api/v1/...`) are English. See [docs/GLOSSARY.md](docs/GLOSSARY.md) for the full kept-word list and the mapping of incidental Danish names already renamed to English.

## Testing

See [docs/TESTING.md](docs/TESTING.md) for the full strategy. Summary of rules agents must follow:

- **Two layers only**: API integration tests (tUnit + WebApplicationFactory + Testcontainers) and Playwright e2e for critical flows. Nothing else.
- **Never mock `DbContext` or `ITenantContext`** — use a real PostgreSQL test database via Testcontainers. Mocks bypass the global query filter.
- **Never test private methods** — only via public HTTP endpoints or rendered UI.
- **Playwright selectors use `data-testid` only** — never CSS classes or DOM structure.
- **One test file per feature flow**, not per class.
- When in doubt whether something needs a test: does a silent break block Hanne? If yes, test it.

## After completing a feature

After finishing any feature or fix, run **all of the following** before declaring done:

1. **TypeScript build**: `cd web && npm run build` — catches type errors that tsc would reject in CI.
2. **dotnet format**: auto-fixes by default via `verify.ps1`. Use `-NoFix` only to inspect violations without changing files.
3. **API integration tests**: `dotnet test`
4. **ryni**: `ryni check` via `verify.ps1` — broken local Markdown links and invalid `SKILL.md` metadata. Install with `scripts/setup.ps1`.
5. **Playwright e2e**: `cd web && npx playwright test --reporter=line` — starts Aspire stack automatically. Pass `SKIP_ASPIRE=1` if already running.

Do not report a task as complete until all five pass.

Formatting never fails CI: a pre-commit hook (`.githooks/`, enabled by `npm install` in `web/`) formats staged files, and the `autofix` job in `ci.yml` commits any formatting left over. Biome lint rules and TypeScript errors still fail CI; on PRs, `ci-fix.yml` lets Claude try one fix.

**Use the skills instead of running commands manually:**
- `/verify` — runs steps 1–4 (TypeScript build, dotnet format, dotnet build, API integration tests, ryni)
- `/test` — runs step 5 (Playwright e2e)
- `/add-migration` — generates a new EF Core migration after model changes

## Documentation map

| Doc | Read it for |
|---|---|
| [docs/VISION.md](docs/VISION.md) | Mission, why scope is broad-but-narrow, the "does this save Hanne time" filter |
| [docs/PRD.md](docs/PRD.md) | Full product requirements, target segments, competitive positioning, out-of-scope list |
| [docs/PERSONAS.md](docs/PERSONAS.md) | Hanne/Thomas/Birgitte/Mikkel — the four users every screen must work for |
| [docs/adr/INDEX.md](docs/adr/INDEX.md) | Concept → ADR lookup for all product/architecture decisions |
| [tasks/INDEX.md](tasks/INDEX.md) | Prioritized list of open tasks — "implement next task" starts here |
| [docs/SCHEMA_FEATURES.md](docs/SCHEMA_FEATURES.md) | Schema planner detail: time slot inheritance, conflict detection, entities, permissions |
| [docs/GLOSSARY.md](docs/GLOSSARY.md) | Kept Danish domain vocabulary vs. incidental Danish names renamed to English in code |
| [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) | Role model, ClassPermission superadmin/restricted modes, endpoint auth summary |
| [docs/TESTING.md](docs/TESTING.md) | Test strategy — what layer to write a test in and what to skip |
| [docs/PRICING.md](docs/PRICING.md) | Billing model — Basis tier, module add-ons, trial, intervals |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Required environment variables for production |
| [docs/STRIPE_LOCAL.md](docs/STRIPE_LOCAL.md) | Testing Stripe subscription flows locally with the Stripe CLI |

## What agents must never do

- Write database queries without tenant scoping
- Hard-code school names, branding, or any tenant-specific values
- Introduce complexity that a school secretary would not be able to operate
- Modify existing EF Core migration files
- Inject `AppDbContext` into a new controller, or create/mutate an entity outside its owning service
- Trust a URL slug as an authorization token — always resolve to TenantId first
- Bypass Stripe Checkout for billing (no manual invoicing, no MobilePay)

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

**Billing via Stripe Checkout**: all billing is self-serve. 14-day free trial, then monthly Stripe Checkout. No manual invoicing. No MobilePay.

**Built features (beyond core schema planner)**. This product is far broader than "a timetable app" — it is becoming the full admin backbone for a small school, which is the point: less paperwork and fewer disconnected tools, not just a schema grid.

- SFO week plan (`SfoWeekPlanController`, `SfoController`) — weekly SFO schedule with print view
- Ugeplan / weekplan (`WeekPlanController`) — per-class weekly plan with file attachments per slot, shown to parents
- Vikar overview (`VikarController`) — free/busy staff lookup per time slot and one-click substitute assignment when a teacher or aide is out
- Parent module (`ParentsController`, `ParentMeController`, `ParentInvitationsController`) — parent portal with schema/calendar/ugeplan views
- Absence reporting (`AbsenceController`) — parents report absence, staff confirm/dismiss
- Kontakt directory (`ContactDirectoryController`) — role-filtered parent directory with `ShareContactInfo` consent
- Kontaktbog (`ContactThreadsController`) — per-child parent↔teacher message threads
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
- Vacation registration / ferieindmelding (`VacationRegistrationController`) — admin creates registration windows with granularity (weeks/days) and deadlines; parents submit vacation requests via `ParentFerieindmeldingPage`; admin reviews all entries and manages windows via `FerieindmeldingPage` / `FerieindmeldingDetailPage`; full CRUD on windows with open/closed toggle and CSV export of responses

## Coding conventions

### API (ASP.NET Core / C#)

- **API style**: RESTful, versioned at `/api/v1/`. OpenAPI spec is generated from code (Swashbuckle). Do not hand-write the spec.
- **Error responses**: all API errors use `ProblemDetails` format (RFC 7807). Never return plain strings or custom error shapes.
- **Auth**: all endpoints require a valid Keycloak-issued JWT bearer token unless explicitly decorated to allow anonymous. Never skip auth on endpoints that touch tenant data.
- **EF Core migrations**: never modify an existing migration file. Always generate a new migration for schema changes.
- **Tenant scoping**: the `ITenantContext` service is injected and used in the `DbContext` global query filter. Never pass TenantId as a method parameter through business logic — it must come from the context.
- **Authorization**: role and ClassPermission logic (admin vs. staff, superadmin-mode vs. restricted-mode class editing) is non-obvious — see [docs/AUTHORIZATION.md](docs/AUTHORIZATION.md) before touching auth on any endpoint.

### Frontend (React / TypeScript)

- **API client**: always use the generated typed client (hey-api/openapi-ts). Never hand-write fetch calls to API endpoints that are in the spec.
- **Styling**: Tailwind utility classes only. No CSS-in-JS, no inline `style` props, no separate `.css` files for component styles.
- **Components**: functional components with hooks only. No class components.

### General

- Style and naming conventions (indentation, casing, imports) are enforced by `.editorconfig` and the project linting setup. Do not duplicate those rules here.
- Danish domain terms: klasse (class), fag (course), lokale (room), lektion (time slot), lærer (teacher), pædagog (aide), vikar (substitute), skema (schema/schedule). These are the only Danish words allowed in code identifiers (class names, file names, routes). Everything else in code must be English — UI text and URL paths stay Danish, since the product is Danish-language only. See [docs/GLOSSARY.md](docs/GLOSSARY.md) for the full kept-word list and the mapping of incidental Danish names already renamed to English.

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
4. **Playwright e2e**: `cd web && npx playwright test --reporter=line` — starts Aspire stack automatically. Pass `SKIP_ASPIRE=1` if already running.

Do not report a task as complete until all four pass.

**Use the skills instead of running commands manually:**
- `/verify` — runs steps 1–3 (TypeScript build, dotnet format, dotnet build, API integration tests)
- `/test` — runs step 4 (Playwright e2e)
- `/add-migration` — generates a new EF Core migration after model changes

## Documentation map

| Doc | Read it for |
|---|---|
| [docs/VISION.md](docs/VISION.md) | Mission, why scope is broad-but-narrow, the "does this save Hanne time" filter |
| [docs/PRD.md](docs/PRD.md) | Full product requirements, target segments, competitive positioning, out-of-scope list |
| [docs/PERSONAS.md](docs/PERSONAS.md) | Hanne/Thomas/Birgitte/Mikkel — the four users every screen must work for |
| [docs/adr/INDEX.md](docs/adr/INDEX.md) | Concept → ADR lookup for all product/architecture decisions |
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
- Trust a URL slug as an authorization token — always resolve to TenantId first
- Bypass Stripe Checkout for billing (no manual invoicing, no MobilePay)

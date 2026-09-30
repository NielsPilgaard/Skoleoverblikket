# Skoleoverblikket

SaaS schema planner for Danish friskoler and private/independent schools. Schools build, manage, and print their weekly class schedules — with real-time conflict detection for teachers, rooms, and aides.

## What it does

- **Schema planner** — weekly grid per class, assign course + teacher + room to each time slot
- **Conflict detection** — real-time validation: no double-booked teachers, rooms, or aides
- **Time slot wizard** — school defines default lesson structure (durations, breaks); classes inherit with optional overrides
- **Staff management** — teachers, aides, substitutes; invitation-based onboarding
- **File explorer** — upload files linked to courses for easy reference
- **Stats** — hours per course, hours per teacher/aide, unassigned slots
- **Printable schemas** — per class, per teacher, per room; designed for A4 print
- **Billing** — self-serve via Stripe Checkout, 30-day free trial, auto-renew monthly

## Tech stack

| Layer          | Choice                                              |
| -------------- | --------------------------------------------------- |
| API            | ASP.NET Core Web API, C# / .NET 10                  |
| ORM            | Entity Framework Core                               |
| Database       | PostgreSQL (Docker Compose, self-hosted on OVH VPS) |
| Frontend       | React + Vite + TypeScript + Tailwind CSS            |
| Auth           | Keycloak (Docker Compose)                           |
| API client     | Generated from OpenAPI spec (`openapi-typescript`)  |
| Email          | Scaleway TEM via SMTP (MailKit)                     |
| Object storage | OVHCloud Object Storage (S3-compatible, EU)         |
| Reverse proxy  | Traefik (managed by Dokploy)                        |
| Hosting        | OVHCloud VPS + Dokploy + Docker Compose             |

## Repository layout

```
/api                    ASP.NET Core Web API
/web                    React + Vite frontend
/infrastructure/aspire  .NET Aspire local orchestration
/infrastructure/docker  Production docker-compose + Caddyfile
/infrastructure/keycloak  Keycloak realm export
/scripts                Developer scripts
/docs                   PRD, pricing, decisions, and other documentation
/tasks                  Phase-by-phase implementation tasks
```

## Key architecture rules

**Multi-tenancy**: every query that touches school, staff, class, course, schema, or file data must be scoped to a tenant via EF Core global query filters. Never bypass them. Never trust a URL slug as an authorization signal — always resolve to a `TenantId` at the middleware boundary.

**Responsive UI**: admin UI (schema builder) is laptop-first. Staff schedule views must work on phones and tablets.

**Simplicity first**: the primary admin user is a school secretary with limited time. Every feature must be operable without training.

## Pricing

| Tier            | Storage | Monthly |
| --------------- | ------- | ------- |
| Basis           | 100 GB  | 499 kr  |
| Skole+ (future) | 1000 GB | 499 kr  |

30-day free trial with full access. No per-student pricing.

See [docs/PRICING.md](docs/PRICING.md) for full details.

## Local development

### Prerequisites

- [.NET 10 SDK](https://dot.net/download)
- [Node.js 20 LTS](https://nodejs.org)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

Run the setup script (Windows) to install prerequisites automatically:

```powershell
.\scripts\setup.ps1
```

### Start the dev stack

The local dev environment runs via [.NET Aspire](https://learn.microsoft.com/aspire), which orchestrates all containers (PostgreSQL, Keycloak, LocalStack, API, web).

```powershell
aspire run
```

This starts:
| Service | URL |
| ---------- | ---------------------------- |
| Web app | http://localhost:5173 |
| API | http://localhost:5000 |
| Swagger UI | http://localhost:5000/api/v1/openapi |
| Keycloak | http://localhost:8080 |
| PgAdmin | http://localhost:5050 |
| LocalStack | http://localhost:4566 |

The Keycloak realm (`Skoleoverblikket`) is imported automatically from `infrastructure/keycloak/realms/Skoleoverblikket-realm.json` on first startup.

### Aspire parameters

Aspire uses parameter secrets for local dev. On first run, set:

```powershell
# PostgreSQL password
dotnet user-secrets set "Parameters:postgres-password" "your-dev-password" --project infrastructure/aspire/Skoleoverblikket.AppHost

# Keycloak admin password
dotnet user-secrets set "Parameters:keycloak-admin-password" "your-dev-password" --project infrastructure/aspire/Skoleoverblikket.AppHost

# Scaleway TEM (Email)
dotnet user-secrets set "Smtp:Username" "your-scaleway-username" --project api/Skoleoverblikket.Api
dotnet user-secrets set "Smtp:Password" "your-scaleway-api-key" --project api/Skoleoverblikket.Api


# OVHcloud S3
dotnet user-secrets set "ObjectStorage:AccessKey" "your-s3-accesskey" --project api/Skoleoverblikket.Api
dotnet user-secrets set "ObjectStorage:SecretKey" "your-s3-secretkey" --project api/Skoleoverblikket.Api

```

### Dev login credentials

A test school and admin user are seeded automatically in development:

| Field    | Value                       |
| -------- | --------------------------- |
| URL      | http://localhost:5173       |
| Username | `admin@debugskolen.dk`      |
| Password | `test1234`                  |
| School   | Debugskolen (`debugskolen`) |

The Keycloak user is imported from the realm JSON. The school row is seeded by the API on startup.

> **First run only:** Keycloak imports the realm only when its database is empty. If Keycloak was already running before these credentials were added, delete the `keycloak-db` Docker volume and restart the stack so the realm is re-imported.

### Database migrations

Add a new EF Core migration from the repo root:

```powershell
.\scripts\add-migration.ps1 -MigrationName YourMigrationName
```

Migrations live in `api/Skoleoverblikket.Api/Data/Migrations/`. Never edit an existing migration file — always add a new one.

### API client codegen

When the API has changed, regenerate the typed TypeScript client:

```powershell
# 1. Build the API (emits openapi spec to web/openapi/v1.json)
dotnet build api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj

# 2. Run codegen
cd web && npm run codegen
```

## Documentation

- [PRD](docs/PRD.md) — product requirements and feature spec
- [Personas](docs/PERSONAS.md) — the real users every design decision is measured against
- [Schema features](docs/schema-features.md) — schema planner detail
- [Decisions](docs/DECISIONS.md) — product and architecture decisions
- [Tasks](docs/TASKS.md) — phased implementation plan
- [Pricing](docs/PRICING.md) — tier breakdown
- [Testing](docs/TESTING.md) — testing strategy
- [Contacts](media/CONTACTS.md) — discovery interview contacts

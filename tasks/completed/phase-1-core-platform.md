# Phase 1 — Core platform

## Goal

Build the core product: tenant setup, schema planner with conflict detection, staff/class/course/room CRUD, printable schemas, and basic stats.

---

## Tasks

### Tenant routing and slug

- [x] Tenant slug design and enforcement
  - Admin picks slug at signup (e.g. `vores-friskole`)
  - Validation: lowercase letters, digits, hyphens only; 3–40 chars; globally unique
  - Reserved words blocked: `api`, `admin`, `www`, `static`, `health`, `app`, `dashboard`, `login`, `logout`, `signup`
  - Slug is immutable immediately after creation — no self-serve rename; corrections via support
- [x] Path-based tenant resolution middleware
  - Extracts slug from URL path prefix: `/{slug}/...`
  - Resolves slug → TenantId via cached DB lookup; returns HTTP 404 for unknown slugs
  - URL slug is authoritative; SlugResolutionMiddleware validates slug uniqueness and populates ITenantContext.TenantId
  - Middleware name: `SlugResolutionMiddleware` (extracts slug, resolves to ID, injects via context Items key "TenantId")
  - Failure mode: HTTP 404 when slug is unknown
  - JWT tenant_id claim is not compared; ITenantContext derives solely from URL slug resolver
  - All downstream services read TenantId from context — never from URL or JWT claim directly

### Tenant / school setup

- [x] Tenant creation flow: signup form at `/signup`, picks slug, `POST /api/v1/tenants`
- [x] School settings page (admin): school name, contact info — `GET/PUT /api/v1/schools/settings`, `/indstillinger`
- [x] Logo upload (→ OVHCloud Object Storage / LocalStack in dev) — `POST /api/v1/schools/logo`

### Authentication

- [x] Keycloak realm configured: `skoleoverblikket-web` public OIDC client, `skoleoverblikket-api` bearer-only resource server
- [x] Role-based access: `admin`, `teacher`, `aide` roles in Keycloak; mapped to JWT claims; enforced on API endpoints via `[Authorize(Roles = "...")]`
- [x] Frontend Keycloak OIDC login flow (`keycloak-js`, PKCE, auto token refresh)
- [x] All API endpoints require a valid Keycloak-issued JWT; tenant_id claim drives `ITenantContext`

### Room management

- [x] CRUD API for rooms (lokaler): name, capacity, description — `GET/POST/PUT/DELETE /api/v1/rooms`
- [x] Room list view (admin) — `RoomsPage`

### Staff management

- [x] Staff register CRUD API (admin): name, email, phone, role (Teacher/Aide/Substitute) — `GET/POST/PUT/DELETE /api/v1/staff`
- [x] Staff list view (admin) — `StaffPage`
- [ ] Staff invitation email flow (send Keycloak invite link to new staff member) — Phase 2

### Class management

- [x] CRUD API for classes (klasser): name, description — `GET/POST/PUT/DELETE /api/v1/classes`
- [x] Class list view (admin) — `ClassesPage`

### Course management

- [x] CRUD API for courses (fag): name, description — `GET/POST/PUT/DELETE /api/v1/courses`
- [x] Course list view (admin) — `CoursesPage`

### Time slot template

- [x] School-level time slot template API: lesson duration, breaks, school day start/end — `GET/PUT /api/v1/time-slot-template`
- [ ] Time slot wizard for onboarding (see [SCHEMA_FEATURES.md](../../docs/SCHEMA_FEATURES.md)) — Phase 2
- [x] Per-class time slot overrides
  - **Data model:** `TimeSlotOverride` entity with: classId (FK), dateRange (startDate/endDate), daysOfWeek (bitmask or array), startTime, endTime, recurrenceRule (optional), sortOrder, createdBy (FK)
  - **DB table:** `time_slot_overrides` with columns: id (PK), class_id (FK), start_date, end_date, days_of_week, start_time, end_time, recurrence_rule, sort_order, created_by_id (FK), created_at, updated_at
  - **API endpoints:**
    - `GET /api/v1/classes/{classId}/time-slots` — returns effective (template or overridden) slots for class
    - `GET /api/v1/classes/{classId}/time-slot-overrides` — list active overrides for class
    - `POST /api/v1/classes/{classId}/time-slot-overrides` — create override (request: daysOfWeek, startTime, endTime, dateRange, recurrenceRule)
    - `PUT /api/v1/classes/{classId}/time-slot-overrides/{overrideId}` — update override
    - `DELETE /api/v1/classes/{classId}/time-slot-overrides/{overrideId}` — delete override (returns HTTP 200 or 404)
  - **Response shape:** `TimeSlotOverrideDto` with id, classId, daysOfWeek, startTime, endTime, dateRange, recurrenceRule, sortOrder, createdBy
  - **Authorization:** Admins only ([Authorize(Roles = "admin")])
  - **UI components:**
    - `ClassTimeSlotOverridesPage` — list, create, edit overrides for a class
    - `TimeSlotOverrideForm` — form to define override times and recurrence
    - Conflict validation: warning if override conflicts with existing assignments in active schema
    - Preview: show effective schedule before saving override

### Schema builder

- [x] CRUD API for schemas per class — `GET/POST/DELETE /api/v1/classes/{classId}/schemas`
- [x] Weekly grid UI — `SchemaBuilderPage`
- [x] Cell assignment: course + teacher + room + optional aide via `PUT /api/v1/classes/{classId}/schemas/{schemaId}/slots`
- [x] Real-time conflict detection (teacher, room, aide double-booking with clock-time overlap)
- [x] Conflict display: returned on every slot upsert via `SlotsAndConflictsDto`
- [x] Schema marked complete blocked when conflicts exist (HTTP 422)
- [x] Conflict highlight UI in schema builder grid
- [x] Copy/duplicate schema from one class to another

### Printable schemas

- [x] Per-class weekly schema (print-friendly, A4)
- [x] Per-teacher weekly schema (all classes for one teacher)
- [x] Per-room weekly schema (all classes using one room)
- [x] Include generation date on all printable views

### Stats dashboard

- [x] Hours per course per class — `GET /api/v1/stats/dashboard`
- [x] Hours per teacher / aide
- [x] Unassigned slots count
- [x] Stats displayed on admin dashboard — `DashboardPage`

### Staff schedule views

- [x] Teacher: view own weekly schedule across all classes (read-only)
  - **Endpoint:** `GET /api/v1/staff/{staffId}/schedule` (authenticated, bearer token required)
  - **Authorization:** User must be authenticated; [Authorize]
  - **Response:** JSON array of `ScheduleSlotDto` objects
  - **Fields per slot:** weekday (int 1–5), startTime (HH:mm string), endTime (HH:mm string), courseName, className, roomId, roomName, teacherId, teacherName, aideId, aideName
  - **Status codes:** 200 OK, 401 Unauthorized, 404 Not Found (if staffId invalid)
  - **Query parameters:** (optional) weekStart (ISO date), date (ISO date to filter by specific day)
  - **UI:** `StaffSchedulePage` displays weekly schedule in expanded card view per day; read-only (no edit/delete); print-friendly (via `/udskriv/medarbejder/{staffId}`)

- [x] Aide: view own weekly schedule (read-only)
  - Uses same endpoint `GET /api/v1/staff/{staffId}/schedule`
  - Returns slots where aideId matches the authenticated staff member or teacherId matches if aide is teaching

- [x] Room schedule: view which classes use a room and when
  - **Endpoint:** `GET /api/v1/rooms/{roomId}/schedule` (authenticated, bearer token required)
  - **Authorization:** User must be authenticated; [Authorize]
  - **Response:** JSON array of `ScheduleSlotDto` objects
  - **Fields per slot:** weekday (int 1–5), startTime (HH:mm), endTime (HH:mm), courseName, className, teacherId, teacherName, aideId, aideName, roomId, roomName
  - **Path param validation:** roomId must be valid UUID; return 404 if room not found
  - **Status codes:** 200 OK (with array), 401 Unauthorized, 404 Not Found (if roomId invalid)
  - **Query parameters:** (optional) weekStart, date
  - **UI:** `RoomSchedulePage` displays weekly belægning grid; read-only; print-friendly (via `/udskriv/lokale/{roomId}`)

### Admin dashboard

- [x] School overview: class count, staff count, course count — `DashboardPage`
- [x] Schema completeness indicator
- [x] Quick links to schema builder, staff list, course list via sidebar

### Responsive UI validation

- [x] Schema builder and admin views usable on laptop-size screens (1280px+)
- [x] Teacher/aide schedule views fully functional on phone (375px+)
- [x] No feature unusable at any screen size (per responsive-ui ADR)

### Testing

- [x] Integration test project: tUnit + Testcontainers (real PostgreSQL) + `WebApplicationFactory`
- [x] Test auth handler bypasses Keycloak for integration tests
- [x] CRUD tests: full room lifecycle (create, read, update, delete, 404, validation)
- [x] Tenant isolation tests: data from tenant A is invisible to tenant B; cross-tenant ID lookup returns 404
- [x] Conflict detection tests: teacher double-booking, room double-booking, mark-complete blocked on conflicts

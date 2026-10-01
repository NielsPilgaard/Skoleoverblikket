---
title: 'Feedback button — capture, backoffice and status updates'
purpose: 'Let non-technical users report bugs and feedback in two clicks with all the technical context captured automatically, so nobody has to write emails and we stay connected to users.'
description: >-
  A "Tilbagemelding" button for admin/staff/board (parents later, per-tenant
  flag) that captures a screenshot, a masked DOM outline and the last 100 actions
  before opening a single text field with dictation (Scaleway Whisper). Reports
  are redacted server-side, stored in the EU, shown in a SuperAdmin backoffice,
  emailed to the owner, and the reporter gets status notifications. Phase 2 of
  the feedback system — no AI yet beyond speech-to-text.
status: 'Proposed'
---

# Feedback button — capture, backoffice and status updates

## TL;DR

Click "Tilbagemelding" → screenshot + masked DOM outline taken **before** the panel opens → one text field ("Fortæl hvad du gjorde, og hvad du forventede der ville ske") with a mic button for dictation → Send. The last 100 actions and technical context are attached automatically. The API stores a **raw** copy (owner-only) and a **masked/redacted** copy (the only thing AI ever sees, task 46). New `FeedbackReport` + `FeedbackEvent` timeline, SuperAdmin page "Tilbagemeldinger", email to the owner, in-app/email status notifications and a "Mine tilbagemeldinger" list for the reporter. Data boundary: [ai-data-boundary](../../docs/adr/ai-data-boundary.md).

## Context

Today users who hit a bug have to email, and most won't — Hanne doesn't know what a "browser version" is and won't describe the steps. We lose bugs and lose touch with users. Ideas the grilling settled:

- No AI chat during intake: write or dictate, then send. Fewer steps, cheaper.
- No `getDisplayMedia` ("choose a screen to share" confuses non-technical users) — DOM-rendered screenshot instead.
- No rrweb (heavy, records text) — a small custom ring buffer.

## Decisions (confirmed)

- **D1 — Who**: admin, staff, board. Parents behind per-tenant setting `FeedbackEnabledForParents` (default off) after ~1 month of real data.
- **D2 — Rate limits**: 5 reports per user per day, 30 per tenant per day (ProblemDetails 429 with Danish message).
- **D3 — Intake**: single free-text field + dictation. No AI follow-up questions. No intake LLM.
- **D4 — Dictation**: Scaleway Generative APIs `whisper-large-v3` (EU, €0.003 per audio minute, first 60 minutes free, max 25 MB, accepts webm/mp4/m4a/ogg/wav, so both Chrome's and Safari's `MediaRecorder` output work as-is). Audio is transcribed and discarded, never stored. Alexandra Instituttet has no speech-to-text on its platform (checked 2026-10-01; their Danish ASR work, CORAL, would mean self-hosting).
- **D5 — Two copies**: raw (screenshot + text) for the owner in the backoffice; masked screenshot + DOM outline + redacted text/log for AI.
- **D6 — Reporter feedback**: status notifications only (Modtaget → Vi kigger på det → Rettet / Lukket med begrundelse). Timeline model so a two-way thread can be added later.
- **D7 — Retention**: screenshots deleted 30 days after the report is closed, or 90 days after creation, whichever comes first. Redacted text and log kept.

## Scope

### 1. Frontend capture (`web/src/lib/feedback/`)

**Action log** (`actionLog.ts`) — module-level ring buffer of 100 entries, started in `main.tsx`:

| Kind | Recorded |
|---|---|
| `route` | route **pattern** (`/:slug/klasser/:classId/skema`), never IDs or query values |
| `click` | `data-testid`, else aria-label, else role + visible text of buttons/links — skipped inside `[data-sensitive]` |
| `api` | method, route template (from the hey-api request interceptor), status, duration, correlation id — via `client.interceptors` in the generated client setup, not by editing generated files |
| `error` | `console.error`, `window.onerror`, `unhandledrejection` — message + top stack frame, truncated |
| `viewport` | resize to a new breakpoint |

Never records typed text, input values or response bodies.

**Screenshot** (`capture.ts`) — `modern-screenshot` (or equivalent DOM-to-image), **lazy-loaded** on click so it doesn't hit the main bundle. Two renders:
- raw: as-is;
- masked: in the cloned DOM, blur/replace text of `input`, `textarea`, `[contenteditable]` and `[data-sensitive]`, and replace `img[data-sensitive]` with grey boxes.

**DOM outline** (`outline.ts`) — compact JSON of landmarks, headings, buttons, links, form fields (type + label only), tables (column headers + row count), `data-testid`s, element widths and an `overflow` flag where `scrollWidth > clientWidth`. Text only from UI chrome (buttons, labels, headings, `th`) and not inside `[data-sensitive]`; truncated. This is what text-only LLMs use (task 46).

**Context**: app version (`VITE_APP_VERSION` = image sha), user agent, viewport, device pixel ratio, role, current route pattern, tenant plan/modules, last failed request's correlation id.

**`data-sensitive` markup**: add to the shared components that render personal data — person names, avatars, message bodies (Beskeder, kontaktbog, klassechat), absence reasons, free-text notes, contact info. List the components touched in this task when done.

### 2. Frontend UI

- `FeedbackButton` in the app header (desktop) and the profile menu (mobile). Not floating: a floating button covers the schema grid and phone content. Icon + "Tilbagemelding".
- Click → capture → open `Modal` with:
  - textarea, placeholder *"Fortæl hvad du gjorde, og hvad du forventede der ville ske."*
  - mic button: `MediaRecorder` (webm/opus or mp4 on Safari), max 2 minutes, visible timer, text appended to the field when transcribed;
  - masked screenshot preview + toggle "Vedhæft skærmbillede" (default on);
  - info line: *"Vi sender et skærmbillede og dine seneste klik med, så vi kan se, hvad der skete. Skriv venligst ikke elevers navne."*;
  - "Send" → toast "Tak! Vi har modtaget din tilbagemelding."
- "Mine tilbagemeldinger" page in the profile menu: list with status and the reasons the owner wrote.
- All copy in Danish, testids on every interactive element.

### 3. API

New `FeedbackController` (`/api/v1/feedback`), `FeedbackService` owning the rules:

- `POST /feedback/uploads` → presigned PUTs for raw + masked screenshot (existing presign+confirm pattern used for avatars), **private** object keys under `feedback/{tenantId}/{reportId}/`.
- `POST /feedback` → creates the report (description, outline, action log, context, upload confirm tokens). Validates role/tenant setting and rate limits.
- `POST /feedback/transcriptions` → audio (≤ 2 min, ≤ 5 MB) → Scaleway Whisper → `{ text }`. Not stored. Rate limited separately (20/user/day).
- `GET /feedback/mine` → reporter's own reports with reporter-visible events.
- SuperAdmin endpoints (follow the existing `SuperAdminTenantsController` cross-tenant pattern; see [AUTHORIZATION.md](../../docs/AUTHORIZATION.md)): list/filter, detail with short-lived presigned GETs for both screenshots, `POST …/{id}/status` (status + optional reporter-visible message), `POST …/{id}/duplicate-of/{otherId}`.

**Redaction** (`FeedbackRedactor`), applied to description, outline text and action-log labels before storing the redacted copies:
- names of the tenant's students, parents and staff (first, last and full names, case-insensitive, word-boundary) → `[elev]`, `[forælder]`, `[medarbejder]`. Over-redaction ("Mark") is acceptable;
- CPR (`\d{6}-?\d{4}`), Danish phone numbers, email addresses → `[cpr]`, `[telefon]`, `[email]`.

**Entities** (migration via `/add-migration`, written by a human):

- `FeedbackReport`: `TenantId`, `ReporterUserId`, `ReporterRole`, `Description`, `DescriptionRedacted`, `RawScreenshotKey?`, `MaskedScreenshotKey?`, `DomOutline` (jsonb, redacted), `ActionLog` (jsonb, redacted), `Context` (jsonb), `Status` (`Received`, `InProgress`, `Fixed`, `Closed`), `Category?` (task 46), `DuplicateOfId?`, `CreatedAt`, `ClosedAt?`.
- `FeedbackEvent`: `ReportId`, `Type` (`Created`, `StatusChanged`, `MessageToReporter`, `MarkedDuplicate`, … later `Triage*`, `Fix*`), `Payload` (jsonb), `VisibleToReporter`, `CreatedAt`.
- `Tenant.FeedbackEnabledForParents` (default false).

**Notifications**: new notification type `FeedbackStatusChanged` through the existing `NotificationsController` / `NotificationPreference` (in-app + email, opt-out). Duplicates inherit status changes of their original.

**Email to owner**: on every new report, to `Feedback__NotifyEmail` — tenant, role, route, redacted description, link to the backoffice. No screenshot in the email.

**Retention**: background job (daily) deletes screenshot objects per D7 and nulls the keys.

### 4. Backoffice page

SuperAdmin → "Tilbagemeldinger": table (date, school, role, route, status, category), filters. Detail: raw + masked screenshot side by side, description (raw and redacted), action log as a timeline, context, event history, buttons for status change with message, mark duplicate.

## Open questions

- Is the redactor's name list loaded per request fast enough for a 300-student school? (Expected yes — a few hundred strings; cache per tenant for a minute if not.)
- Which speech-to-text provider. The API hides it behind `ISpeechToText` (config `SpeechToText__Provider`). Before shipping the mic button, run a bake-off on ~10 real Danish dictations (casual, dialect, school vocabulary) and pick on accuracy, since cost is negligible at our volume. Candidates (checked 2026-10-01):
  - **Scaleway `whisper-large-v3`**: default; OpenAI-compatible, €0.003/min, 60 min free, EU (FR).
  - **syv.ai Hviske** (Copenhagen): Danish-specialised, API offered, but pricing, hosting location and DPA aren't public. Contact them.
  - **Corti** (Danish): Danish "Premier" tier, $0.0065/min, $50 free credits, EU processing (Ireland). Medical-tuned and healthcare-positioned, so check that the terms allow non-medical use.

## Out of scope

- AI triage, categories and reply drafts — [task 46](46-feedback-ai-triage.md).
- Two-way conversation with the reporter (model is ready for it).
- Parents (setting exists, default off).
- Feedback from the logged-out marketing site.

## Testing

API integration (`FeedbackTests.cs`):
- staff creates a report → stored with redacted copies; names of the tenant's students/staff and a CPR in the description are redacted in `DescriptionRedacted`, raw kept.
- tenant isolation: another tenant's staff can't read it; `GET /feedback/mine` only returns own reports.
- parent gets 403 when `FeedbackEnabledForParents` is off, 201 when on.
- rate limit returns 429 ProblemDetails on report #6.
- SuperAdmin status change creates a reporter-visible event and a notification; duplicates follow.

Playwright e2e (`feedback.spec.ts`): staff clicks Tilbagemelding, writes text, sends, sees the report in "Mine tilbagemeldinger"; SuperAdmin sees it in the backoffice and closes it with a message; staff sees the message. Dictation not covered by e2e (external STT).

---
title: 'AI suggestions for lesson content and lektier in the ugeplan'
purpose: 'Spec for a one-click "Foreslå indhold" button in the ugeplan edit modal that drafts the next lesson''s description and lektier from the class''s recent ugeplan history.'
description: >-
  Adds a "Foreslå indhold" button to the ugeplan EditSlotModal. One click sends
  the class's recent lessons in the same course (about 12 lessons with content,
  at most 6 weeks back) plus the week notes, redacted for names, to the EU LLM
  (Scaleway) and fills Beskrivelse and Lektier as an editable draft. Button
  only, no chat. Included in Basis, school-level switch default on, 30 calls per
  user per day, shared monthly budget. Suggestion text is never stored.
status: 'Ready'
---

# AI suggestions for lesson content and lektier in the ugeplan

## TL;DR

In the ugeplan edit modal, a teacher clicks **Foreslå indhold**. The API collects this class's most recent lessons in the same course (up to ~12 lessons with content, max 6 weeks back), the week notes for those weeks and the target week, replaces student and staff names with placeholders, and asks the EU LLM ([ai-data-boundary](../docs/adr/ai-data-boundary.md)) for the natural next lesson. The answer fills Beskrivelse and/or Lektier as an unsaved draft; the teacher edits and presses Gem as usual. **Nyt forslag** regenerates. No chat, no free-text prompt. Included in Basis, admin can turn it off per school, 30 calls per user per day, shared monthly LLM budget. Only usage counts are stored, never the prompt or the suggestion.

## Why

- Thomas (teacher) fills in the ugeplan every week for every course. Most of that is "what comes after last week": the next step in a topic and homework that fits. A good draft in one click saves minutes per lesson, every week, for every teacher. This is daily value, unlike [25](25-schema-import.md) (onboarding) and [21](21-ai-suggestions-for-schema.md) (once a year).
- Button, not chat: Hanne and Thomas shouldn't have to learn to prompt. The input is data we already have.

## Context

- Lesson content already exists: `WeekPlanSlot.Description` and `WeekPlanSlot.Lektier` (Markdown, 8000 chars each), per `SchemaSlot` per ISO week. `WeekPlan.Notes` is the class-wide note for the week. Course is `WeekPlanSlot.OverrideCourseId` if set, else `SchemaSlot.Course`. Klassetrin is `Class.GradeLevel` (nullable).
- Editing happens in `EditSlotModal` ([WeekPlanPage.tsx](../web/src/pages/WeekPlanPage.tsx)), which keeps a local autosave draft. Writes are authorized by the `EditWeekPlan` policy ([AUTHORIZATION.md](../docs/AUTHORIZATION.md#week-plan-writes-editweekplan)).
- `WeekPlanController` is a legacy controller that writes `AppDbContext` directly; `WeekPlanService` exists. The new endpoint goes through a new service (below), per AGENTS.md.
- The shared LLM client (`ILlmClient`, `Llm__*` config, `LlmUsage` table, monthly budget) is specified in [46](feedback/46-feedback-ai-triage.md) and may be built first by [25](25-schema-import.md). Whichever of 25, 46, 66 lands first builds it.
- Scaleway is already on the sub-processor list for both email and AI (`web/src/content/dataProcessing.ts`, names ugeplan suggestions). Nothing to notify.

## Decisions

- **D1 — Unit**: one lesson per click, from inside `EditSlotModal`. No week-level "fill everything" button.
- **D2 — History**: same class, same effective course. The most recent lessons *with content* (Beskrivelse or Lektier non-empty) before the target lesson, including earlier lessons in the same week. Cap at 12 lessons or 6 ISO weeks back, whichever comes first. Counting lessons instead of calendar weeks means holidays don't leave an empty history. Each lesson is sent as date, weekday, Beskrivelse, Lektier. Parallel classes (4.a/4.b) are **not** used.
- **D3 — Week notes**: `WeekPlan.Notes` for every week the history touches, plus the target week's notes. File contents are not sent; file names at most.
- **D4 — Redaction**: deterministic, in the API, before the call. Student names of the class (first name, full name, first name + last initial) → `[elev]`; staff names in the tenant → `[lærer]`; CPR, phone, email by regex. Whole-word, case-sensitive, capitalised matches only (so "maj" the month survives a student called Maj). Build as a shared `PiiRedactor` service if 46 hasn't, and reuse it there. The prompt also says "nævn aldrig elever ved navn".
- **D5 — Access and cost**:
  - Included in Basis, not a module.
  - School setting **"Tillad AI-forslag i ugeplanen"** on `School` (`AllowAiWeekPlanSuggestions`, default `true`), toggled by an admin on the school settings page. Off → button hidden, API returns 403 ProblemDetails.
  - Anyone who passes `EditWeekPlan` for the slot may use it. No new role.
  - 30 calls per user per day → 429 ProblemDetails "Du har brugt dagens AI-forslag. Prøv igen i morgen."
  - Shared `Llm__MonthlyBudgetEur` ([46](feedback/46-feedback-ai-triage.md) D6). Over budget → 503 ProblemDetails "AI-forslag er midlertidigt utilgængelige."
  - LLM not configured (`Llm__BaseUrl`/`ApiKey` unset) → button hidden (expose via the existing settings or a small capability flag on the week plan response).
- **D6 — Cold start**: fewer than 2 earlier lessons with content → still generate from course + klassetrin + date. Response carries `limitedHistory: true`; the modal shows a grey line "Få tidligere lektioner at bygge på — forslaget er generelt."
- **D7 — Fill rules** (steering without typing), based on what is in the modal fields *now* (the draft, not the saved value):
  - Both empty → suggest both.
  - Beskrivelse filled, Lektier empty → keep Beskrivelse verbatim as given, suggest Lektier that fits it.
  - Lektier filled, Beskrivelse empty → suggest Beskrivelse only.
  - Both filled → confirm "Erstat det du har skrevet?" then suggest both.
  - The current field text is redacted like the history before sending.
- **D8 — Regenerate**: after a suggestion, the button reads **Nyt forslag**. It sends the same input plus the previous suggestion with "giv et andet bud", at a higher temperature. No free text, no chips ("kortere", "differentieret") in v1.
- **D9 — Output**: model returns JSON `{ "beskrivelse": string, "lektier": string }`. One retry on invalid JSON, then ProblemDetails "Kunne ikke lave et forslag, prøv igen." Short Markdown: Beskrivelse ≤ ~5 lines (goal + 2–4 activities), Lektier ≤ ~2 lines, hard-truncated at 8000. Lektier may be empty; if the history has no lektier for this course, don't invent any. Ask for the natural next step at the klassetrin level; don't cite specific Fælles Mål (hallucination risk). Danish, teacher tone, no `[elev]`/`[lærer]` in the output (strip if present).
- **D10 — Storage**: the suggestion is never stored server-side; it goes straight into the modal draft and becomes the teacher's own text when saved. Each call writes one `LlmUsage` row (46's table, `Feature = "weekplan-suggest"`, plus nullable `UserId` and `TenantId` for the per-user cap), no prompt or response. Delete `LlmUsage` rows older than 90 days.
- **D11 — Transparency**: no "AI-genereret" label towards parents; the teacher reviewed and owns the text. Under the button, small grey text: "Forslaget laves af AI ud fra klassens seneste ugeplaner. Læs det igennem, før du gemmer." The sub-processor list covers the processing.
- **D12 — Model**: eval like 25/46 before go-live. Ten synthetic ugeplan histories (dansk, matematik, engelsk, natur/teknik; indskoling to udskoling; one with a holiday gap; one cold start) through GLM 5.3 Flash (if listed), `glm-5.3` and `deepseek-v4.1-flash`. Judge Danish quality, sensible progression, valid JSON first try, cost per call. Cheapest good-enough model goes in `Llm__Models__WeekPlanSuggestion`. Synthetic data only; no real school data in the repo.

## Scope

### API

- `Services/WeekPlanSuggestionService.cs` (sealed, primary constructor: `AppDbContext`, `ITenantContext`, `ILlmClient`, `PiiRedactor`, `TimeProvider`). One public method `SuggestAsync(classId, request, userId, ct)` returning a small result enum + DTO (`Ok`, `NotFound`, `Disabled`, `DailyLimit`, `BudgetExceeded`, `LlmFailed`). Loads history with `AsNoTracking` projections, redacts, builds the prompt, calls the LLM, validates JSON, writes `LlmUsage`, saves once.
- Endpoint on `WeekPlanController`:

  ```
  POST /api/v1/classes/{classId}/week-plan/suggestions
  Body: { isoYear, isoWeek, schemaSlotId, currentDescription?, currentLektier?, previousSuggestion? }
  200: { description: string | null, lektier: string | null, limitedHistory: bool }
  ```

  Authorized with `EditWeekPlan` on `(classId, schemaSlotId)`, same as the slot PUT. `description`/`lektier` is `null` for a field the fill rule (D7) left alone. The controller only maps the enum to status codes.
- `School.AllowAiWeekPlanSuggestions` (bool, default true) + migration via `/add-migration`. Expose it in `SchoolSettingsDto` / `UpdateSchoolSettingsRequest`. `SchoolsController.UpdateSettings` writes `AppDbContext` directly; since this changes its logic, move settings read/write into a `SchoolSettingsService` in the same PR.
- `PiiRedactor` and `ILlmClient`/`LlmUsage`/budget if not yet built (see [46](feedback/46-feedback-ai-triage.md) for their spec). Register in `AddDomainServices`.
- `LlmUsage` gets nullable `UserId` + `TenantId` columns if 46 built it without them. Per-user cap counts rows for `(UserId, Feature, today in Europe/Copenhagen)`.
- Prompt template as a constant in the service. System prompt in Danish, includes course name, klassetrin (or "ukendt"), target date and weekday, fill mode, and the rules from D9.
- `appsettings.json`: `Llm__Models__WeekPlanSuggestion`. Note in [DEPLOYMENT.md](../docs/DEPLOYMENT.md) alongside the other `Llm__*` keys.

### Web

- `EditSlotModal`: **Foreslå indhold** button (sparkle icon + text, `data-testid="weekplan-suggest"`) between the fields and Gem, shown when the school setting is on and the LLM is configured. Loading state "Laver forslag…" disables the button and both fields. Result fills the fields per D7 and flows into the existing autosave draft. Then the button reads **Nyt forslag**. Errors show the ProblemDetails `detail` inline under the button.
- Confirm dialog for D7 "both filled" (use the existing confirm pattern).
- Grey disclosure line (D11) and `limitedHistory` hint (D6).
- Phone: the modal already works on mobile; check the button doesn't push Gem off-screen at 375px.
- School settings page: checkbox "Tillad AI-forslag i ugeplanen" with one line of help text.
- Landing page: update the ugeplan card in `web/src/content/features.tsx` with one sentence about the AI suggestion.
- `/codegen` after the API change.

### Docs

- [FEATURES.md](../docs/FEATURES.md): add `WeekPlanSuggestionService` under Ugeplan.
- [ai-data-boundary](../docs/adr/ai-data-boundary.md) IMP-001: add this task as a reuser of `ILlmClient`.

## Tests

API integration (`WeekPlanSuggestionTests.cs`), stub `ILlmClient` registered in the `WebApplicationFactory` (external HTTP dependency, allowed by [TESTING.md](../docs/TESTING.md)). The stub records the prompt it received.

- History selection: same class + course only, other class and other course excluded, override course respected, cap at 12 lessons / 6 weeks, holiday gap still finds lessons, target week's notes included.
- Redaction: a student name and a staff name in Description/Notes never reach the stub prompt; "maj" lowercase survives.
- Fill rules: Beskrivelse given → response `description` null and prompt contains the given text; both empty → both returned.
- Cold start → `limitedHistory: true`.
- School setting off → 403. Staff without `EditWeekPlan` on the slot → 403. Other tenant's class → 404.
- 31st call same day → 429. Budget exhausted (seed `LlmUsage`) → 503. Invalid JSON twice → error ProblemDetails, one `LlmUsage` row per attempt.
- Nothing about the suggestion is persisted to `WeekPlanSlot` by the suggest call.

Playwright (one test in the existing ugeplan spec): open a lesson, click `weekplan-suggest`, fields fill with the stubbed text, Gem, text shows in the week view. Needs the stub LLM wired in the e2e environment (a fixed-response fake when `Llm__BaseUrl` points at a test value), or skip e2e and rely on integration tests if wiring a fake into Aspire costs more than the test is worth.

## Out of scope

- Chat or free-text instructions.
- Suggesting a whole week or several courses at once.
- Using parallel classes' plans, files' contents or Fælles Mål data.
- Suggestions for SFO ugeplan.
- Storing suggestions or showing an AI label to parents.

## Done when

- A teacher with edit rights clicks Foreslå indhold on a lesson with history and gets a sensible Danish next-lesson draft in under ~10 seconds, edits it and saves.
- Admin can turn it off; button disappears and API refuses.
- No student or staff name reaches the LLM in tests.
- Model picked from the eval and set in prod config.

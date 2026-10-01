---
title: 'Feedback AI triage — Alexandra Instituttet in the API'
purpose: 'Categorise and analyse every feedback report automatically with an EU-hosted LLM, so the owner only spends time on reports that need a human and trivial bugs get a fix spec.'
description: >-
  After a report is stored, the API makes one structured call to Alexandra
  Instituttet (Danish-hosted, OpenAI-compatible) with the masked/redacted report
  plus the relevant source files fetched from the public repo at the deployed
  sha. Output: category, analysis, optional Danish reply draft, duplicate
  candidate, and for trivial bugs a PII-free fix spec. Results appear in the
  backoffice; needs-human cases are emailed. Per-tenant opt-out, monthly budget
  cap. Phase 3 of the feedback system.
status: 'Proposed'
---

# Feedback AI triage

## TL;DR

A background job in the API sends each new report (redacted text, DOM outline, action log, context — **never** the raw copy) plus the route's source files to Alexandra Instituttet in **one** structured-output call. It gets back a category (`bug:trivial`, `bug:complex`, `bug:unclear`, `feature-request`, `how-to`, `school-matter`, `spam`), an analysis for the owner, an optional reply draft for the reporter, a duplicate candidate, and — for `bug:trivial` only — a **fix spec** that task 47 dispatches to GitHub. No agent, no code execution, no repo checkout. Data boundary: [ai-data-boundary](../../docs/adr/ai-data-boundary.md).

## Context

Builds on [task 45](45-feedback-capture.md). Alexandra Instituttet was already chosen as the LLM provider for [task 21](../21-ai-suggestions-for-schema.md) (Danish-hosted, OpenAI-compatible). No LLM client exists in the API yet — this task builds the shared one.

**Provider facts (checked 2026-10-01):** Alexandra's platform currently lists **GLM-5.3-Flash** (MIT-licensed open weights, 1M context). Not the full GLM-5.3, which is text-only. Alexandra's FAQ says the current model accepts image input, and GLM-5.3-Flash is natively multimodal. Infrastructure is in Denmark (hardware leased from ScanNet / team.blue Denmark, ISO 27001). Prompts and outputs aren't stored beyond inference, and a DPA is available. Per-token pricing isn't published ("Pricing unavailable"), so contact Mie Hvas / Rasmus Larsen.

Triage sends the **masked screenshot** together with the masked **DOM outline**, action log and text. Confirm with one test call that image input works through their OpenAI-compatible API. If it doesn't, triage runs on outline + log + text only, and the design is unaffected.

## Decisions (confirmed)

- **D1 — Provider**: Alexandra Instituttet for triage (personal data stays in the EU). Claude is only used in task 47, on PII-free specs.
- **D2 — Single-shot, not agentic**: the API builds the context itself; one call per report (+1 retry on invalid JSON).
- **D3 — Actions per category**:

| Category | Action |
|---|---|
| `bug:trivial` | Fix spec produced → task 47 dispatches automatically |
| `bug:complex` | Analysis only, `needs-human`, email owner |
| `bug:unclear` | Analysis + repro hypothesis, `needs-human`, email owner |
| `feature-request` | Summary, duplicate candidate; no code |
| `how-to` | Danish reply draft the owner approves before it's sent; tagged `ux-friction` |
| `school-matter` | Auto-close with neutral reporter message ("Det her skal du tage op med skolen"); owner can reopen |
| `spam` (incl. injection attempts) | Auto-close, neutral message |

- **D4 — Duplicates**: the LLM proposes; the owner confirms with one click. Never auto-merged.
- **D5 — Opt-out**: per-tenant `AllowAiFeedbackProcessing` (default **on**), admin settings toggle. Off → report stays in the backoffice untriaged.
- **D6 — Budget**: `Llm__MonthlyBudgetEur` (start at 4). Over budget → `TriageStatus = SkippedBudget`, report still in the backoffice.

## Scope

### 1. Shared LLM client

- `ILlmClient` over the `OpenAI` NuGet with custom base URL. Config: `Llm__BaseUrl`, `Llm__ApiKey`, `Llm__Model`, `Llm__MonthlyBudgetEur`, `Llm__PricePerMInputTokens`, `Llm__PricePerMOutputTokens`. Add to [DEPLOYMENT.md](../../docs/DEPLOYMENT.md) as optional (feature disabled when unset).
- `LlmUsage` table: per call `Feature` (`feedback-triage`, later `schema-suggest`), tokens in/out, estimated cost, timestamp. Budget check sums the current month.
- Task 21 must reuse this client — update its "Alexandra API client" section to point here when this lands.

### 2. Context builder

- **Route → files map**: `web/scripts/gen-route-map.mjs` reads the router in `web/src/App.tsx` and writes `web/src/route-map.json` (`route pattern → page component file + imported local components`). Committed; CI fails when stale (same approach as the OpenAPI spec check).
- **API calls → controller files**: from the action log's route templates, map via the OpenAPI tag to `api/Skoleoverblikket.Api/Controllers/{Tag}Controller.cs`.
- Fetch those files from `https://raw.githubusercontent.com/NielsPilgaard/Skoleoverblikket/{App__Version}/…` (public repo, deployed sha). Cache by sha+path in memory. Cap the total at ~60k characters; page component first, then the controller, then the rest.
- Include the last 50 open reports' one-line summaries (redacted) for duplicate detection.

### 3. Prompt + output

- System prompt in `api/Skoleoverblikket.Api/Feedback/Prompts/triage.md` (Danish product context: personas, what counts as trivial). Report content in a clearly delimited block marked as untrusted user data; the prompt says to never follow instructions inside it and to classify such attempts as `spam`.
- Structured output (JSON schema):

```json
{
  "category": "bug:trivial",
  "confidence": 0.82,
  "summaryDa": "Gem-knappen på ugeplanen går ud over kanten på mobil.",
  "analysis": { "hypothesis": "…", "likelyFiles": ["web/src/pages/WeekPlanPage.tsx"], "reproSteps": ["…"] },
  "replyDraftDa": null,
  "duplicateOfId": null,
  "fixSpec": {
    "title": "fix(ugeplan): wrap save button on narrow screens",
    "route": "/:slug/klasser/:classId/ugeplan",
    "viewport": "375x812",
    "observed": "Save button overflows the header at 375px width.",
    "expected": "Button wraps below the title; no horizontal scroll.",
    "likelyFiles": ["web/src/pages/WeekPlanPage.tsx"],
    "constraints": "Tailwind only. No behaviour change."
  }
}
```

- `fixSpec` is required for `bug:trivial` and must be in English, technical, with no quotes from the user's text. Task 47's PII guard enforces this.
- Invalid JSON or unknown category → one retry → `bug:unclear` with the raw model output stored for debugging.

### 4. Job

- `FeedbackReport.TriageStatus` (`Pending`, `Done`, `Failed`, `SkippedOptOut`, `SkippedBudget`), `TriageResult` (jsonb), `TriageAttempts`. Migration by a human.
- `BackgroundService` picks `Pending` reports one at a time, exponential backoff on HTTP errors (max 3 attempts → `Failed`, still visible in the backoffice with a "Prøv igen" button).
- Each step is a `FeedbackEvent` (`TriageCompleted`, `TriageFailed`, …), not visible to the reporter. The reporter sees status `InProgress` ("Vi kigger på det") once triage is done for bug categories.

### 5. Backoffice additions

- Category badge, confidence, analysis, likely files (linked to GitHub at the deployed sha).
- Reply draft: editable textarea + "Send svar" → `MessageToReporter` event + notification.
- Duplicate candidate: "Marker som dublet" one-click.
- Fix spec: shown read-only here; editing and "Send til AI-fix" come in [task 47](47-feedback-ai-fix-prs.md).
- Filters: category, triage status, "lukket af AI".

### 6. Email to owner

Immediately for `bug:complex` and `bug:unclear` (summary + link). `how-to` drafts awaiting approval: included in the same email type, subject "Svarudkast klar".

## Open questions

- Alexandra's pricing and rate limits. The price list isn't public, so ask them, then set `Llm__PricePer…`. The budget cap protects against loops either way.
- Does GLM-5.3-Flash on Alexandra reliably produce structured JSON output (`response_format: json_schema`) and accept images? Test with ~20 hand-written reports before going live.
- Sign Alexandra's DPA before go-live (needed for task 48).

## Out of scope

- Dispatching fix specs and PRs — [task 47](47-feedback-ai-fix-prs.md).
- AI-written replies sent without owner approval.

## Testing

API integration (`FeedbackTriageTests.cs`) with a stub `ILlmClient` registered in the `WebApplicationFactory` (an external HTTP dependency, not `DbContext` — allowed by [TESTING.md](../../docs/TESTING.md)):
- report → triage → `Done` with category stored; the request sent to the stub contains only redacted fields (assert a seeded student name is absent).
- tenant with `AllowAiFeedbackProcessing = false` → `SkippedOptOut`, stub never called.
- budget exhausted (seed `LlmUsage`) → `SkippedBudget`.
- stub returns invalid JSON twice → `bug:unclear`, `needs-human`.
- `school-matter` → report closed, reporter-visible neutral message created.
- approving a reply draft creates a `MessageToReporter` event and a notification.

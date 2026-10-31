---
title: 'Feedback AI triage — Scaleway Generative APIs in the API'
purpose: 'Categorise and analyse every feedback report automatically with an EU-hosted LLM, so the owner only spends time on reports that need a human and trivial bugs get a fix spec.'
description: >-
  After a report is stored, the API makes one structured call to Scaleway
  Generative APIs (Paris, OpenAI-compatible) with the masked/redacted report
  plus the relevant source files fetched from the public repo at the deployed
  sha. Output: category, analysis, optional Danish reply draft, duplicate
  candidate, and for trivial bugs a PII-free fix spec. Results appear in the
  backoffice; needs-human cases are emailed. Per-tenant opt-out, monthly budget
  cap. Phase 3 of the feedback system.
status: 'Proposed'
---

# Feedback AI triage

## TL;DR

A background job in the API sends each new report (redacted text, DOM outline, action log, context — **never** the raw copy) plus the route's source files to Scaleway Generative APIs in **one** structured-output call. It gets back a category (`bug:trivial`, `bug:complex`, `bug:unclear`, `feature-request`, `how-to`, `school-matter`, `spam`), an analysis for the owner, an optional reply draft for the reporter, a duplicate candidate, and — for `bug:trivial` only — a **fix spec** that task 47 dispatches to GitHub. No agent, no code execution, no repo checkout. Data boundary: [ai-data-boundary](../../docs/adr/ai-data-boundary.md).

## Context

Builds on [task 45](45-feedback-capture.md). Scaleway Generative APIs is the LLM provider for every AI feature ([ai-data-boundary](../../docs/adr/ai-data-boundary.md); switched from Alexandra Instituttet 2026-10-31 on price). No LLM client exists in the API yet. Whichever of this task and [task 25](../25-schema-import.md) lands first builds the shared one.

**Provider facts (checked 2026-10-31):**

- Endpoint `https://api.scaleway.ai/v1`, OpenAI-compatible, `Authorization: Bearer <IAM secret key>`. Region Paris (fr-par).
- Zero data retention by default: prompts and outputs aren't stored or used for training. Exception: a request that triggers a server error may be stored for up to two weeks to debug it ([data privacy](https://www.scaleway.com/en/docs/generative-apis/reference-content/data-privacy/)).
- Scaleway is already a sub-processor (email) with a DPA, so no new DPA to sign.
- Candidate models on Serverless ([supported models](https://www.scaleway.com/en/docs/generative-apis/reference-content/supported-models/)), all with structured output:

| Model | Input | Max output | Note |
|---|---|---|---|
| `glm-5.3` | Text | 16k | Strongest open model they host. No images |
| GLM 5.3 Flash | ? | ? | Not in Scaleway's model docs on 2026-10-31. Check the console; use it where it is listed |
| `deepseek-v4.1-flash` | Text + image | 32k | Cheap, and the only candidate that takes the screenshot |

- All three list English and Chinese only. Danish output (`summaryDa`, `replyDraftDa`) must be tested.
- Prices: [pricing page](https://www.scaleway.com/en/pricing/model-as-a-service/). Prefer the Flash models wherever the eval shows they're good enough.

Triage sends the **masked screenshot** together with the masked **DOM outline**, action log and text when the chosen model takes images. With a text-only model, triage runs on outline + log + text only, and the design is unaffected.

## Decisions (confirmed)

- **D1 — Provider**: Scaleway Generative APIs for triage (personal data stays in the EU). The model is config, chosen by the eval below. Claude is only used in task 47, on PII-free specs.
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

- `ILlmClient` over the `OpenAI` NuGet with custom base URL. Config: `Llm__BaseUrl` (`https://api.scaleway.ai/v1`), `Llm__ApiKey`, `Llm__Model` (default), `Llm__Models__<Feature>` (optional per-feature override, e.g. `Llm__Models__FeedbackTriage=deepseek-v4.1-flash`), `Llm__MonthlyBudgetEur`, `Llm__Prices__<model>__InputPerM` / `__OutputPerM` (EUR, from Scaleway's pricing page). Add to [DEPLOYMENT.md](../../docs/DEPLOYMENT.md) as optional (feature disabled when unset).
- No provider-specific code: switching model or provider is a config change.
- `LlmUsage` table: per call `Feature` (`feedback-triage`, later `schema-suggest`, `schema-import`), `Model`, tokens in/out, estimated cost, timestamp. Budget check sums the current month.
- Tasks 21 and 25 reuse this client — update their "LLM client" sections to point here when this lands.

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

- **Model eval before go-live**: run ~20 hand-written reports through GLM 5.3 Flash (if listed), `glm-5.3` and `deepseek-v4.1-flash`. Compare category accuracy, valid JSON on the first try, Danish quality of `summaryDa`/`replyDraftDa`, and cost per report. Does the screenshot improve accuracy enough to justify a vision model? Pick the cheapest model that is good enough and set `Llm__Models__FeedbackTriage`.
- Scaleway rate limits on Serverless ([rate limits](https://www.scaleway.com/en/docs/generative-apis/reference-content/rate-limits/)) are per organization. The one-at-a-time job stays well under them; the budget cap protects against loops either way.
- Send the 30-day sub-processor notice (Scaleway's purpose grows to AI processing) before go-live ([task 48](../../tasks/completed/48-databehandleraftale.md)).

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

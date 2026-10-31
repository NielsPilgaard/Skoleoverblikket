# Task 21 — AI Suggestions for Class Schemas

Generate a suggested weekly schema for a class using an LLM. User reviews the suggestion, tweaks it, then accepts. No auto-apply.

**LLM provider**: [Scaleway Generative APIs](https://www.scaleway.com/en/generative-apis/) — hosted in Paris, zero data retention by default ([ai-data-boundary](../docs/adr/ai-data-boundary.md)). OpenAI-compatible REST API, consumed via the shared LLM client from [task 46](feedback/46-feedback-ai-triage.md) / [25](25-schema-import.md). No custom SDK. Model: GLM 5.3 Flash where Scaleway lists it, otherwise `glm-5.3`; `deepseek-v4.1-flash` is the alternative. Pick after testing (open question 1).

### Disclosure requirements

User must see a clear disclaimer **before** the first request is sent and **inline** on the suggestion result. Non-negotiable — AI is involved and mistakes will happen.

**Before sending** (modal or inline warning on button click):
> Forslaget genereres af en AI-model hos [Scaleway](https://www.scaleway.com) i Frankrig (EU). Skoledata (klassenavn, fag, lærere og lektionstider) sendes til en server i EU og gemmes ikke. AI kan lave fejl — gennemgå altid forslaget før du anvender det.

**On the suggestion result** (persistent banner above ghost slots):
> Dette er et AI-genereret forslag. Kontrollér det grundigt — AI kan placere forkerte lærere, forkerte fag eller overtræde UVM-timetallet.

**Implementation**:
- "Forslag fra AI" button opens confirm dialog first (one-time per session, dismissable with "Vis ikke igen")
- Suggestion result panel has persistent amber banner with above text — cannot be dismissed
- Conflicted slots (from retry loop) additionally flagged with red warning per slot

---

## User flow

1. User opens schema builder for a class
2. Clicks "Forslag fra AI" button
3. API fetches tenant data, builds prompt, calls the LLM
4. Response renders as ghost overlay on schema grid — suggested slots shown in muted style
5. User accepts/rejects individual slots or clicks "Anvend alle"
6. Accepted slots upsert via existing `PUT .../slots` endpoint

---

## Backend

### New endpoint

```
POST /api/v1/classes/{classId}/schemas/{schemaId}/suggest
→ 200 SchemaSuggestionResponse
```

No request body needed — all context fetched from DB scoped to tenant.

### Response shape

```json
{
  "slots": [
    {
      "timeSlotId": "guid",
      "weekday": 1,
      "courseId": "guid",
      "teacherId": "guid",
      "roomId": "guid|null",
      "aideId": "guid|null",
      "conflicts": []
    }
  ],
  "warnings": ["string"]
}
```

`conflicts` per slot: run `ConflictDetectionService` on the suggestion before returning. Slots with conflicts are still returned — user decides.

### New service: `SchemaAiSuggestionService`

Steps:
1. Load context from DB (all tenant-scoped via `ITenantContext`):
   - Class (name, grade/`GradeLevel`)
   - All active Staff for tenant
   - All Courses for tenant (with `SubjectCategory`)
   - All TimeSlots for this schema (resolved cascade: schema → class → school defaults)
   - Active schemas for other classes (to detect existing teacher commitments at same times)
2. Load UVM timetal for class grade via `UvmTimetableService`
3. Build structured prompt (see below)
4. Call the LLM
5. Deserialize + **validate all IDs** — reject any ID not present in loaded context AND not owned by the current tenant. Since all context is loaded per-tenant via `ITenantContext`, both presence and tenant ownership are verified together — an ID absent from the loaded context set is implicitly cross-tenant or fabricated.
6. Run conflict detection on returned slots. If conflicts exist, retry with conflicts appended to prompt:
   ```
   Disse konflikter opstod — ret dem:
   {conflicts as JSON array}
   ```
   Up to 3 attempts total. Return best result if conflicts persist after 3 tries (flagged in response).
7. Return `SchemaSuggestionResponse`

### Prompt design

Use **structured output** (`ChatResponseFormat.CreateJsonSchemaFormat`) — no "returnér kun JSON" instruction needed. LLM is forced to emit valid JSON matching the schema.

**System prompt** (Danish, constraints only):
```
Du er ekspert i danske skoleskemaer. Du kender UVM's vejledende timetal.
Regler: brug kun id'er fra konteksten. Én lærer må ikke have to lektioner på samme tidspunkt. Tilstræb UVM-timetallet. Foretræk samme lærer til samme fag hele ugen.
```

**User prompt** — serialized context block:

```
Klasse: {name}, {gradeLevel}. klassetrin

Fag (id, navn, kategori):
{courses as JSON array}

Lærere (id, navn):
{staff as JSON array}

Ledige lektioner (id, ugedag, start, slut):
{timeSlots as JSON array}

UVM vejledende timetal for {gradeLevel}. klasse:
{uvmTimetal as JSON object}

Allerede optaget (lærer-id → [lektion-id liste]):
{busyTeachers as JSON object}
```

**Structured output** via `Microsoft.Extensions.AI` `GetResponseAsync<T>` — schema auto-inferred from C# type:

```csharp
internal sealed record SlotSuggestion(
    string TimeSlotId,
    int Weekday,
    string CourseId,
    string TeacherId,
    string? RoomId,
    string? AideId);

internal sealed record SchemaSuggestionResult(List<SlotSuggestion> Slots);
```

No hand-written JSON schema. `GetResponseAsync<SchemaSuggestionResult>` derives schema from the records via `System.Text.Json` reflection and sets `ResponseFormat` automatically.

> **Note**: `useJsonSchemaResponseFormat` defaults to `true`. Scaleway supports structured output for all candidate models. If a model still rejects the strict schema, pass `useJsonSchemaResponseFormat: false` to fall back to `json_object` mode — ID validation server-side catches malformed output either way.

### LLM client

NuGets:
- `OpenAI` — connects to Scaleway (`https://api.scaleway.ai/v1`) via custom base URL
- `Microsoft.Extensions.AI` — `IChatClient` abstraction + `GetResponseAsync<T>` structured output
- `Microsoft.Extensions.AI.OpenAI` — `.AsIChatClient()` adapter

Register `IChatClient` with per-tenant rate-limit pipeline:

> **Per-tenant keying**: `UseRateLimiting` accepts a `RateLimiter` factory. Use `PartitionedRateLimiter.Create` with `TenantId` as the partition key so each tenant has an independent quota. The `TenantId` is extracted inside `SchemaAiSuggestionService` from `ITenantContext` and passed as a key when acquiring a lease — not at `AddChatClient` registration time, since DI scope is per-request there.
>
> Concretely: `SchemaAiSuggestionService` holds an `IPartitionedRateLimiter<Guid>` (injected as singleton), acquires a lease keyed by `tenant.TenantId`, and throws `RateLimitRejectedException` (or returns 429) if the lease is denied.

```csharp
// Register partitioned rate limiter as singleton
builder.Services.AddSingleton<IPartitionedRateLimiter<Guid>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return PartitionedRateLimiter.Create<Guid, Guid>(tenantId =>
        RateLimitPartition.GetSlidingWindowLimiter(tenantId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(opts.RateLimitSeconds),
            SegmentsPerWindow = 1,
            QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
            QueueLimit = 0
        }));
});

builder.Services.AddChatClient(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    var openAIClient = new OpenAIClient(
        new ApiKeyCredential(opts.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl) });

    return openAIClient
        .GetChatClient(opts.Model)
        .AsIChatClient();
});
```

- `UseRateLimiting` is built into `Microsoft.Extensions.AI` pipeline — no separate `IMemoryCache` needed
- `QueueLimit = 0` + `QueueProcessingOrder.NewestFirst` → reject immediately on breach (throws `RateLimitRejectedException`)
- Catch `RateLimitRejectedException` in service → return 429 ProblemDetails

Call via `GetResponseAsync<T>` — schema auto-inferred from C# type, no hand-written JSON schema:

```csharp
var result = await _chatClient.GetResponseAsync<SchemaSuggestionResult>(messages);
// result.Result is a typed SchemaSuggestionResult
```

Options class:

```csharp
public sealed class LlmOptions
{
    public const string SectionName = "Llm";
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
}
```

Registration:

```csharp
builder.Services
    .AddOptions<LlmOptions>()
    .BindConfiguration(LlmOptions.SectionName) // "Llm"
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return new OpenAIClient(
        new ApiKeyCredential(opts.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl) });
});
```

`appsettings.json` (values in user secrets / env). Same `Llm` section as [task 46](feedback/46-feedback-ai-triage.md); this feature's model goes in `Llm__Models__SchemaSuggest`:
```json
"Llm": {
  "BaseUrl": "https://api.scaleway.ai/v1",
  "ApiKey": "",
  "Model": ""
}
```

Auth is `Authorization: Bearer <Scaleway IAM secret key>`, which the `OpenAI` client sends by default.

---

## Frontend

### Schema builder changes

- Add "Forslag fra AI" button in schema toolbar (superadmin + admin only)
- On click: `POST .../suggest` → loading state on button
- Response renders as **ghost slots** (50% opacity, dashed border, AI badge)
- Each ghost slot has Accept (✓) / Reject (✗) buttons
- "Anvend alle" button bulk-accepts non-conflicting slots
- Conflicted ghost slots show warning icon — user must explicitly accept
- On accept: call existing `PUT .../slots` upsert, slot becomes real

### State management

Add `suggestionMode: boolean` + `suggestedSlots: SlotSuggestion[]` to schema store/context. Clear on navigate away or explicit dismiss.

---

## Per-tenant rate limiting

We pay the API key — protect cost. One suggestion request per tenant per ~1 minute.

**Implementation**: `SlidingWindowRateLimiter` in the `IChatClient` middleware pipeline via `UseRateLimiting` (built into `Microsoft.Extensions.AI`). Keyed per `TenantId` so tenants don't share quota.

- `QueueLimit = 0` → reject immediately, no queuing
- `RateLimitRejectedException` caught in service → 429 `ProblemDetails`
- Frontend shows toast: "Vent et øjeblik før du genererer et nyt forslag."
- `RateLimitSeconds` in `LlmOptions` (default `60`) — tunable without redeploy

```csharp
public int RateLimitSeconds { get; init; } = 60;
```

```json
"Llm": {
  "BaseUrl": "https://api.scaleway.ai/v1",
  "ApiKey": "",
  "Model": "",
  "RateLimitSeconds": 60
}
```

---

## Constraints & validation

| Rule | Where enforced |
|---|---|
| All IDs from prompt only | Server-side after LLM response |
| No double-booked teacher | `ConflictDetectionService` on suggestion |
| One slot per cell (weekday+timeSlot) | Server deduplicates; last wins |
| Tenant scoping | `ITenantContext` on all DB reads |
| Per-tenant rate limit (~1 min) | `SlidingWindowRateLimiter` in `IChatClient` pipeline, 429 on breach |

---

## Out of scope

- Auto-applying suggestions without user review
- Multi-class suggestions in one call
- Room assignment (optional field — LLM may leave null)
- Saving suggestion as draft — it's ephemeral until user accepts

---

## Onboarding integration

This feature must be surfaced during onboarding, not just discovered later inside the schema builder. A new school with classes/staff/rooms already set up (whether entered manually or via the [import wizard](25-schema-import.md) or [STIL import](23-stil-userdata-import.md)) has no schema yet — "Forslag fra AI" is the fastest path from empty schema to a usable first draft.

- Once a class has courses/staff/rooms but no schema slots, the dashboard onboarding card and/or the class's empty schema grid should point at "Forslag fra AI" as the suggested next step — not just leave an empty grid.
- Coordinate with task 25: if the school uploaded an existing schedule file (task 25's import flow), don't also prompt for an AI *suggestion* — that's a suggestion for schools starting from scratch, task 25 is for schools digitizing an existing schedule. Surface exactly one, not both, based on whether an old schedule file exists.
- Revisit the setup wizard (`SchoolSetupWizardPage.tsx`) once this ships: consider adding a step after class/staff/room creation that offers "Forslag fra AI" per class before sending the user to the dashboard.

---

## Open questions

1. Model: run ~10 real classes through GLM 5.3 Flash (if listed), `glm-5.3` and `deepseek-v4.1-flash`. Compare valid IDs, conflicts after the retry loop, UVM fit and cost per suggestion. Pick the cheapest that is good enough. `glm-5.3` caps output at 16k tokens on Serverless, plenty for one class.
2. ~~Does class entity have `GradeLevel`?~~ Confirmed: `Class.GradeLevel` is `int?` (0=børnehaveklasse, 1–10). Handle `null` as follows:
   - Omit UVM timetal block from the prompt entirely
   - Add `"UVM timetal ikke tilgængeligt — klassen mangler klassetrin"` to `SchemaSuggestionResponse.warnings`
   - Show warning inline above ghost slots in the UI (same amber banner, different text)
   - Do **not** reject the request — proceed with reduced guidance; suggestions are still useful for teacher/room assignment even without UVM hours
3. Token limits: 20 staff + 15 courses + 30 slots ≈ small — no chunking needed
4. Scaleway enforces per-organization rate limits on Serverless: add `Polly` retry on 429 from upstream (separate from our per-tenant gate)

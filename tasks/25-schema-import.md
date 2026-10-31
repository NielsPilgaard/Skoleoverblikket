# Task 25: Schema Import via AI

## Context

Schools switching to Skoleoverblikket have existing schedules in other tools — exported as Excel, CSV, or PDF. Manually re-entering every slot is a barrier to adoption. This feature lets admins upload their current schedule file and get slot suggestions back — same ghost-slot UX as task 21 (accept individually or all at once).

**No FastAPI sidecar.** Pure .NET: extract text from file, send to the EU LLM (Scaleway Generative APIs, [ai-data-boundary](../docs/adr/ai-data-boundary.md)), parse structured response. Same AI setup as task 21.

**Model**: start with GLM 5.3 Flash where Scaleway lists it, otherwise `glm-5.3`; test `deepseek-v4.1-flash` against it on 3–5 real schedule exports (CSV, Excel, PDF) and keep the cheapest that resolves names correctly. Set it in `Llm__Models__SchemaImport`. `glm-5.3` caps output at 16k tokens on Serverless (DeepSeek: 32k), enough for one class's week.

---

## Supported input formats

| Format | Extraction library | Notes |
|--------|--------------------|-------|
| CSV | Built-in (`StreamReader`) | Raw text → LLM |
| Excel (.xlsx / .xls) | **ClosedXML** (already in csproj) | Read cells as text table → LLM |
| PDF | **PdfPig** (add to csproj, Apache 2.0) | Extract text per page → LLM |

Images NOT supported in v1. `deepseek-v4.1-flash` on Scaleway does take images, so photos or scanned PDFs are possible later if schools ask.

---

## .NET Changes

### New NuGet

```xml
<PackageReference Include="UglyToad.PdfPig" Version="..." />
```

Only addition needed. ClosedXML already present.

### New files

- `Services/SchemaImportService.cs` — text extraction per format + LLM call + fuzzy-match
- `Services/SchemaImportTextExtractor.cs` — per-format extraction logic (CSV, Excel, PDF)

### Modified files

- `Controllers/SchemasController.cs` — add `POST .../import` endpoint
- `Services/ServicesExtensions.cs` — register `SchemaImportService`
- `appsettings.json` — shared `Llm` section ([task 46](feedback/46-feedback-ai-triage.md) scope 1); built here if this task lands first
- `Skoleoverblikket.Api.csproj` — add PdfPig

### New endpoint

```
POST /api/v1/classes/{classId}/schemas/{schemaId}/import
Content-Type: multipart/form-data
Body: file (csv/xlsx/xls/pdf, max 10 MB)
```

Response:
```csharp
record SchemaImportSuggestionResponse(
    IReadOnlyList<ImportedSlotSuggestion> Slots,
    IReadOnlyList<string> UnresolvedCourseNames,
    IReadOnlyList<string> UnresolvedTeacherNames,
    IReadOnlyList<string> UnresolvedRoomNames
);

record ImportedSlotSuggestion(
    Guid? TimeSlotId,       // null = no matching timeslot found
    DayOfWeek Weekday,
    Guid? CourseId,
    string CourseName,      // raw name from import (for unresolved display)
    Guid? TeacherId,
    string TeacherName,
    Guid? RoomId,
    string? RoomName
);
```

---

## Service flow (`SchemaImportService`)

1. Extract text from uploaded file (`SchemaImportTextExtractor`)
2. Load tenant context: courses, staff, rooms, timeslots for this schema
3. Build LLM prompt:
   - Extracted text
   - Known entity names as hints
   - Instruction to output structured JSON (`ImportedSlotSuggestion[]` shape)
4. Call the LLM via `IChatClient` (same setup as task 21, `GetResponseAsync<T>`)
5. Fuzzy-match LLM-returned names to entity IDs
6. Return `SchemaImportSuggestionResponse`

### Text extraction per format

```csharp
// CSV
using var reader = new StreamReader(stream);
return await reader.ReadToEndAsync();

// Excel (ClosedXML — already referenced)
using var workbook = new XLWorkbook(stream);
// iterate sheets → rows → cells → build text table

// PDF (PdfPig)
using var doc = PdfDocument.Open(stream);
// concat text from all pages
```

### Fuzzy matching

Normalize: lowercase, trim, strip punctuation. Then:
1. Exact match
2. Contains match  
3. Levenshtein ≤ 2 (implement inline, ~15 lines, no new NuGet)

---

## LLM prompt strategy

Send extracted text + context:

```
Du er en assistent der udtrækker skemainformation fra tekst.

Eksisterende fag: [liste]
Eksisterende lærere: [liste]  
Eksisterende lokaler: [liste]
Eksisterende tidspunkter: [liste med labels]

Udtræk alle skemaposter fra denne tekst og returner JSON:
[{ weekday: 0-6, timeslot_label: "...", course_name: "...", teacher_name: "...", room_name: "..." }]

Brug navne fra de eksisterende lister hvis muligt. Tekst:
[extracted text]
```

Use `Microsoft.Extensions.AI` `GetResponseAsync<ImportedSlotDto[]>` for typed output (same as task 21 pattern).

---

## Frontend

**New**: `SchemaImportModal.tsx`  
**Modified**: `SchemaBuilderPage.tsx` — add "Importer skema" button (admin only)

Flow:
1. "Importer skema" button opens modal
2. File picker — accepts `.csv,.xlsx,.xls,.pdf`
3. Upload via `POST /import` (multipart, no presign needed — file goes directly to API, not S3)
4. Loading state ~5–15 s: spinner + "Læser skema..."
5. Ghost slots rendered same as task 21 suggestions
6. Amber warning banner listing unresolved names
7. Accept individually or "Anvend alle" → calls existing `PUT .../slots`

**No S3 presign needed** — file goes directly to the API endpoint, processed in memory, discarded. No persistence required.

Use generated typed API client after spec updates.

---

## Task 21 dependency

Task 21 and task 25 share:
- Same LLM `IChatClient` config
- Same ghost-slot frontend component (task 21 defines it, task 25 reuses)

If task 21 not yet implemented when this is built, we need to add the AI client setup as part of this task.

---

## Aspire / infrastructure

No changes — no new services, no new containers.

---

## Verification

1. Upload a simple CSV with schedule data → endpoint returns correct slots
2. Upload an Excel file → same
3. Upload a PDF with text → same
4. Unresolved names appear in response + displayed in frontend banner
5. Accept all → slots appear in schema grid
6. `/verify` passes
7. `/test` Playwright: upload → review ghost slots → accept all

---

## Onboarding integration

This is a distinct import branch from the entity import wizard (`ImportPage.tsx` — students/parents, staff, rooms, board members via paste-grid) and from STIL import (task 23 — students/staff/classes/parents via SkoleGrunddata). Those three cover *who/where* (entities); this task covers *when* (the actual weekly schema/slots) — a school can complete every onboarding step (classes, rooms, staff) and still face a blank schema grid per class.

- Onboarding must point schools with an existing paper/Excel/PDF schedule at **this** import, not at the entity import wizard — they solve different problems and neither substitutes for the other. Copy in the setup wizard fork (see `SchoolSetupWizardPage.tsx`) must not conflate "import your data" (entities) with "import your existing schedule" (this task) — offer both as separate, clearly-labeled choices once this ships.
- After entity onboarding completes (classes/rooms/staff all created, via any of the three entity paths), the dashboard/onboarding flow should ask, per class: "Har du et eksisterende skema for denne klasse?" → this import. If no → task 21's AI suggestion. If neither → empty grid, manual entry.
- Until this task ships, the setup wizard's import fork only covers entities — do not reference schema import from the wizard yet.

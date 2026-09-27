# Glossary: Danish domain terms vs. code identifiers

The product is Danish-language only — UI text, user-facing web page URLs, and user-visible labels stay Danish. Backend API route paths (`/api/v1/...`) are English. Code identifiers (C# classes, DTOs, TypeScript components, file names) are English, except for a fixed list of domain words kept as-is because they don't translate cleanly or are the team's shared vocabulary.

## Kept Danish domain vocabulary

These words appear in code identifiers deliberately and must **not** be renamed:

| Danish | English meaning | Where it shows up in code |
|---|---|---|
| klasse | class | `Class`, `ClassId`, `ClassesController` (already English root, "klasse" appears in route paths/comments) |
| lokale | room | `Room`, `RoomsController` |
| lektion | time slot / lesson | `TimeSlot`, `TimeSlotsController` |
| lærer | teacher | `Teacher`-flavored roles/fields |
| pædagog | aide | `Aide`-flavored roles/fields |
| skema | schema/schedule | `SchemasController`, `SchemaBuilderPage`, `PrintSchemaPage`, `ParentSchemaPage` |
| SFO | after-school care institution (no clean English equivalent) | `SfoController`, `SfoWeekPlanController`, `SfoPage` |

User-facing web page URLs and UI-visible text stay Danish everywhere; backend API route paths are English (this list is about **code identifiers** only).
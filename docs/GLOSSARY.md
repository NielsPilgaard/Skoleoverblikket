# Glossary: Danish domain terms vs. code identifiers

The product is Danish-language only — UI text, URL paths, and user-visible labels stay Danish. Code identifiers (C# classes, DTOs, TypeScript components, file names) are English, except for a fixed list of domain words kept as-is because they don't translate cleanly or are the team's shared vocabulary.

## Kept Danish domain vocabulary

These words appear in code identifiers deliberately and must **not** be renamed:

| Danish | English meaning | Where it shows up in code |
|---|---|---|
| klasse | class | `Class`, `ClassId`, `ClassesController` (already English root, "klasse" appears in route paths/comments) |
| fag | course/subject | `Course`, `FagSwapCourseId` |
| lokale | room | `Room`, `RoomsController` |
| lektion | time slot / lesson | `TimeSlot`, `TimeSlotsController` |
| lærer | teacher | `Teacher`-flavored roles/fields |
| pædagog | aide | `Aide`-flavored roles/fields |
| vikar | substitute | `VikarController`, `web/src/components/vikar/` |
| skema | schema/schedule | `SchemasController`, `SchemaBuilderPage`, `PrintSchemaPage`, `ParentSchemaPage` |
| SFO | after-school care institution (no clean English equivalent) | `SfoController`, `SfoWeekPlanController`, `SfoPage` |

Route path strings and UI-visible text stay Danish everywhere (this list is about **code identifiers** only).

## Renamed: incidental Danish names → English

These were Danish by accident, not by domain necessity, and have been renamed in code. Where a rename touched an EF-mapped identifier, the DB table/column was renamed too via a dedicated migration (see `RenameDanishColumnsToEnglish`) rather than left mismatched behind a `[Column]`/`[Table]` attribute.

| Old (Danish) | New (English) | Area |
|---|---|---|
| `KontaktController` | `ContactDirectoryController` | Parent/staff contact directory |
| `KontaktParentDto` / `KontaktStudentDto` | `ContactDirectoryParentDto` / `ContactDirectoryStudentDto` | Contact directory DTOs |
| `StaaMaalMedController` | `ComplianceCoverageController` | Stå-mål-med compliance publishing |
| `UpdateSfoGenereltRequest` / `GenereltDto` (SfoWeekPlanController) | `UpdateSfoNotesRequest` / `NotesDto` | SFO week plan notes DTO |
| `UpdateGenereltRequest` / `GenereltDto` (WeekPlanController) | `UpdateNotesRequest` / `NotesDto` | Class week plan notes DTO |
| route `api/v1/kontakt` | `api/v1/contact-directory` | Backend route |
| route `api/v1/staa-maal-med` | `api/v1/compliance-coverage` | Backend route |
| route `.../ugeplan` (WeekPlanController, SfoWeekPlanController) | `.../week-plan` | Backend route |
| `TildeleVikarPanel` | `AssignSubstitutePanel` | Vikar assignment UI panel |
| `KontaktPage.tsx` | `ContactPage.tsx` | Public marketing contact page |
| `KontaktbogPage.tsx` / `ParentKontaktbogPage.tsx` | `ContactBookPage.tsx` / `ParentContactBookPage.tsx` | Kontaktbog (parent↔teacher threads) |
| `BeskederPage.tsx` | `MessagesPage.tsx` | Flat inbox |
| `BestyrelseDashboardPage.tsx` | `BoardDashboardPage.tsx` | Board module |
| `BestyrelseFilerPage.tsx` | `BoardFilesPage.tsx` | Board module |
| `BestyrelseMedarbejderePage.tsx` | `BoardStaffPage.tsx` | Board module |
| `BestyrelseSkemaerPage.tsx` | `BoardSchemasPage.tsx` | Board module |
| `BestyrelsesmedlemmerCard` | `BoardMembersCard` | School settings page |
| `StaaMaalMedPage.tsx` | `ComplianceCoveragePage.tsx` | Stå-mål-med compliance publishing |
| `FerieindmeldingPage.tsx` / `FerieindmeldingDetailPage.tsx` / `ParentFerieindmeldingPage.tsx` | `VacationRegistrationPage.tsx` / `VacationRegistrationDetailPage.tsx` / `ParentVacationRegistrationPage.tsx` | Vacation registration |
| `UgeplanPrintPage.tsx` / `ParentUgeplanPage.tsx` | `WeekPlanPrintPage.tsx` / `ParentWeekPlanPage.tsx` | Weekplan |
| `FravaerPage.tsx` / `ParentFravaerPage.tsx` | `AbsencePage.tsx` / `ParentAbsencePage.tsx` | Absence reporting |
| `AarsrulPage.tsx` | `ClassRolloverPage.tsx` | Class year rollover |
| `SkoleindstillingerPage.tsx` | `SchoolSettingsPage.tsx` | School settings |
| `OmPage.tsx` | `AboutPage.tsx` | Public marketing site |
| `PrivatlivspolitikPage.tsx` | `PrivacyPolicyPage.tsx` | Public marketing site |
| `KlassechatPage.tsx` | `ClassChatPage.tsx` | Per-class parent-teacher chat |
| `StaaMaalMedSnapshot` (entity + table) | `ComplianceCoverageSnapshot` (table `ComplianceCoverageSnapshots`) | Renamed via migration `RenameDanishColumnsToEnglish` |
| `WeekPlan.Generelt` / `SfoWeekPlan.Generelt` (column `Generelt`) | `.Notes` (column `Notes`) | Renamed via migration `RenameDanishColumnsToEnglish` |
| `WeekPlanSlot.Beskrivelse` / `SfoWeekPlanShift.Beskrivelse` (column `Beskrivelse`) | `.Description` (column `Description`) | Renamed via migration `RenameDanishColumnsToEnglish` |
| API field `generelt` (WeekPlanDto/SfoWeekPlanDto) | `notes` | DTO + frontend + generated client updated |
| API field `beskrivelse` (WeekPlanSlotDto/SfoWeekPlanShiftDto) | `description` | DTO + frontend + generated client updated |
| route `PUT .../week-plan/generelt` | `PUT .../week-plan/notes` | Backend route (WeekPlanController, SfoWeekPlanController) |
| `GenereltEditor` / `SfoGenereltEditor` (React components) | `NotesEditor` / `SfoNotesEditor` | Weekplan/SFO editor components |
| CSS classes `print-generelt` / `print-beskrivelse` | `print-notes` / `print-description` | Print stylesheets in SfoPrintPage.tsx, WeekPlanPrintPage.tsx |

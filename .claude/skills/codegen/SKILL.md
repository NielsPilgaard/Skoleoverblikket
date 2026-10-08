---
name: codegen
description: Regenerate OpenAPI spec then frontend API client for Skoleoverblikket. USE when user says 'codegen', 'swagger gen', 'regenerate api', 'run codegen', 'generate api client', 'regenerate spec', 'regenerate openapi', or after controller/model/endpoint changes.
---

Run one command:

```bash
set -o pipefail; cd web && npm run codegen --silent 2>&1 | cut -c1-300 | tail -30
```

It builds the API quietly into `bin/codegen/` (works while Aspire is running,
no locked-DLL errors), which regenerates `openapi/Skoleoverblikket.Api.json`,
then runs openapi-ts with `--silent`. Success prints about five lines
ending in `0 Error(s)`. On failure, report the error lines verbatim and fix the
C# compile error they name.

Done means exit code 0. Then move on. To prove the client still fits the
pages that use it, run `npx tsc -b` in `web/` (or `/verify`).

## Never read, grep or diff the generated output

These files are generated and huge. Reading them only costs tokens:

| File | Size |
|---|---|
| `openapi/Skoleoverblikket.Api.json` | ~400 KB |
| `web/src/api/generated/*.gen.ts` | barrel lines of 12,000–15,000 chars |

One `grep` match on a barrel line dumps ~7k tokens. A `Read` dumps far more.
The repo-root `.ignore` already hides both paths from repo-wide Grep/Glob.
Don't get around it by passing the paths explicitly.

- Don't `Read` them, `cat` them or open them to "check the output".
- Don't run `git diff` on them. Use `git diff --stat` or exclude them:
  `git diff -- . ':!openapi' ':!web/src/api/generated'`.
- To find a hook or type name, grep the **usages** in `web/src/pages` and
  `web/src/components`. Names follow `{method}ApiV1{Path}`, for example
  `getApiV1StaffAbsencesOptions` and `postApiV1StaffAbsencesMutation`.

If you really must confirm a symbol exists, count it or truncate every line:

```bash
grep -c "MySymbol" web/src/api/generated/@tanstack/react-query.gen.ts
grep -n "MySymbol" web/src/api/generated/sdk.gen.ts | cut -c1-120 | head -5
```

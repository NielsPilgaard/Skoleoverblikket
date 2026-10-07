---
name: ci-failure
description: "Diagnose and fix a failed GitHub Actions run for Skoleoverblikket: CI (API, web, docs), PR e2e, Staging (e2e against published images) or the production deploy. USE THIS SKILL when the user says 'staging failed', 'CI failed', 'the build is red', 'deploy failed', 'e2e failed on the PR', 'fix the pipeline', or pastes a GitHub Actions run link."
---

# CI failure

Find the failing step, get the evidence, find the commit that broke it, then fix the cause.
Use `gh` for everything on GitHub. Don't guess from the workflow file; read the run.

## Pipeline map

| Workflow | Trigger | What fails there |
|---|---|---|
| `ci.yml` | PR + push to main | `API — build & test`, `Web — build & lint`, `Docs — ryni`, `E2E` (PRs only) |
| `e2e.yml` | called by CI (PR, builds images) and Staging (pulls `sha-<commit>` images) | Playwright against `infrastructure/docker/docker-compose.staging.yml` |
| `staging.yml` | CI succeeded on main | `E2E`, then `Deploy` |
| `cd.yml` | called by Staging | image check, `infrastructure/scripts/deploy.mjs` |

A Staging run named "skipped" means CI on that commit didn't succeed. Look at the CI run instead.

## Step 1: Find the failed run and step

```bash
gh run list --limit 15
gh run view <run-id> --log-failed | tail -120
```

For a run link the user pasted, the run id is the number after `/runs/`.

## Step 2: Get the evidence

**Playwright failure** (E2E job). The log shows the assertion, but download the artifacts too:

```bash
gh run download <run-id> -n playwright-report -D <scratch-dir>/pw   # traces + error-context.md
gh run download <run-id> -n stack-logs -D <scratch-dir>/stack        # api/keycloak/web logs, failure only
```

- `test-results/<test>/error-context.md` has the page's accessibility snapshot at the moment of failure. Read it first.
- `trace.zip` can be opened with `npx playwright show-trace <path>`.
- `stack-logs/api.log` has server-side exceptions. The job log also prints the last 200 API lines under "API log".

**Production deploy or runtime failure.** Prod API logs go to Grafana Cloud Loki (datasource `grafanacloud-logs`, label `service_name="Skoleoverblikket.Api"`). Query them with the Grafana MCP `query_loki_logs` tool around the deploy time. Staging logs are not in Grafana; the stack only lives on the runner.

## Step 3: Find what changed

Compare the failing commit with the last green run of the same workflow:

```bash
gh run list --workflow=staging.yml --limit 10
git diff --stat <last-green-sha> <failing-sha>
git log --oneline <last-green-sha>..<failing-sha>
```

A test added or changed in that range that fails on first run is usually a wrong test, not a regression. Read the code it exercises before changing either.

## Step 4: Reproduce locally

For e2e, run only the failing spec against the running Aspire stack:

```bash
cd web && npx playwright install chromium && SKIP_ASPIRE=1 npx playwright test tests/e2e/<spec>.ts --reporter=line
```

Drop `SKIP_ASPIRE=1` if Aspire isn't running; Playwright starts it. For API failures, run `/test` or the single test class with `dotnet test`.

## Step 5: Fix and report

- Fix the cause, then rerun the reproduction.
- Run `/verify` before committing code changes.
- Don't push to main or rerun workflows without asking. `gh run rerun <run-id> --failed` is fine to suggest for flakes.
- Report: failing job/step, root cause, the commit that introduced it, the fix, and how you verified it.

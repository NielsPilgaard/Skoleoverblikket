---
title: 'GitHub Actions major versions'
purpose: 'Take the eight major GitHub Actions bumps in Dependabot PR #80 without breaking the deploy, Staging, changelog or nightly workflows that PR CI never runs.'
description: >-
  Dependabot #80 bumps checkout, setup-node, setup-dotnet, github-script and
  four Docker actions by one or two major versions across six workflows. PR CI
  is green, but it only exercises ci.yml and e2e.yml on pull requests. The
  publish jobs, cd.yml, staging.yml, changelog.yml and nightly-improvement.yml
  first run after merge, so a breaking input change would surface as a failed
  deploy.
status: 'In review'
---

# GitHub Actions major versions

## TL;DR

- #80 updates: `actions/checkout` 5 → 7, `actions/setup-node` 5 → 7, `actions/setup-dotnet` 5 → 6, `actions/github-script` 8 → 9, `docker/login-action` 3 → 4, `docker/build-push-action` 6 → 7, `docker/setup-buildx-action` 3 → 4, `docker/bake-action` 6 → 7.
- Files: `cd.yml`, `changelog.yml`, `ci-fix.yml`, `ci.yml`, `e2e.yml`, `nightly-improvement.yml`.
- PR CI passes, but the image publish jobs, the deploy, Staging, the weekly changelog and the nightly job only run on `main` or on a schedule.
- Size S. Owner: Niels + agent (the agent reads changelogs and prepares, Niels merges when there's time to watch the deploy).

## Status (2026-11-01)

Agent steps 1–2 are done in the `chore/dependency-upgrades` PR (#80 was closed, so the bumps were reapplied there). No input or script we use is affected; the release-note review is in the PR description. Left: Niels's steps 3–4.

## Steps

1. **Agent:** for each action, read the release notes for the major version and check every `with:` input we pass, plus `github-script` code that uses the Octokit API (v9 may move to a newer Octokit). List breaking changes that affect us in a PR comment on #80. Fix the workflows on the #80 branch if needed.
2. **Agent:** confirm `docker/bake-action` 7 still accepts `source: .` and our `docker-bake.e2e.hcl`, and `build-push-action` 7 still accepts our cache settings (`type=gha`).
3. **Niels:** merge on a day you can watch the first `main` run through publish, deploy and Staging. Trigger `changelog.yml` and `nightly-improvement.yml` once by hand with `workflow_dispatch` (if they have it) instead of waiting for the schedule.
4. If deploy breaks: revert the merge commit. The deploy rollback ([44](44-auto-rollback.md)) covers the app, not the workflow.

## Done when

- One full `main` run (publish, deploy, Staging) and one run each of the changelog and nightly workflows have passed on the new versions.

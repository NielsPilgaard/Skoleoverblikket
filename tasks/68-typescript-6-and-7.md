---
title: 'TypeScript 6, then 7'
purpose: 'Move web/ off TypeScript 5.6 without breaking API client codegen, replacing Dependabot PR #78.'
description: >-
  Dependabot #78 jumps TypeScript 5.6.3 → 7.0.2 and fails CI: TypeScript 7 is
  the native (Go) compiler and no longer ships the JS compiler API that
  @hey-api/openapi-ts calls, so `api:generate` crashes. Step 1 is TypeScript
  6.0.x, which openapi-ts supports today. Step 2 is TypeScript 7 once
  openapi-ts lists it as a peer.
status: 'Ready'
---

# TypeScript 6, then 7

## TL;DR

- #78 (TS 5.6.3 → 7.0.2) fails "Web — build & lint" and e2e. Close it when step 1 lands; Dependabot reopens a TS 7 PR later.
- Cause: `npm run api:generate` dies with `TypeError: Cannot read properties of undefined (reading 'LineFeed')` at `ts.NewLineKind.LineFeed`. TS 7's npm package has no JS compiler API.
- `@hey-api/openapi-ts` 0.99.0 has peer `typescript: ">=5.5.3 || >=6.0.0 || 6.0.1-rc"`, so TS 6 is supported and TS 7 isn't.
- Step 1 (S, Agent): `typescript ~6.0.3`, fix new type errors, `/verify` + `/test`.
- Step 2 (S, Agent): TS 7 once an openapi-ts release supports it. Until then, keep the `~6.0` pin.

## Step 1: TypeScript 6.0

1. `npm install -D typescript@~6.0.3` in `web/`. Keep the tilde: a caret would let Dependabot's minor/patch group pull in 7.x later.
2. `npm run build`. TS 6 deprecates some older options (check `tsconfig*.json` for `moduleResolution: node`, `target` below ES2015, `baseUrl` without `paths`). Fix what it reports instead of silencing it with `ignoreDeprecations`.
3. `/codegen` must produce no diff in `web/src/api/generated/` (compare with `git status`, don't read the files).
4. `/verify` and `/test`.
5. Close #78 with a link to the PR.

## Step 2: TypeScript 7

Blocked on openapi-ts. Check with `npm view @hey-api/openapi-ts@latest peerDependencies`. When it lists 7.x, bump both, run `/codegen`, `/verify` and `/test`. Biome doesn't use the `typescript` package, so it isn't affected.

## Done when

- `web/package.json` has `typescript ~6.0.x` (step 1) or `^7` (step 2), CI is green and #78 is closed.

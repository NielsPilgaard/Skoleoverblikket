---
title: 'NuGet updates: minor group, OpenApi 3, Refit 16'
purpose: 'Land the held-back NuGet updates (Dependabot PRs #82, #83, #84) in risk order, and keep Stripe.net on 47.x.'
description: >-
  Three Dependabot PRs: #82 is 32 minor and patch updates (EF Core, Aspire,
  OpenTelemetry, TUnit, Testcontainers) with green CI; #83 moves our
  Microsoft.OpenApi security pin to 3.x, which Swashbuckle isn't built
  against; #84 is Refit 16, which fails 47 API tests because it needs the
  reflection package or source generation. Stripe.net majors are not taken
  (#85 closed): billing may move off Stripe (task 62).
status: 'Done'
---

# NuGet updates: minor group, OpenApi 3, Refit 16

## TL;DR

- **Part 1, #82** (S, Agent): 32 minor/patch updates across the API, tests, Aspire and backup agent. CI is green. Notable: EF Core and ASP.NET 10.0.7 → 10.0.14, Npgsql 10.0.1 → 10.0.3, Aspire 13.4 → 13.6, OpenTelemetry 1.15 → 1.20, TUnit 1.43 → 1.78, Swashbuckle 10.1.7 → 10.3.1, Refit 11.0.1 → 11.2.0, Microsoft.OpenApi 2.7.5 → 2.13.0, Stripe.net 47.3 → 47.4 (same major, fine).
- **Part 2, #83** (S, Agent): Microsoft.OpenApi 2.x → 3.11.0. Don't take it until Swashbuckle depends on OpenApi 3.
- **Part 3, #84** (S–M, Agent): Refit 11 → 16. Fails with `NotSupportedException: This interface needs the reflection request builder, which is not installed. Add a reference to the Refit.Reflection NuGet package...`.
- **Not doing:** Stripe.net majors. #85 (47 → 53) is closed and `.github/dependabot.yml` ignores Stripe.net major updates until [62](../62-stripe-to-alunta.md) decides whether billing stays on Stripe.

## Outcome (2026-11-01)

- Part 1: all 32 updates taken. Spec unchanged.
- Part 2: the pin stays, at 2.13.0. Swashbuckle 10.3.1 depends on 2.7.5, but the build-time spec generator in Microsoft.Extensions.ApiDescription.Server 10.0.14 is built against Microsoft.OpenApi 2.12.2 and fails to load 2.7.5. OpenApi 3 waits for a Swashbuckle release on 3.x.
- Part 3: Refit 16.4.0. Every Keycloak method generates inline (no RF006), so `KeycloakAdminExtensions` registers the clients with `AddRefitGeneratedClient` and no `Refit.Reflection` package is needed.
- Keycloak outages: Refit 16 made a failing call wait up to 30 s, because a timed-out attempt is now retried instead of ending the call. The Keycloak clients now give up after 10 s, as they did on Refit 11. Connection failures also became `KeycloakException`, so signup shows its Danish message (502) instead of a generic 500, and other uncaught calls get a 503. Tests run without Keycloak, so `ApiFactory` turns retries off to keep them fast.

## Part 1: minor/patch group (#82)

1. Rebase #82 on `main` (comment `@dependabot rebase`).
2. Run `/verify` and `/test` locally on the branch, then `/codegen`. The OpenAPI spec and generated client must not change, apart from version strings. If they change, the Swashbuckle or OpenApi bump changed the output: say what changed in the PR.
3. Run Aspire once (`aspire run`) and check the dashboard shows traces and logs. Aspire and OpenTelemetry both moved.
4. Merge.

## Part 2: Microsoft.OpenApi 3 (#83)

[Skoleoverblikket.Api.csproj](../../api/Skoleoverblikket.Api/Skoleoverblikket.Api.csproj) pins Microsoft.OpenApi above Swashbuckle's own dependency for GHSA-v5pm-xwqc-g5wc. A 3.x pin under a Swashbuckle built for 2.x can break spec generation at runtime even when the build passes.

1. After part 1: `dotnet list package --include-transitive` in the API project to see which Microsoft.OpenApi Swashbuckle 10.3.1 pulls in.
2. If that is ≥ 2.7.5, remove the pin (the comment says to). Then close #83.
3. Take Microsoft.OpenApi 3 only with a Swashbuckle release that depends on it. Check with `/codegen` that the spec is unchanged.

## Part 3: Refit 16 (#84)

Refit is used for the Keycloak admin and token clients in `api/Skoleoverblikket.Api/Auth/` (`IKeycloakAdminApi`, `IKeycloakTokenApi`, wired in `KeycloakAdminExtensions`).

1. Read the Refit 12–16 release notes. Prefer the source generator if Refit 16 ships one, so no reflection package is needed. Otherwise add `Refit.Reflection`.
2. Check request and response serialization for the Keycloak calls. Refit majors have changed default serializer settings before.
3. `/verify` (47 integration tests failed on #84) and `/test` (staff invitation and signup go through Keycloak admin).
4. Close #84 if a replacement PR was used.

## Done when

- #82, #83 and #84 are merged or closed with a reason, CI is green, and the spec and generated client are unchanged unless the PR explains why.

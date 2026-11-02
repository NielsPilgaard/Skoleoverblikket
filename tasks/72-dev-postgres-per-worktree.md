---
title: 'One dev Postgres per worktree'
purpose: 'Stop parallel Aspire stacks from running two Postgres servers on one data directory.'
description: >-
  The AppHost mounts fixed volume names (skoleoverblikket-pgdata-a and friends),
  so an Aspire stack from any worktree uses the same dev data. Two stacks at once
  means two Postgres servers on one data directory, which can corrupt it, and a
  Keycloak upgrade in one branch migrates the Keycloak database of all of them.
  Seen on 2026-11-02 with parallel agent sessions. Dev only: prod runs one stack.
status: 'Ready'
---

# One dev Postgres per worktree

## TL;DR

- [AppHost Program.cs](../infrastructure/aspire/Skoleoverblikket.AppHost/Program.cs) mounts fixed volume names. Each worktree gets its own containers, but they all share those volumes.
- On 2026-11-02, `postgres-bf05da0e` and `postgres-28003c8a` ran at the same time on `skoleoverblikket-pgdata-a`. Postgres's `postmaster.pid` check didn't stop the second server.
- The same sharing moved every worktree's dev Keycloak database to 26.8 during [69](69-keycloak-26-8.md). Stacks still on 26.2 log `Possibly incorrect state of migration`.
- Size S. Owner: Agent. Prod isn't affected, and the prod Postgres image shouldn't change for this.

## Options

1. **Volumes per worktree (recommended).** Add the AppHost's path hash to the volume names. Stacks stop sharing anything, at the cost of each worktree seeding its own data. The backup agent and the restore wizard use the same volumes, so check they still agree.
2. **Refuse to start a second stack.** Check for a running `postgres-*` container with the same volume before starting. Keeps shared dev data, but parallel sessions then block each other.

## Done when

- Two worktrees can run Aspire at the same time without sharing a data directory, and `aspire run` still works from a clean machine.

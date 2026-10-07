---
title: 'Task index'
purpose: 'Single prioritized list of open tasks, so "implement next task" has one obvious answer.'
description: >-
  Every open task in tasks/, ranked by business value against cost. Quick wins
  and customer-promise fixes first, then data safety, then features that sell
  the product to friskoler, then AI and feedback automation, then the folkeskole
  (STIL/Aula) track. Update this file whenever a task is added or completed.
status: 'Active'
---

# Task index

## TL;DR

"Implement next task" = the first row in **Up next** whose dependencies are done and whose owner isn't *Niels only*. When a task ships: move its file to [completed/](completed/) and delete its row here. New task: give it a row in the right spot, don't append to the bottom by default.

**Size**: S ≈ under a day, M ≈ 1–3 days, L ≈ a week or more. **Owner**: *Agent* = an agent can do it end to end. *Niels + agent* = needs Niels for accounts, servers or decisions, an agent does the code. *Niels only* = no code to write.

## Do now (no code)

- **Dokploy backup retention → 14 days.** The DPA promises 14 days ([53](53-restore-drill.md) context), the ADR says 30. Five minutes in Dokploy. Don't wait for 53 or 54.
- **Lawyer review of the DPA**, see [TO_VERIFY.md](TO_VERIFY.md) "Your turn". Blocks relying on the DPA in sales.

## Up next

| # | Task | Size | Owner | Depends on | Why this spot |
|---|---|---|---|---|---|
| 1 | [55 Bestyrelse under Stamdata](55-board-in-masterdata.md) | S | Agent | — | Frontend only, fully specced, already staged. Hanne can't find the board today. |
| 2 | [58 Enforce paid modules in the API](58-enforce-board-module.md) | M | Agent | 55 | BoardModule and ParentModule are sold at 300 kr/md each but no endpoint checks them, so buying one unlocks nothing and every school gets both free. |
| 3 | [54 Move prod to bigger VPS](54-move-vps.md) | L | Niels + agent | 60 Phase 0 + A (code done 2026-10-07) | VPS could use better speccs for higher volume, prod Postgres is reachable (but secure) from the internet, RPO is 24h. Absorbs 53 phases 3–5, so do it before 53 to avoid building a drill twice. Ships together with 60 Phase A, or the new box has no backups. |
| 4 | [60 Backup agent and break-glass console](60-backup-console.md) | L | Niels | 54 (ships with the cutover) | Code for Phases 0–C is done and proven on a local stack. Left: OVH repo and ops buckets with keys, elmah.io heartbeats, turning on the `selfhosted-db` profile at the 54 cutover, one real restore through the wizard. |
| 5 | [53 Restore drill and off-site backup](53-restore-drill.md) | M | Niels + agent | 54, 60 | Drill itself moved to 60. What's left: runbook, off-site copy of DB and files, quarterly full-rebuild drill. Losing a school's data would end the company. Phase 2 waits for the 30-day sub-processor notice. |
| 6 | [44 Auto-rollback and deploy safety net](44-auto-rollback.md) | M | Agent | — (coordinate migration check with 54) | A bad deploy currently stays live until someone notices. Prerequisite for the feedback chain (45–47). |
| 7 | [43 Links](43-links.md) | M | Agent | — | Grilled and specced. Basis feature that gives parents a reason to open the app and saves Hanne repeat questions. |
| 8 | [20 Stå mål med — Fase 3](20-staa-maal-med-fase3.md) | L | Agent | — | Status Ready. §1a publishing is a legal need for every friskole (primary market) and the copy promising it was pulled until it ships. Sells the board/governance module. |
| 9 | [37 Skole-hjem-samtaler sign-up](37-parent-teacher-signup.md) | M | Agent | — | Every school runs these twice a year, today on paper or email. Visible to every parent, strengthens the paid parent module. |
| 10 | [25 Schema import via AI](25-schema-import.md) | M | Niels + agent | Alexandra Instituttet DPA and pricing | Re-typing the old schema is the biggest switching barrier for a new school. Builds the shared Alexandra LLM client that 46 and 21 reuse. |
| 11 | [45 Feedback button](feedback/45-feedback-capture.md) | L | Agent | 44 | Two-click bug reports with context. More valuable once there are more schools than Niels can talk to directly. |
| 12 | [41 Uncovered lesson tracking](41-uncovered-lesson-tracking.md) | S | Agent | — (task 38 shipped) | Small read-only count next to the Stå mål med coverage view. Pairs naturally with 20 Fase 3. |
| 13 | [50 Parent dashboard](50-parent-dashboard.md) | M | Agent | 43, grill first | Stub with open questions. Reuses the Links card from 43. |
| 14 | [46 Feedback AI triage](feedback/46-feedback-ai-triage.md) | M | Niels + agent | 45, Alexandra pricing | Only pays off once 45 produces real report volume. |
| 15 | [47 Feedback AI fix PRs](feedback/47-feedback-ai-fix-prs.md) | M | Agent | 44, 46 | Last link in the feedback chain. Auto-merge ships disabled. |
| 16 | [21 AI schema suggestions](21-ai-suggestions-for-schema.md) | M | Agent | Shared LLM client (25 or 46) | Nice to have. Hanne builds a schema once a year, and 25 covers the switching case. |
| 17 | [59 Microsoft and Google login](59-microsoft-google-login.md) | M | Niels + agent | Ask schools which accounts staff use | Free replacement for UNI•Login (07, parked over STIL fees). One password fewer for staff. Niels checks with schools first. |

## Folkeskole track (blocked on STIL / Aula)

Secondary market. Runs in parallel with the list above because the calendar time is bureaucracy, not code.

| Task | Size | Owner | Blocked on |
|---|---|---|---|
| [07 UNI•Login SSO](07-uni-login.md) | L | Niels + agent | **Parked 2026-10-07.** STIL charges 7,500 kr. setup + 7,500 kr./year. Don't order a tjeneste until a paying folkeskole asks or there are ~10+ paying schools. Use 59 meanwhile. |
| [23 STIL user data import](23-stil-userdata-import.md) | L | Niels + agent | 07 (parked), client certificate, a data agreement per school |
| [22 Aula widget](22-info-screen-module-aula-widget.md) | M | Niels + agent | Aula supplier access |

## Later

| Task | Why later |
|---|---|
| [20 Student module](20-student-module.md) | Large, and students aren't the buyer. Revisit when a school asks for it. |

## Needs a spec before it can be ranked

| File | Note |
|---|---|
| [33-app.md](33-app.md) | Empty. |
| [51-stripe-to-alunta.md](51-stripe-to-alunta.md) | Empty, and number 51 is also used by full data export. Moving billing off Stripe contradicts "Billing via Stripe Checkout" in [AGENTS.md](../AGENTS.md), so it needs an ADR first. |

## Not tasks

- [TO_VERIFY.md](TO_VERIFY.md): manual UI acceptance checklist.

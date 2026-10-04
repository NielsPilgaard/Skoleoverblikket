---
title: 'Gratis flyttehjælp'
purpose: 'Offer free help moving a school''s data from its current system, promoted on the landing page, and record the reversal of the paid-migration decision.'
description: >-
  The PRD and landing page say migration help is a paid add-on (~3.000 kr).
  Make it free: a landing-page section, a request form that reuses the demo
  form, an in-app link from Importer data, and a written process for doing
  the work. No new import tooling. Schema rebuild is done by us; personal
  data about students and parents is pasted in by the school itself.
status: 'Proposed'
---

# Gratis flyttehjælp

## TL;DR

"Hjælp til overgangen kan købes som tillægsservice" becomes **"Vi flytter jeres data gratis."** The hardest part of switching is rebuilding the schema and master data, and a school stuck on Skoleintra or Docendo won't start a trial if switching looks like a week of typing. We remove that objection. What we build: a landing-page section, a `?emne=flytning` variant of `/book-demo`, a link from `/import` and the setup wizard, and an ADR plus PRD edits. What we don't build: parsers for other vendors' exports. The work is done by hand with the existing paste import, or later with [task 25](25-schema-import.md).

## Context

- [PRD.md:33](../docs/PRD.md#L33) and [PRD.md:253](../docs/PRD.md#L253): guided migration is a paid consulting service (~3.000 kr), and "do not build automated migration tooling as a free feature". This task reverses the first part and keeps the second.
- [LandingPage.tsx:169](../web/src/pages/LandingPage.tsx#L169), audience card "Skoler der vil skifte system": "Hjælp til overgangen kan købes som tillægsservice."
- `/import` ([ImportPage.tsx](../web/src/pages/ImportPage.tsx)) already has paste import for Elever & forældre, Medarbejdere, Klasser, Lokaler and Bestyrelse. Fag and schema have no import. Schema import via AI is [task 25](25-schema-import.md).
- `/book-demo` ([DemoPage.tsx](../web/src/pages/DemoPage.tsx)) posts to `DemoRequestController`, which emails kontakt@skoleoverblikket.dk. The email logic sits in the controller.
- A superadmin can't write into a customer tenant: `tenant_id` comes from the JWT, and view-as only changes the UI mode. To do the work for a school, we need a user **in that school's tenant**.
- One Keycloak user per e-mail, tied to one tenant (`CreateStaffUserAsync(..., tenant.TenantId)`). A support login needs its own address per school.
- Exports from other systems: see the table in [PRD.md § Migration from competitors](../docs/PRD.md#migration-from-competitors). No standard exchange format exists. Expect Excel, CSV and PDF.
- Personal data must stay in the EU, and school data never goes to Claude or GitHub ([ai-data-boundary](../docs/adr/ai-data-boundary.md), vendor constraints).

## Decisions

- **D1 — Free, no fine print, for every school that signs up.** Free during the trial too. The trial is when they decide, and that's when the help matters. The guardrail is the scope (D2), not the price.
- **D2 — What "flytte" covers.** We set up: Klasser, Fag, Lokaler, Medarbejdere, ringetider/lektioner and the **weekly schema**, rebuilt from the school's current schema (Excel/PDF/screenshot). The school pastes in **students and parents** itself via `/import`, in a 30-minute video call with us if they like. Not covered: message history, old ugeplaner, fraværshistorik, files. We say so plainly on the form page.
  - Why the split: the schema is the slow part (hours of work) and holds almost no sensitive data (class names, subjects, teacher names/initials). Students and parents are the sensitive part, but paste import takes minutes. So the school keeps child data in its own hands, and we never receive a file of children's CPR numbers or addresses.
- **D3 — How we get access: the school invites us as an administrator.** The school creates a staff member "Skoleoverblikket Support" with e-mail `support+<slug>@skoleoverblikket.dk` and admin rights, using the existing invite. No backdoor, no code. The school can see who has access and remove it. We remove ourselves when done (D6).
- **D4 — Handover of the current schema: e-mail is fine, child data isn't.** The form says: "Send os jeres nuværende skema (Excel, PDF eller skærmbillede). Send ikke elev- eller forældrelister — dem sætter I selv ind." Received files are deleted from the mailbox when the job is done. Never pasted into Claude/ChatGPT.
- **D5 — Request form = demo form with a topic.** `/book-demo?emne=flytning` shows a different heading and an extra field "Hvilket system bruger I i dag?" (free text). No new page or route. Backend: optional `Topic` and `CurrentSystem` on the request. Subject becomes `Flyttehjælp: {skole} (fra {system})`. Since this changes the endpoint's logic, move the email building into `DemoRequestService` ([AGENTS.md](../AGENTS.md#encapsulation-thin-controllers-feature-services)).
- **D6 — Done means checked by the school.** We email: "Skemaet er sat op. Tjek det, og fjern så Skoleoverblikket Support under Medarbejdere." If our user is still there after 14 days, we remove it ourselves.
- **D7 — Name competitors on the page, neutrally.** "Fra fx Skoleintra, Docendo, Skoleplan eller Excel." Comparative mention is allowed under markedsføringsloven § 5 if it's factual. No logos, no claims about them.

## Scope

### 1. Docs: reverse the decision

- [ ] New ADR `docs/adr/free-migration-help.md` (template in docs-authoring): decision D1–D4, alternative "paid ~3.000 kr" rejected (removes the biggest objection for segments B/C; cost is founder hours, capped by D2). Row in [INDEX.md](../docs/adr/INDEX.md): "Migration help — free, manual, schema by us, child data by the school".
- [ ] [PRD.md](../docs/PRD.md): segment B line 33 and the "Migration consulting add-on" paragraph (line 253) become one-line links to the ADR. Keep "do not build automated migration tooling" (tooling is task 25's call, not this one's).
- [ ] [PRICING.md](../docs/PRICING.md): one line under Basis: "Gratis flyttehjælp, se ADR."

### 2. Landing page — [LandingPage.tsx](../web/src/pages/LandingPage.tsx)

- [ ] Audience card text: "Skift fra et gammelt eller dyrt system. Vi flytter jeres skema og stamdata gratis."
- [ ] New section between Audience and Pricing, `id="flyttehjaelp"`, `data-testid="migration-section"`:
  - Heading: "Skifter I system? Vi flytter jeres data — gratis"
  - Line: "Fra fx Skoleintra, Docendo, Skoleplan eller Excel. I sender os jeres nuværende skema, vi sætter det op."
  - Three steps (numbered, stacked on phone, three columns from `sm`): **1. Opret skolen** ("Gratis i 30 dage, intet kreditkort.") **2. Send os jeres skema** ("Excel, PDF eller et skærmbillede er nok.") **3. Tjek og gå i gang** ("Vi bygger klasser, fag, lokaler, medarbejdere og ugeskema. I tjekker, at det er rigtigt.")
  - Small print: "Elev- og forældrelister sætter I selv ind på få minutter — vi hjælper gerne over et videoopkald."
  - Button "Få hjælp til at flytte" → `/book-demo?emne=flytning`, `data-testid="migration-cta"`.
- [ ] Trust row: no change. Hero: no change (keep one message).
- [ ] [features.tsx](../web/src/content/features.tsx): no new card (it's a service, not a feature, and no sidebar route).

### 3. Request form — [DemoPage.tsx](../web/src/pages/DemoPage.tsx)

- [ ] Read `emne` from the query string. `flytning` → heading "Få hjælp til at flytte jeres data", intro with what's included and not included (D2), extra field "Hvilket system bruger I i dag?" (`data-testid="demo-current-system"`), and the D4 note about not sending student lists.
- [ ] Send `topic: 'Migration'` and `currentSystem` via the generated client (run `/codegen`).
- [ ] Success text for flytning: "Tak! Vi skriver inden for 1–2 hverdage med, hvordan I sender os skemaet."

### 4. API — demo request

- [ ] `Services/DemoRequestService.cs`: `SubmitAsync(DemoRequest, ct)` builds and sends the email (moved from the controller). Register in `AddDomainServices`.
- [ ] Request record next to the service: existing fields plus `DemoRequestTopic Topic = Demo` (enum `Demo | Migration`) and `[MaxLength(200)] string? CurrentSystem`. Keep the existing JSON field names (`navn`, `skole`, ...) so the client doesn't break. Renaming them to English is a separate change.
- [ ] Subject: `Demo-forespørgsel fra {skole}` / `Flyttehjælp: {skole} (fra {system})`. Body line `Nuværende system: ...` when set.
- [ ] Controller: bind, call the service, `Ok()`. Rate limit unchanged.

### 5. In the app: where a trial school finds it

- [ ] [ImportPage.tsx](../web/src/pages/ImportPage.tsx): one line at the top: "Kommer I fra et andet system? Vi sætter jeres skema op gratis. [Få hjælp]" → `/book-demo?emne=flytning`. `data-testid="import-migration-help-link"`.
- [ ] [SchoolSetupWizardPage.tsx](../web/src/pages/SchoolSetupWizardPage.tsx): the same line on the first step.
- [ ] Founder welcome email ([task 52](52-founder-welcome-email.md)): add one sentence if that task isn't done yet, otherwise a follow-up.

### 6. Process (not code): `docs/MIGRATION_HELP.md`

A short runbook for whoever does the work:

1. Reply to the request within 1–2 working days. Ask for the current schema and ringetider. Send the invite instructions (D3).
2. Check that the school has accepted the DPA (signup does this). We only work in their tenant after that.
3. Set up in this order: ringetider → lokaler → fag → medarbejdere (paste import) → klasser → schema. Use `/import` wherever it has a tab.
4. Book the optional 30-minute call for the students/parents paste import.
5. Send the "tjek det" email (D6). Delete the received files from the mailbox. Log the hours spent in a private sheet, so we know what "free" costs.
6. After 14 days: remove the support staff member if it's still there.

## Testing

- **API** (new `DemoRequestTests.cs`, since the endpoint has no test today): POST with `topic: Migration` and `currentSystem` → 200, and the fake `IEmailSender` gets the subject `Flyttehjælp: ...` with the system in the body. POST without `topic` → old subject (backwards compatible).
- **Playwright** (`web/tests/e2e/migration-help.spec.ts`, one flow): landing page → `migration-cta` → form shows `demo-current-system` → submit → success text. Selectors: `data-testid` only.
- Manual: the landing section at 375 px, no horizontal scroll.

## Open questions

1. **Plus addressing**: does our mail host deliver `support+<slug>@skoleoverblikket.dk` to one inbox? If not: a catch-all on a subdomain, or a few numbered aliases.
2. **Support user in staff lists**: "Skoleoverblikket Support" shows up as a medarbejder in schema teacher pickers, vikar candidates and reports while it exists. OK for a few days, or do we want a "skjult i lister" flag later? Recommendation: accept it for now. D6 keeps it short-lived.
3. **Capacity**: free with no cap is fine at < 10 schools a month. Revisit when the hours log (step 5) says otherwise, e.g. by doing it only for paying schools or adding a waiting time.
4. **SEO pages** like "skift fra Skoleintra": separate follow-up under [task 40](completed/40-seo.md), not this task.

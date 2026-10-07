---
title: 'Databehandleraftale (DPA) and sub-processor list'
purpose: 'Give every customer school the GDPR Art. 28 processor agreement it is legally required to have with us, with a maintained list of sub-processors.'
description: >-
  Skoleoverblikket processes children's and staff data on behalf of schools but
  has no databehandleraftale. Draft one based on the EU standard contractual
  clauses for controller–processor (2021/915), publish it with a sub-processor
  list, have schools accept it at signup/checkout, and notify admins before
  adding sub-processors. Prerequisite for selling — independent of the feedback
  feature, but the feedback feature adds sub-processors.
status: 'In progress'
---

# Databehandleraftale (DPA) and sub-processor list

## TL;DR

Schools are data controllers; we are their processor. GDPR Art. 28 requires a written processor agreement, and we have none — every current customer is out of compliance through us. Draft a databehandleraftale (start from the Commission's standard contractual clauses, Implementing Decision (EU) 2021/915, or Datatilsynet's template), publish it plus a sub-processor page, record acceptance (version + timestamp + user) at signup/checkout, and email tenant admins 30 days before a new sub-processor is added. Get it reviewed by a lawyer before publishing.

## Context

Surfaced while planning the feedback system ([ai-data-boundary](../../docs/adr/ai-data-boundary.md)), which adds Alexandra Instituttet and Scaleway transcription as processors of personal data. Friskole boards will ask for this document; folkeskoler (kommuner) will require it outright.

## Scope

1. **Document** (Danish): processing purposes, data categories (elever, forældre, medarbejdere, fravær, beskeder, filer), security measures, breach notification (≤ 48 h to the school), deletion on termination, audit rights, sub-processor clause with prior general authorisation + notice + right to object.
2. **Sub-processor list** (public page `/underdatabehandlere`), initial draft — verify each before publishing:

| Sub-processor | Purpose | Location |
|---|---|---|
| OVHcloud | VPS, PostgreSQL, object storage, backups | EU (FR) |
| Scaleway | Transactional email; speech-to-text for feedback | EU (FR) |
| Alexandra Instituttet | AI triage of feedback; AI schema suggestions (DPA available; their own sub-processor: ScanNet / team.blue Denmark for hardware) — not live yet, add with notice | DK |
| Stripe | Billing (school admin contact + payment data) | EU entity (IE); check transfers |
| elmah.io | Error logs (may contain user ids / request data) | DK — verify hosting region |

   Not sub-processors (no personal data by design): GitHub, Anthropic, Pingpuffin (pings public URLs only).
3. **Acceptance**: checkbox at signup / Stripe Checkout start ("Jeg accepterer databehandleraftalen på vegne af skolen"), stored as `DpaAcceptance` (tenant, user, version, timestamp). Existing tenants: banner for admins until accepted. Migration by a human.
4. **Change notice**: when the sub-processor list changes, email all tenant admins ≥ 30 days ahead; bump the DPA version.
5. Update the privatlivspolitik to reference the DPA and the list.

## Status (2026-10-03)

Built:

- Public pages `/databehandleraftale` (draft text, version 1.0) and `/underdatabehandlere`, both prerendered and in the footer and sitemap. Version and list live in `web/src/content/dataProcessing.ts`.
- `DataProcessingAgreementAcceptance` (tenant, version, subject, name, email, time). Signup requires the checkbox and stores acceptance in the same save as the school. Existing schools: amber banner for admins until accepted.
- Superadmin backoffice page "Underdatabehandlere" sends the change notice (Bcc to every school's admins), refusing less than 30 days' notice.
- Privacy policy references the DPA and shows the same sub-processor list.

Left for Niels:

- [ ] Lawyer review of the agreement text before merging/publishing. Bump the version (both `DPA_VERSION` and `DataProcessingAgreementService.CurrentVersion`) if the text changes after schools have accepted.
- [x] Backup retention in section 10: production database backups are kept 14 days (`BACKUP_RETENTION_DAYS`).
- [ ] Get elmah.io's DPA and confirm their transfer basis (their legal pages name none; the list now says Microsoft is DPF-certified). Add elmah.io's CVR and address to `SUB_PROCESSORS`.
- [ ] Fill in the security section with measures that are true today: encryption at rest for disk and backups, MFA on OVH/Dokploy/Keycloak admin.
- [ ] Privacy policy: name the mailbox provider behind kontakt@skoleoverblikket.dk, and give elmah.io's log retention in days.
- [x] elmah.io: keep it, scrub personal data before sending (see below).

Review pass (2026-10-04): added the art. 28(3) duty to flag unlawful instructions, the company's CVR and address, folkeskole/kommune as controller, Datatilsynet access, governing law, and what happens if a new version isn't accepted. Stripe moved out of the sub-processor list (it only gets our own customer data). Privacy policy split into school-controlled data vs. our own, with a legal basis per purpose. Google Fonts replaced by self-hosted fonts (web and Keycloak theme). elmah.io: the JWT name claim is now `sub`, the scrubber drops a non-id user and PostgreSQL's "Failing row contains" values.

## Open questions

- ~~Does elmah.io store data in the EU?~~ **No.** elmah.io stores all data in Azure West US / East US and offers no EU region ([legal FAQ](https://elmah.io/legal/legal-faq)). Listed as a US transfer. `ElmahIoScrubber` now runs on every message from both elmah.io hooks (unhandled exceptions and Error logs): it drops form data, cookies, query values and all headers except a short allowlist (so no Authorization, cookies or client IP), and masks emails, phone and CPR numbers, bearer tokens/JWTs and PostgreSQL key values in titles, details, data and breadcrumbs. Ids are kept on purpose.
- Alexandra Instituttet and Scaleway transcription are not on the list yet: the AI features are not built. Send the 30-day notice before they ship.

## Out of scope

- Per-school negotiated DPAs (kommuner may send their own; handle case by case).

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
status: 'Proposed'
---

# Databehandleraftale (DPA) and sub-processor list

## TL;DR

Schools are data controllers; we are their processor. GDPR Art. 28 requires a written processor agreement, and we have none — every current customer is out of compliance through us. Draft a databehandleraftale (start from the Commission's standard contractual clauses, Implementing Decision (EU) 2021/915, or Datatilsynet's template), publish it plus a sub-processor page, record acceptance (version + timestamp + user) at signup/checkout, and email tenant admins 30 days before a new sub-processor is added. Get it reviewed by a lawyer before publishing.

## Context

Surfaced while planning the feedback system ([ai-data-boundary](../docs/adr/ai-data-boundary.md)), which adds Alexandra Instituttet and Scaleway transcription as processors of personal data. Friskole boards will ask for this document; folkeskoler (kommuner) will require it outright.

## Scope

1. **Document** (Danish): processing purposes, data categories (elever, forældre, medarbejdere, fravær, beskeder, filer), security measures, breach notification (≤ 48 h to the school), deletion on termination, audit rights, sub-processor clause with prior general authorisation + notice + right to object.
2. **Sub-processor list** (public page `/underdatabehandlere`), initial draft — verify each before publishing:

| Sub-processor | Purpose | Location |
|---|---|---|
| OVHcloud | VPS, PostgreSQL, object storage, backups | EU (FR) |
| Scaleway | Transactional email; speech-to-text for feedback | EU (FR) |
| Alexandra Instituttet | AI triage of feedback; AI schema suggestions (DPA available; their own sub-processor: ScanNet / team.blue Denmark for hardware) | DK |
| Stripe | Billing (school admin contact + payment data) | EU entity (IE); check transfers |
| elmah.io | Error logs (may contain user ids / request data) | DK — verify hosting region |

   Not sub-processors (no personal data by design): GitHub, Anthropic, Pingpuffin (pings public URLs only).
3. **Acceptance**: checkbox at signup / Stripe Checkout start ("Jeg accepterer databehandleraftalen på vegne af skolen"), stored as `DpaAcceptance` (tenant, user, version, timestamp). Existing tenants: banner for admins until accepted. Migration by a human.
4. **Change notice**: when the sub-processor list changes, email all tenant admins ≥ 30 days ahead; bump the DPA version.
5. Update the privatlivspolitik to reference the DPA and the list.

## Open questions

- Does elmah.io store data in the EU? If not, scrub personal data from error logs or switch.

## Out of scope

- Per-school negotiated DPAs (kommuner may send their own; handle case by case).

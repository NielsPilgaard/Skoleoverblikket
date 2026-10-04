---
title: 'UNI•Login SSO Integration (Extra Module)'
purpose: 'Plan the STIL registration and the Keycloak federation work needed to let staff log in with UNI•Login.'
description: >-
  Optional paid add-on that adds UNI•Login (STIL's national school SSO) as a
  federated OIDC identity provider in Keycloak, mapping UNI•Login users to
  existing staff records. Requires STIL vendor registration via MitID Erhverv.
  Tasks are split into steps only Niels can do (MitID, portal submissions) and
  prep work an agent can do ahead of STIL approval.
status: 'In progress'
---

# UNI•Login SSO Integration (Extra Module)

## TL;DR

Add UNI•Login as a federated OIDC IdP in Keycloak (OIO OIDC 0.9, PKCE, confidential client) so folkeskole staff log in with existing credentials. Most of the calendar time is STIL bureaucracy that needs Niels' MitID Erhverv. Everything else (form texts, metadata JSON, Keycloak config, staff matching, mock IdP, school onboarding kit) can be built before STIL approves anything, so test credentials only need pasting in when they arrive.

UNI•Login is Denmark's national SSO for educational institutions, operated by STIL. It is the de facto standard for folkeskoler — staff and teachers already have UNI•Login accounts provisioned by their municipality.

This is an **optional paid add-on module**, not part of the base plan. It removes the signup barrier for folkeskoler who want staff to log in with existing UNI•Login credentials rather than creating a new Keycloak account. Pricing TBD.

## How it works

UNI•Login acts as an external identity provider. Keycloak stays as the internal auth layer — UNI•Login is added as a federated IdP (OIDC) in Keycloak. On first login, the UNI•Login subject is mapped to the matching staff record in the tenant.

Vendor registration is required: register as a service provider with STIL and sign a data processor agreement. No public SDK — standard OIDC against STIL's metadata endpoints.

## STIL Registration Process

### Prerequisites

- MitID Erhverv with permission: *"Tilslutning: Ret til at administrere aftaler på tilslutning.stil.dk"*
- Also need: *"Provider Portal: Right to manage services"* for udbyderportal.stil.dk
- Company must be registered in CVR (already done)

### Steps

0. **Get created as udbyder** — a working MitID Erhverv login isn't enough. Until STIL creates the CVR as an udbyder organisation, tilslutning.stil.dk/adm shows "Ingen organisationer … har ikke privilegie til nogen organisationer i Tilslutning". Request it via the Tilslutning.stil.dk supportformular at [stil.dk/support](https://www.stil.dk/support) with CVR, company name, notification email, desired name in Tilslutning, business purpose and a school contact.
1. **Register as tjenesteudbyder** on [tilslutning.stil.dk](https://tilslutning.stil.dk) — create a tjeneste and request connection to "Unilogin Broker OIDC"
2. **Build + test** via [udbyderportal.stil.dk](https://udbyderportal.stil.dk) — self-service portal for creating/managing the OIDC service
3. **Production approval** — Provider Portal cannot self-approve production; must file a support case at [stil.dk/support](https://www.stil.dk/support) with:
   - OIDC metadata JSON named `{supportcasenr}_prod_oidc_metadata.json`
   - Declaration that UNI•Login serves a relevant educational purpose in the service
4. **Each pilot school** approves the dataaftale in their own UNI•Login admin tool

**STIL support hours: Mon–Fri 08:00–14:00** — contact via [stil.dk/support](https://www.stil.dk/support) (case-based, no public email)

### Technical requirements

- OIDC, follows **OIO OIDC 0.9** profile
- **PKCE required** (`S256`), confidential client, `response_type=code`, `scope=openid` only — Unilogin ignores `profile`/`email`
- `state` and `nonce` at least 16 alphanumeric chars
- Standard OIDC endpoints — no proprietary SDK. Test broker: `https://et-broker.unilogin.dk/auth/realms/broker`, prod: `https://broker.unilogin.dk/auth/realms/broker`
- Keycloak broker redirect URI (prod): `https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/broker/unilogin/endpoint` (IdP alias `unilogin`)
- **No email claim.** ID token gives `sub`, `uniid` (10 hex chars), `aktoer_gruppe`, `institution_ids`, `unilogin_loa`. Userinfo `inst_brugere` gives `instnr`/`instnavn` freely; name and `roller` only with a per-school data agreement (`uniloginUdvidetBillet`). So the match key can't be email — see Agent prep §3.
- Session management (`session_state`) not supported by Unilogin.
- STIL publishes no format for the prod metadata JSON. Expect it to come from udbyderportal or the support case reply.

### Data agreements (three required)

| Agreement | Between |
|-----------|---------|
| Tilslutningsaftale | Skoleoverblikket ↔ STIL (via tilslutning.stil.dk) |
| Dataaftale | Skoleoverblikket ↔ each school (school approves in their UNI•Login admin) |
| Databehandleraftale | Skoleoverblikket ↔ each school — covered by [task 48](completed/48-databehandleraftale.md), don't draft a separate one here |

**Note for friskoler:** Schools do NOT need a separate databehandleraftale with STIL itself — only with Skoleoverblikket as vendor. ([source](https://www.friskolerne.dk/nyheder/artikel/ingen-databehandleraftale-ved-brug-af-unilogin))

### Key links

- OIDC docs: [viden.stil.dk/display/OFFSKOLELOGIN/OIDC](https://viden.stil.dk/display/OFFSKOLELOGIN/OIDC)
- OIDC FAQ: [FAQ: Tilslutning af OIDC tjeneste](https://viden.stil.dk/display/OFFSKOLELOGIN/FAQ:+Tilslutning+af+OIDC+tjeneste+i+Unilogin)
- Technical requirements: [viden.stil.dk Tekniske krav](https://viden.stil.dk/display/OFFSKOLELOGIN/Tekniske+krav)
- Connect service: [viden.stil.dk Tilslut tjeneste](https://viden.stil.dk/display/OFFSKOLELOGIN/Tilslut+tjeneste)

## Agent prep work (no STIL access needed)

Do these before or in parallel with the MitID steps, so each manual step becomes copy-paste. Check the current viden.stil.dk pages before writing form texts — STIL's portals change.

### 1. Paste-ready form texts

Put them in `docs/stil/` (Danish, ready to copy):

- Tjeneste name and description for tilslutning.stil.dk
- Declaration that UNI•Login serves a relevant educational purpose (for the production support case)
- Support case body text for production approval, listing what's attached

### 2. Production OIDC metadata JSON

Template file `docs/stil/prod_oidc_metadata.template.json` with redirect URI, post-logout redirect URI, PKCE and scopes filled in. Niels renames it to `{supportcasenr}_prod_oidc_metadata.json` when the case number is known.

### 3. Technical implementation

- Add a `unilogin` OIDC identity provider to [Skoleoverblikket-realm.json](../infrastructure/keycloak/realms/Skoleoverblikket-realm.json), **disabled by default**, with client ID/secret from config so test/prod credentials only need pasting in.
- First-broker-login flow: map the UNI•Login user to an existing staff record in the tenant. Unilogin releases no email, so Keycloak's default email-based first-broker-login can't match. Store `uniid` on the staff record once linked; check `institution_ids` contains the tenant's institution number. First link happens two ways (decided 2026-10-04):
  - **New staff**: the invitation page gets a "Log ind med Unilogin" button. The invitation token identifies the staff record.
  - **Existing staff**: a "Forbind Unilogin" button after normal login (Keycloak account linking).
  - After that, `uniid` is stored and Unilogin login goes straight in. No email matching, no auto-created accounts.
- Sikringsniveau (decided 2026-10-04): request `Low` so password-only Unilogin works. Users who have two-factor on their Unilogin use it, and `unilogin_loa` records which one was used. That matches today's Keycloak password login, so Unilogin isn't weaker than what exists. Requiring `Substantial` per school can be added later if a school asks.
- No-match case: clear Danish error page telling the user to ask the school's admin to invite them (no auto-created accounts).
- Mock UNI•Login: a second local Keycloak realm acting as fake STIL IdP, wired into the Aspire stack, so the whole flow runs locally.
- Tests: integration tests for staff matching and the no-match path; Playwright e2e for the login flow against the mock IdP.
- Module gating: UNI•Login login only available for tenants with the add-on (pricing TBD, see [PRICING.md](../docs/PRICING.md)).

### 4. Pilot school kit

- One-page Danish guide for the school's UNI•Login admin: how to find and approve the Skoleoverblikket dataaftale.
- Databehandleraftale comes from [task 48](completed/48-databehandleraftale.md).

## Tasks

### Niels only (MitID Erhverv / portal access)

- [x] Get MitID Erhverv login to tilslutning.stil.dk working (2026-10-04)
- [x] Confirm MitID Erhverv access to udbyderportal.stil.dk (2026-10-04). Use the **Tjenester** tab; **Lokal IdP** is for organisations plugging their own IdP into the Broker, not for us
- [x] Ask STIL support to create Skoleoverblikket as udbyder in Tilslutning (filed 2026-10-04, waiting for STIL)
- [ ] Register on tilslutning.stil.dk — request "Unilogin Broker OIDC" (use texts from `docs/stil/`)
- [ ] Create test OIDC service on udbyderportal.stil.dk; paste test client ID/secret into config
- [ ] File production support case at stil.dk/support (Mon–Fri 08–14) with metadata JSON + declaration
- [ ] Find a folkeskole pilot school and send them the pilot kit

### Agent (can start now)

- [x] Write paste-ready Danish form texts in [docs/stil/form-texts.md](../docs/stil/form-texts.md) (2026-10-04)
- [ ] Write production OIDC metadata JSON template
- [ ] Add `unilogin` IdP (disabled) to Keycloak realm, credentials from config
- [ ] Map UNI•Login user to existing tenant staff record on first login
- [ ] No-match error page (invite-only, no auto-created accounts)
- [ ] Mock UNI•Login realm in local Aspire stack
- [ ] Integration + e2e tests against mock IdP
- [ ] Gate UNI•Login behind the add-on module
- [ ] Pilot school guide for approving the dataaftale

### Together

- [ ] Test end-to-end against STIL test environment once credentials exist
- [ ] Test with a folkeskole pilot school

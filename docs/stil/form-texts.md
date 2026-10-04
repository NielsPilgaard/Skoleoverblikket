---
description: >-
  Paste-ready Danish texts and technical values for the STIL Unilogin
  registration: tjeneste description, educational-purpose declaration,
  production support case and the URLs to enter in udbyderportal.stil.dk.
---

# STIL form texts (Unilogin)

## TL;DR

Copy-paste texts for [task 07](../../tasks/07-uni-login.md). Texts are Danish because STIL reads them. Fill in `[brackets]` before sending. Checked against viden.stil.dk on 2026-10-04.

## 1. Tjeneste på tilslutning.stil.dk

Used when requesting connection to "Unilogin Broker OIDC".

**Navn på tjeneste**

> Skoleoverblikket

**Kort beskrivelse** (one line)

> Administrationssystem til grundskoler: skema, vikardækning, ugeplaner, fravær og forældrekommunikation.

**Beskrivelse / formål**

> Skoleoverblikket er et webbaseret administrationssystem til danske grundskoler (friskoler, privatskoler og folkeskoler). Skolens personale bruger systemet til skemaplanlægning, vikardækning, ugeplaner, fraværsregistrering, SFO-planlægning og kommunikation med forældre.
>
> Vi ønsker tilslutning til Unilogin Broker, så skolens medarbejdere kan logge ind med deres eksisterende Unilogin i stedet for et separat brugernavn og adgangskode. Login er kun muligt for medarbejdere, som skolen selv har oprettet i Skoleoverblikket. Unilogin opretter ikke nye brugere automatisk.
>
> Vi bruger UniID og institutionsnummer til at koble login til den rigtige medarbejder på den rigtige skole. Vi beder ikke om CPR-nummer.

## 2. Erklæring om uddannelsesmæssigt formål

For the production support case.

> Skoleoverblikket erklærer hermed, at Unilogin anvendes til et relevant uddannelsesmæssigt formål i tjenesten Skoleoverblikket.
>
> Tjenesten understøtter grundskolers daglige drift og undervisning: lærernes og pædagogernes skemaer, vikardækning af lektioner, ugeplaner til elever og forældre, lovpligtig fraværsregistrering og offentliggørelse af undervisningsplaner efter friskolelovens § 1a. Unilogin bruges udelukkende til at give skolens egne medarbejdere adgang til disse funktioner.
>
> CVR: 41249269
> Dato: [dato]
> Navn: Niels Pilgaard Grøndahl

## 3. Supportsag: godkendelse til produktion

Filed via [stil.dk/support](https://www.stil.dk/support) after the test service works. Attach the metadata JSON renamed to `{supportcasenr}_prod_oidc_metadata.json` and the declaration above.

**Hvad drejer henvendelsen sig om?**

> Godkendelse af OIDC-tjeneste til produktion i Unilogin Broker

**Længere beskrivelse**

> Hej STIL,
>
> Vi har oprettet og testet OIDC-tjenesten "Skoleoverblikket" i udbyderportalen mod testmiljøet (et-broker.unilogin.dk) og ønsker den godkendt til produktion.
>
> Vedhæftet:
> - [supportsagsnr]_prod_oidc_metadata.json
> - Erklæring om at Unilogin anvendes til et relevant uddannelsesmæssigt formål
>
> Tjenesten:
> - Client ID: [client_id fra udbyderportalen]
> - Redirect URI: https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/broker/unilogin/endpoint
> - Post logout redirect URI: https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/broker/unilogin/endpoint/logout_response
> - Aktørgruppe: medarbejder
> - PKCE (S256) og fortrolig klient (client secret)
>
> Med venlig hilsen
> Niels Pilgaard Grøndahl
> Skoleoverblikket, CVR 41249269

**Institution/virksomhed – øvrige**: company name as in CVR.

## 4. Values for udbyderportal.stil.dk (Tjenester tab)

| Field | Value |
|---|---|
| Redirect URI (prod) | `https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/broker/unilogin/endpoint` |
| Post logout redirect URI (prod) | `https://auth.skoleoverblikket.dk/realms/Skoleoverblikket/broker/unilogin/endpoint/logout_response` |
| Redirect URI (local dev) | `http://localhost:8080/realms/Skoleoverblikket/broker/unilogin/endpoint` — only if the portal allows localhost for test services |
| Aktørgruppe | Medarbejder |
| Sikringsniveau | Low (`https://data.gov.dk/concept/core/nsis/loa/Low`) — password-only Unilogin works, two-factor used when the user has it |

The portal issues `client_id` and `client_secret`. They go into config, never into git.

## Broker endpoints

| Env | Discovery |
|---|---|
| Test (ET) | `https://et-broker.unilogin.dk/auth/realms/broker/.well-known/openid-configuration` |
| Prod | `https://broker.unilogin.dk/auth/realms/broker/.well-known/openid-configuration` |

Source: [Implementering af tjeneste](https://viden.stil.dk/display/OFFSKOLELOGIN/Implementering+af+tjeneste).

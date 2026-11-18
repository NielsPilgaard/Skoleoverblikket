---
title: 'Content AI assistants can quote'
purpose: 'Checklist for public pages that answer the questions schools ask AI assistants when choosing admin software.'
description: >-
  Adds a /priser page, one page per feature, a FAQ and fair comparison pages,
  all prerendered with JSON-LD and Markdown mirrors. Moves prices into one
  content module. Niels approves copy and supplies competitor facts.
status: 'Ready'
---

# Content AI assistants can quote

## TL;DR

- Assistants cite pages that state facts plainly: price, what's included, GDPR, how to switch. Today the site has a single landing page with one or two sentences per feature, so there is little for them to quote.
- Depends on [75](75-agent-readable-site.md) Phase 2: new pages go into the route table and get Markdown mirrors, llms.txt entries and sitemap entries automatically.
- Size M. Owner: Niels + agent. An agent drafts the pages; Niels approves all copy and supplies the competitor facts (Phase 4).
- All copy is in Danish and written for Hanne: short sentences, no jargon, concrete numbers.

## Phase 1: Prices in one place, /priser (S)

- [ ] Move the `*_PRICE_KR` constants from [LandingPage.tsx](../web/src/pages/LandingPage.tsx) into `web/src/content/pricing.ts`. The landing page, `/priser`, the JSON-LD and llms.txt all read from it.
- [ ] `/priser` shows Basis and each module, monthly and yearly, what's included (from [docs/PRICING.md](../docs/PRICING.md)), the trial, how to cancel, and that there is no price per student.
- [ ] JSON-LD: `SoftwareApplication` with one `Offer` per plan or module (`price`, `priceCurrency: DKK`, `priceSpecification` with billing period).
- [ ] Add a test that fails when the Stripe price IDs configured for dev don't match `pricing.ts`, if that can be checked without calling Stripe. Otherwise add a comment in `pricing.ts` naming the Stripe config to keep in step.

## Phase 2: One page per feature (M)

- [ ] `/funktioner/<slug>` for each entry in [features.tsx](../web/src/content/features.tsx). Extend the type with `slug` and optional `body` (a few paragraphs on what problem it solves, who uses it, and which module it belongs to). The landing page cards link to the pages.
- [ ] Each page has one `h1`, a screenshot from `web/public/media/` with `width` and `height`, and a link to `/signup`.
- [ ] Entries without `body` don't get a page yet (no thin pages). The `/nyheder` workflow ([changelog.yml](../.github/workflows/changelog.yml)) keeps adding cards only.
- [ ] Add a breadcrumb in JSON-LD (`BreadcrumbList`).

## Phase 3: FAQ (S)

- [ ] `/spoergsmaal` with the questions schools actually ask. Niels supplies or confirms the list. Start with: where data is stored (EU, link to `/underdatabehandlere`), the data processing agreement (databehandleraftale), moving from an existing timetable system, Aula, the trial and what happens after it, cancelling, phone use, how many staff can log in.
- [ ] `FAQPage` JSON-LD. Google only shows FAQ rich results for a few sites, but assistants and other crawlers read the markup.

## Phase 4: Comparison pages (S, needs Niels)

- [ ] Niels lists the competitors to cover (see [docs/PRD.md](../docs/PRD.md) competition section) and confirms each fact. No claim about a competitor goes live unconfirmed.
- [ ] One page per competitor (`/alternativ-til-<navn>`), in a factual tone: what they do well, where Skoleoverblikket differs (price, simplicity, one system), and how to switch. Include a "sidst opdateret" date in the copy and in `lastModified`.

## Done

- [ ] All new pages are in the route table, prerendered, in `llms.txt` and the sitemap, and pass the agent-ready.dev page checks.
- [ ] `/verify` and `/test` pass. Add an e2e smoke test that `/priser` shows the price from `pricing.ts`.

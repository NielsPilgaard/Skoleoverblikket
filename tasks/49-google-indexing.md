---
title: 'Get skoleoverblikket.dk indexed on Google'
status: 'In progress'
purpose: 'Checklist of the remaining steps to get skoleoverblikket.dk indexed and ranking for its own name.'
description: >-
  Google has not indexed skoleoverblikket.dk. The code fixes (prerendering, one canonical per
  page, no Keycloak redirect on anonymous visits) are written but not yet merged to `main` or
  deployed. What remains is merging and deploying them, checking login in production, and the manual Search Console, redirect and backlink work.
---

# Get skoleoverblikket.dk indexed on Google

## TL;DR

The code fixes are written on `feature/tasks-and-seo` but not yet merged or deployed. Merge and deploy them first, then verify login on prod, then
do Search Console. Everything after that is links and waiting.

## 1. Ship the code fixes (today)

- [ ] Review the diff in `web/`: `index.html`, `nginx.conf`, `package.json`, `src/App.tsx`,
      `src/main.tsx`, `src/PublicRoutes.tsx`, `src/entry-server.tsx`, `src/auth/AuthProvider.tsx`,
      `src/auth/keycloak.ts`, `src/components/SeoMeta.tsx`, `src/pages/LandingPage.tsx`,
      `public/silent-check-sso.html`, `scripts/prerender.mjs`.
      Leave out the unrelated docs/adr and nightly workflow changes.
- [ ] Merge to `main` and deploy.
- [ ] **Check login on prod** (the login check now uses a hidden iframe instead of a page redirect):
  - [ ] Log in, then open `https://skoleoverblikket.dk/` and confirm you are sent to the dashboard.
  - [ ] Open a deep link, e.g. `/klasser`, in a new tab while logged in and confirm it loads.
  - [ ] Log out, reload `/` and confirm the landing page shows with no hop to `auth.skoleoverblikket.dk`.
  - [ ] Repeat the first check in Safari or an iPhone. If you then appear logged out on `/`,
        clicking "Log ind" should log you straight in without a password.
- [ ] **Check the HTML Google sees**: `curl -s https://skoleoverblikket.dk/om | grep -E "canonical|<h1|<title"`.
      You should see exactly one canonical (`/om`), a title and an `<h1>`.
      Repeat for `/`, `/kontakt` and `/privatlivspolitik`.

## 2. Tell Google the site exists (after deploy, about 30 min)

- [ ] Google Search Console: add `skoleoverblikket.dk` as a **Domain** property.
      Verify it with the DNS TXT record at GoDaddy.
- [ ] Submit `https://skoleoverblikket.dk/sitemap.xml`.
- [ ] Use URL Inspection on `https://skoleoverblikket.dk/`, run "Test live URL", check the
      rendered HTML has the page text, then click "Request indexing". Do the same for `/om` and `/kontakt`.
- [ ] skoleplanen.dk: in GoDaddy, check that forwarding is a **301 permanent** redirect, not a
      302 or masked forwarding. Then add skoleplanen.dk as a property in Search Console and run
      Change of Address to skoleoverblikket.dk.
- [ ] Bing Webmaster Tools: import the setup from Search Console.

## 3. Small content fixes (this week)

- [x] Landing page `<h1>` keeps the headline without "Skoleoverblikket". A brand line above it
      looked wrong, and the name is already in the `<title>`, JSON-LD and nav.
- [x] `priceValidUntil` in `web/src/pages/LandingPage.tsx` is computed at build time as 31 Dec
      next year, so it rolls forward on every deploy.
- [x] `/signup` is indexable: `noindex` removed, `SeoMeta` added, listed in `web/public/sitemap.xml`.
      It is not prerendered (Google renders it with JavaScript), so check it with URL Inspection
      after deploy.

## 4. Get links pointing to the site (ongoing)

- [ ] GitHub repo NielsPilgaard/Skoleoverblikket: set the About website field to
      `https://skoleoverblikket.dk`. This repo currently ranks for the brand name.
- [ ] Decide whether the repo should be public. It contains the Keycloak realm and infrastructure
      config. If it stays public, check that no secrets are committed.
- [ ] Create a LinkedIn company page that links to the site.
- [ ] Google Business Profile: only if Skoleoverblikket makes in-person contact with customers
      during stated hours (e.g. a staffed address schools can visit). Online-only businesses are
      not eligible per Google's [eligibility guidelines](https://support.google.com/business/answer/3038177);
      otherwise skip this step.
- [ ] Ask Danmarks Friskoleforening and Danmarks Private Skoler about supplier listings.
- [ ] Ask existing customer schools to link to the site, for example from their own "systemer vi
      bruger" or parent info page.

## 5. Check that it worked

- [ ] Search `site:skoleoverblikket.dk` every few days. Indexing usually takes from a few days to a couple of weeks.
- [ ] Search Console → Pages report: look for pages listed as "Excluded" or
      "Duplicate, Google chose different canonical" and fix what shows up.
- [ ] Once indexed, search for "skoleoverblikket" and confirm the site ranks above the GitHub repo.

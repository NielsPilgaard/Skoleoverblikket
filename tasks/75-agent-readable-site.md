---
title: 'Agent-readable marketing site'
purpose: 'Checklist for making the public site readable and usable by AI agents and crawlers, driven by the 2026-11-18 agent-ready.dev scan.'
description: >-
  Serves real files instead of the SPA shell for llms.txt, Markdown mirrors,
  sitemap.md and unknown well-known paths; adds Markdown content negotiation,
  sitemap lastmod, noindex on app routes, a <main> landmark and form fixes so
  browser agents can sign up and book a demo. Public pages only: no agent
  access to tenant data (no MCP server, decided 2026-11-18).
status: 'Ready'
---

# Agent-readable marketing site

## TL;DR

- AI assistants (ChatGPT, Claude, Perplexity) answer "skemaprogram til friskole" from pages they can read cheaply. Today, every unknown path returns `200` with the SPA shell, so `/llms.txt`, `/.well-known/*` and `/openapi.json` all look like they exist but contain HTML.
- Four phases, one PR each, in order. Size M in total. Owner: Agent. No API changes, no migrations.
- Done when a new scan at [agent-ready.dev](https://agent-ready.dev/scan/pNhrWyyKng) passes every check not marked skip below, the [agentready.md](https://agentready.md/tools) Markdown negotiation tester finds all four discovery mechanisms, and `/verify` and `/test` pass.
- Content negotiation follows the [acceptmarkdown.com guides](https://acceptmarkdown.com/guides). The canonical URL is the main path, because agents request the URL they already know. `.md` siblings and `llms.txt` come second.
- Out of scope: MCP server, A2A/UCP/ACP/agents.json and other protocol cards, a public OpenAPI spec. They would expose tenant data or an internal API with no stable contract. They must return `404`, not the shell.

## Scan checks: do or skip

| Scan check | Phase | Action |
|---|---|---|
| S1–S4, L1–L10 llms.txt, llms-full.txt | 2 | Generate both at build |
| S9 sitemap lastmod, S10–S11 sitemap.md | 2 | Generate sitemap.xml and sitemap.md from the route table |
| P15–P20 Markdown mirror, frontmatter, alternate link, canonical Link header, negotiation, `## Sitemap` | 2 | Generate a `.md` per prerendered page, nginx negotiation |
| P24 `rel="describedby"` to llms.txt | 2 | Head link on every prerendered page |
| P25 snippet directives | 1 | `max-snippet:-1, max-image-preview:large` on marketing pages |
| C1–C21, S15 protocol cards, OpenAPI | 1 | Real `404` (skip the feature itself) |
| A4 image dimensions, A12 `<main>`, A10 title | 3 | Fix |
| `/signup` 9/25 | 3 | Prerender it |
| agentready.md: 4 Markdown discovery mechanisms (negotiation, `.md` URL, `<link>` tag, `Link` header) | 2 | All four |
| agentready.md: robots.txt Content-Signal | 1 | Add to robots.txt |
| agentready.md: schema validator | [76](76-agent-quotable-content.md) | JSON-LD for prices, FAQ, breadcrumbs |
| S12–S13 site AGENTS.md | — | Skip: no settled spec, and llms.txt covers the same need. Revisit if one emerges. |
| P14 glossary link | — | Skip: the domain words are explained on the feature pages ([76](76-agent-quotable-content.md)) |
| `x-markdown-tokens`, `variant=GFM` | — | Skip: not standard, and no client needs them |
| agentready.md: OAuth discovery, MCP and A2A card generators | — | Skip: no agent access to tenant data |

## Phase 1: Stop the soft 404s (S)

[nginx.conf](../web/nginx.conf) falls back to `/index.html` for every path. App routes need that; files don't.

- [ ] Paths under `/.well-known/` and paths ending in a file extension (`.json`, `.txt`, `.md`, `.xml`, images, etc.) are served with `try_files $uri =404`. Keep `/env.js`, `/assets/*`, `/silent-check-sso.html` and `/media/*` working. Check for a Stripe or Apple domain-verification file under `/.well-known/` before blocking it.
- [ ] `index.html` (the app shell) gets `<meta name="robots" content="noindex">`. [prerender.mjs](../web/scripts/prerender.mjs) strips it, and `SeoMeta` adds `max-snippet:-1, max-image-preview:large` to marketing pages instead. Today every app route is an indexable duplicate of the landing page's title.
- [ ] [robots.txt](../web/public/robots.txt): add `Disallow: /api/`. Keep allowing all AI bots. Add `Content-Signal: search=yes, ai-input=yes, ai-train=yes` to the `User-agent: *` group (Cloudflare's Content Signals Policy). `ai-train=yes` was decided by Niels on 2026-11-18: the copy is marketing that should spread.
- [ ] **Gotcha:** a location that sets its own `add_header` drops the server-level security headers. Repeat them, or move them to an `include` file.
- [ ] Integration check (curl script in CI or a Playwright request test): `/.well-known/mcp.json`, `/openapi.json` and `/nope.txt` return `404`; `/om`, `/app` and `/env.js` return `200`.

## Phase 2: llms.txt, Markdown mirrors, sitemaps (M)

Everything is generated at build time from the prerendered HTML, so it can't drift from the site.

- [ ] Route table: replace the `PRERENDERED_PATHS` array in [PublicRoutes.tsx](../web/src/PublicRoutes.tsx) with entries of `{ path, lastModified, changefreq, priority }`. `/` and `/nyheder` take the newest entry date from [changelog.ts](../web/src/content/changelog.ts). Others take a hand-set date that a test requires.
- [ ] Wrap each public page's content in `<main>` (also fixes A12). `prerender.mjs` converts `<main>` to Markdown with turndown. Each page is written as `dist/md/<route>.md` (`/` → `dist/md/index.md`) with:
  - frontmatter: `title`, `description`, `url` (canonical HTML URL), `lastmod`, `lang: da`
  - a closing `## Sitemap` section that links every page's `.md`.
- [ ] Markdown endpoints: `/om.md` and `/index.md` serve the file. A request to `/om` that prefers `text/markdown` serves the same file at the same URL. Headers: `Content-Type: text/markdown; charset=utf-8` (never `text/x-markdown` or `application/markdown`), `Vary: Accept`, `Link: <https://skoleoverblikket.dk/om>; rel="canonical"`. nginx has no `md` MIME type. Use `default_type` in that location; a `types {}` block replaces the whole MIME map.
- [ ] Parse `Accept` properly, not by substring. A regex `map` on `text/markdown` gets `text/markdown;q=0` and `text/html, text/markdown;q=0.5` wrong. Use njs (it ships in `nginx:alpine`). It needs `load_module modules/ngx_http_js_module.so;` in the main context, so the Dockerfile must replace `/etc/nginx/nginx.conf`, not only `conf.d/default.conf`. Rules ([accept-parsing](https://acceptmarkdown.com/guides/accept-parsing)):
  - Score `text/html` and `text/markdown` by their best-matching entry: an exact type beats `text/*`, which beats `*/*`. The score is that entry's q-value (default 1), and `q=0` means "never".
  - Markdown wins only if it scores strictly higher. A tie, a missing `Accept` header, `*/*` or only `text/plain` all get HTML.
  - Never return `406`. If nothing matches, serve HTML: it's allowed, and better for users than an error ([returning-406](https://acceptmarkdown.com/guides/returning-406)).
- [ ] Only prerendered routes negotiate. App routes always return the SPA shell.
- [ ] HTML pages advertise the mirror both ways:
  - headers: `Link: </om.md>; rel="alternate"; type="text/markdown"` (njs makes this per path) and `Vary: Accept`
  - head: `<link rel="alternate" type="text/markdown" href="/om.md">` and `<link rel="describedby" href="/llms.txt" type="text/plain">` (from `SeoMeta` or `prerender.mjs`)
- [ ] Caching ([caching-cdn](https://acceptmarkdown.com/guides/caching-cdn)): check whether anything caches in front of nginx (Traefik in Dokploy, any CDN). If it does, key the cache on a normalized `html`/`md` bucket, not the raw `Accept` header, and confirm that `Vary` isn't stripped.
- [ ] Test cases in the smoke suite ([tests/smoke/](../web/tests/smoke/)), so they run against the real nginx image after each deploy. For `/om`:

  | Accept | Expect |
  |---|---|
  | (none) | HTML |
  | Chrome's default header | HTML |
  | `text/markdown` | Markdown |
  | `text/markdown, text/html;q=0.8` | Markdown |
  | `text/html, text/markdown;q=0.5` | HTML |
  | `text/markdown;q=0, */*` | HTML |
  | `text/plain` | HTML |
  | `application/pdf` | HTML (no 406) |

  Every response has `Vary: Accept`, and the HTML response has the `rel="alternate"` `Link` header.
- [ ] `/llms.txt` follows [llmstxt.org](https://llmstxt.org): `# Skoleoverblikket`, a `>` summary (what it is, for whom, price from 300 kr./md., 30-day trial, data in the EU), then `##` sections with `- [Title](url.md): note` lists. Sections: Produkt, Priser og prøveperiode, Jura og databehandling, Kontakt. Put `/nyheder` under `## Optional`. Write it in Danish and build it from [features.tsx](../web/src/content/features.tsx), the price constants and the route table. Keep any hand-written prose in a template in `scripts/`.
- [ ] `/llms-full.txt` contains all page Markdown concatenated, each page under its own `#` heading with its URL.
- [ ] `.txt` is served with `charset=utf-8` (`charset utf-8;` in nginx), or æøå break.
- [ ] Generate `sitemap.xml` (with `lastmod`) and `sitemap.md` (`# Sitemap`, one `##` per section, links to the `.md` pages) from the route table. Delete the hand-written [sitemap.xml](../web/public/sitemap.xml).
- [ ] `prerender.mjs` fails the build if a prerendered route has no `.md`, an `.md` file has no frontmatter, or `llms.txt` links a path that doesn't exist.

## Phase 3: Pages and forms browser agents can use (S)

Browser agents (ChatGPT agent, Claude in Chrome) read the accessibility tree, as screen readers do.

- [ ] Prerender `/signup` (it's in the sitemap but returns the empty shell, so it scored 9/25). If it can't render without Keycloak, split the static intro from the form.
- [ ] Give the landing page images `width` and `height` (A4).
- [ ] A10 reported no `<title>` although the static HTML has one. Check in a real browser whether hydration removes the title Helmet didn't create, and fix it if so.
- [ ] `/signup`, `/book-demo` and `/kontakt`: every field has a `<label>`, the right `autocomplete` token (`name`, `email`, `tel`, `organization`) and the right `type`. Validation errors are shown as text tied to the field (`aria-describedby`), not only as color. The submit button has a verb label. No CAPTCHA.
- [ ] Extend the existing e2e tests for signup and book-demo to locate fields by role and label once (`getByLabel`). That fails if a label disappears. Note this as an exception to the `data-testid` rule in the test file.

## Phase 4: Verify (S)

- [ ] Run the agent-ready.dev scan again. Every check marked Phase 1–3 in the table passes.
- [ ] `curl -H 'Accept: text/markdown' https://skoleoverblikket.dk/` returns Markdown.
- [ ] [agentready.md tools](https://agentready.md/tools): the Markdown negotiation tester finds all four mechanisms, the llms.txt checker passes, the robots.txt checker sees Content-Signal, and the AI crawlers checker reports no blocked bots on marketing pages.
- [ ] Add a short "Agent-readable site" section to [docs/FEATURES.md](../docs/FEATURES.md) naming `prerender.mjs`, the route table and the nginx locations, so the next page added to the route table automatically gets a Markdown mirror and an llms.txt entry.

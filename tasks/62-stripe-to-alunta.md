---
title: 'Move billing from Stripe to Alunta'
purpose: 'Decide whether to replace Stripe Billing with Alunta (Danish subscription billing), and if so, how to move the code and the existing subscriptions.'
description: >-
  Alunta is a Danish subscription-billing service hosted at Hetzner in Germany.
  It is not a payment processor: cards still go through a gateway (Stripe,
  OnPay or QuickPay), but it adds what Danish schools ask for and Stripe lacks:
  invoice with bank transfer, EAN/NemHandel e-invoices, Betalingsservice,
  Leverandørservice and MobilePay. Feasible: the API covers checkout, portal,
  trials, interval change, cancel and signed webhooks. "Fully EU" holds for
  hosting and data, not for every sub-processor. Needs an ADR superseding
  stripe-checkout-billing before any code.
status: 'Needs decision'
---

# Move billing from Stripe to Alunta

## Status (2026-10-27)

**Investigation done, no code.** Testing runs against a separate Alunta test account (account menu → "Opret testkonto", API token under Indstillinger → API, webhook receiver set up there). Test-account webhooks carry `test_mode: true`. There is no local emulator like the Stripe CLI, so a local receiver needs a public HTTPS address. The webhook log shows deliveries and can retry failed ones.

Alunta (Oliver Lindebod, CEO) answered the open questions on 2026-10-27: the four webhook events exist, D2 (a) is their recommended model with one consolidated invoice, and the contract party is Langsom ApS. See [Answers from Alunta](#answers-from-alunta).

Next step: Niels decides D1, D3 and D4 and picks the renewal setting (Phase 0), then an agent writes the ADR and builds Phase 1 against the test account.

## TL;DR

- **Feasible.** Alunta's REST API ([docs](https://app.alunta.com/docs/v1), [OpenAPI](https://app.alunta.com/docs/v1/openapi.yaml)) has everything [SubscriptionService.cs](../api/Skoleoverblikket.Api/Services/SubscriptionService.cs) uses Stripe for: checkout sessions, a hosted customer portal, trials, interval change, cancel/resume, and HMAC-signed webhooks with retries and replay.
- **Main gain**: schools can pay the way Danish schools pay, by invoice (bank transfer or EAN/NemHandel), Betalingsservice or MobilePay, with no card needed. VAT, reminders (rykkere) and bookkeeping sync (e-conomic, Dinero, Billy) come built in.
- **Not a processor.** Card payments still need a gateway. Fully EU-based cards means OnPay or QuickPay (both Danish) with their own acquiring agreement, instead of Stripe.
- **EU verdict**: data and hosting are EU (Hetzner, Germany; Mailgun EU region). A few sub-processors are US or Canadian companies (Slack, Mailgun, Google Fonts, Scalar), covered by DPF/SCCs. That's EU-hosted, but not 100 % EU vendors.
- **Cost**: Alunta is 0 kr up to 10 customers / 10,000 kr MRR, then 199 kr/month up to 25 customers. Gateway fees come on top if cards are kept.

## Is it EU-based?

Sources: [DPA](https://app.alunta.com/databehandleraftale), [FAQ](https://alunta.com/da/faq/), retrieved 2026-10-26.

| Part | Where | EU? |
|---|---|---|
| Servers, database, Redis, backups, files | Hetzner Online GmbH, Germany | Yes |
| Email incl. invoices | Mailgun, EU region (Germany, Belgium). US company | Hosted in EU. US parent, DPF + SCCs |
| Internal ops and error messages (may include customer names) | Slack, USA | No. DPF + SCCs |
| Checkout and portal fonts (visitor IP) | Google Fonts (Ireland/USA), Bunny Fonts (Slovenia) | Partly |
| API docs "try it" | Scalar, Canada | No, only when *we* test calls in their docs |
| CVR lookup, VAT check, IP → country | cvr.dev (AWS Frankfurt), Everapi (Austria), ip-api (Romania) | Yes |
| EAN/NemHandel, Leverandørservice, MobilePay, open banking | Sproom (DK), Mastercard Payment Services DK, Vipps MobilePay (NO), Enable Banking (FI) | Yes (EEA), and only when the feature is used |
| Card gateway | Our choice: Stripe (IE entity, US parent), OnPay (DK), QuickPay (DK) | Depends on D1 |

What Alunta processes is **our** customer data: the school as a paying customer (name, CVR, address, billing contact email). No student or parent data goes there. The billing contact is `School.ContactEmail`, which signup fills with the first admin's email ([SchoolSignupService.cs](../api/Skoleoverblikket.Api/Services/SchoolSignupService.cs)), so it is usually a named staff member's work email. Stripe receives the same field today. We process it as the customer's billing contact, not as school staff data, so like Stripe today ([dataProcessing.ts](../web/src/content/dataProcessing.ts) comment), it belongs in the privacy policy (which must name the billing contact's email as data sent to Alunta), not in the school DPA's sub-processor list. That makes the US sub-processors a smaller issue than they would be for school data.

**Contract party**: Langsom ApS, CVR 33643284, with secondary names (binavne) Alunta ApS and Boligforeningsweb ApS. The website footer (*Langsom.com ApS*) and the DPA (*Boligforeningsweb ApS*) name the same company under different names (confirmed by Alunta 2026-10-27). The privacy policy names it as "Langsom ApS (Alunta), CVR 33643284". **Check before signing**: the terms are a Google Doc linked from [alunta.com/da/betingelser](https://alunta.com/da/betingelser/). Read them for termination, data export and price-change notice.

## Fit with what we have

Current Stripe surface: [SubscriptionService.cs](../api/Skoleoverblikket.Api/Services/SubscriptionService.cs) (784 lines), [BillingController.cs](../api/Skoleoverblikket.Api/Controllers/BillingController.cs), [StripeWebhookController.cs](../api/Skoleoverblikket.Api/Controllers/StripeWebhookController.cs), [StripeExtensions.cs](../api/Skoleoverblikket.Api/Services/StripeExtensions.cs), `StripeOptions` + validator, [Subscription.cs](../api/Skoleoverblikket.Api/Models/Subscription.cs) / [SubscriptionModuleItem.cs](../api/Skoleoverblikket.Api/Models/SubscriptionModuleItem.cs) (Stripe ids), [BillingPage.tsx](../web/src/pages/BillingPage.tsx), and Stripe mentions in the privacy policy, landing page, backoffice, `SchoolDeletionService` and `SchoolExportService`.

| We use (Stripe) | Alunta equivalent | Notes |
|---|---|---|
| Checkout Session, `school_id` metadata | `POST /checkout-sessions` with `external_customer_id` = SchoolId, custom `metadata` echoed in `checkout.completed` | URL expires after 24 h, same as now. Pre-fill name, email, CVR (`reg_number`), `preferred_interval` (1 or 12) |
| Billing portal session | `POST /portal-link/{customerUuid}`: signed auto-login URL, 15 min default | Portal handles cards, invoices, plan change, cancel (cancel can be turned off) |
| Base price + module prices as items on one subscription | No add-on items on a flat plan. See D2 | Main design question |
| Interval switch (immediate, prorated) | `PUT /subscriptions/{uuid}/interval`: takes effect **next period** | Behaviour change. UI text must say "fra næste periode" |
| Add/remove module (immediate, prorated) | Separate subscription per module on the same customer (D2). A new module gets a prorated stub period, then renews with the customer's other subscriptions. Several at once: `POST /customers/{uuid}/subscription-bundles` | Needs fixed renewal, or flexible renewal with "Forsøg at gruppere abonnementer" (Phase 0) |
| Trial | App-side today ([free-trial](../docs/adr/free-trial.md)), no card. Keep it app-side; create checkout with `trial_enabled: false` | |
| Deferred first charge during dated free access (cap 730 days) | Checkout `trial_value` in days, **max 365** | Lower the cap to 365, or use `POST /subscriptions` with `start_date` |
| `PastDue` / `Unpaid` from invoice events | Statuses are only `pending`, `active`, `under_cancellation`, `cancelled`. Use `subscription.payment_failed` / `invoice.payment_failed` → `PastDue`, `subscription.payment_succeeded` / `invoice.paid` → `Active` | Alunta sends reminders (rykkere) itself |
| Cancel → `CanceledAt` (90-day retention clock) | `subscription.ended` (`reason`: cancellation, payment_failure, …) → `Canceled` | `subscription.cancelled` only means "under cancellation", so don't start the clock there |
| Webhook signature (`Stripe-Signature`) | `Signature` header = hex HMAC-SHA256 of the raw body with the webhook secret | Constant-time compare (`CryptographicOperations.FixedTimeEquals`) |
| Idempotency | At-least-once delivery. Dedupe on the stable `data.event_id` (Alunta confirmed it for `subscription.payment_succeeded` and `.ended`; docs also list `invoice.paid`, `.started`, `checkout.completed`). `subscription.payment_failed` has none: dedupe on `webhook_uuid`, so each payment retry attempt is its own event | Respond 2xx within **3 s**; 8 attempts over ~44 h; replay via `POST /webhooks/replay/{uuid}` |
| Test vs live | Separate test account, payloads have `test_mode` | Reject a payload whose `test_mode` doesn't match config, so a test account can never change prod data |

Rate limits (120 reads + 600 writes per minute, max 12 concurrent requests per user) are far above what a few hundred schools need. Handle `429` with `Retry-After` anyway.

## Gains and losses

**Gains**

- Invoice payment by bank transfer and EAN/NemHandel. Many school bookkeepers can't, or won't, put a card on file. A folkeskole (the secondary market) must receive EAN invoices by law.
- Betalingsservice, Leverandørservice and MobilePay without our own integrations.
- Danish invoices with VAT, credit notes, reminders and bookkeeping sync. Today those are Stripe invoices.
- Danish vendor and Danish support, matching the vendor preference.
- With OnPay or QuickPay, billing has no US company in the payment path, which reads better to a friskole board.

**Losses and risks**

- **Small vendor.** If Alunta shuts down, billing has to move again. Mitigation: we keep our own `Subscription` row as the source of truth for access, as now, so a move only touches `SubscriptionService`.
- **Card gateway setup**: OnPay and QuickPay need an acquirer agreement (e.g. Clearhaus), which means KYC and paperwork that Stripe bundles in.
- **Bank-transfer reconciliation**: invoices paid by bank transfer are only marked paid after reconciliation (open banking via Enable Banking, file import, accounting sync or by hand). Until then `invoice.paid` doesn't fire. Access must not depend on that event, or a school that pays on day 7 of 8 gets locked out. See D3.
- **No local emulator**: dev needs a test account plus an HTTPS dev tunnel, unlike `stripe listen` (see [Local dev](#local-dev-https-dev-tunnel)).
- **Docs and ADR churn**: AGENTS.md ("Stripe Checkout only… no manual invoicing, no MobilePay"), [stripe-checkout-billing](../docs/adr/stripe-checkout-billing.md), [PRICING.md](../docs/PRICING.md), [PRD.md](../docs/PRD.md), [DEPLOYMENT.md](../docs/DEPLOYMENT.md) and [STRIPE_LOCAL.md](../docs/STRIPE_LOCAL.md) all assume Stripe.

"No manual invoicing" still holds: Alunta sends invoices automatically, so Niels does nothing per invoice. The ADR rejected *manual* invoicing, not invoices.

## Decisions for Niels

- **D1 — Card gateway.** (a) Keep Stripe as gateway inside Alunta. Existing cards move without re-entry (`POST /customers/{uuid}/payment-methods` with the `pm_…` id) only if Alunta is connected to **our current** Stripe account, since `pm_…` ids are only valid in the account that created them. If Alunta needs its own Stripe account, schools re-enter cards as in (b), or we ask Stripe for a payment-method migration between accounts. Stripe stays in the payment path either way. (b) OnPay or QuickPay. Fully Danish, needs an acquirer agreement, and every card-paying school re-enters its card once in the Alunta portal. (c) No cards: invoice, EAN, Betalingsservice and MobilePay only. Simplest and fully EU, but loses instant card checkout. *Recommendation: (b) if the acquirer paperwork is quick, else (a) first and (b) later. The gateway is a setting in Alunta, not code.*
- **D2 — Modules. Settled: (a).** One Alunta subscription per module on the same customer, mapping 1:1 to `SubscriptionModuleItem`. Alunta recommends this model. All periods due for the same customer in the same currency go on one invoice automatically, and a new module gets a prorated stub period, then follows the other subscriptions' renewal date (with the renewal setting from Phase 0). Rejected: (b) one plan per combination, since plan changes only take effect at renewal and adding a module would wait up to a year.
- **D3 — When does a school count as paid?** Card: at `checkout.completed`, as now. Invoice/EAN: also at `checkout.completed` (the school has committed and the invoice is sent), then `PastDue` only when Alunta reports a failed or overdue payment. *Recommendation: grant access on checkout. Locking a school out while its invoice is in a bookkeeper's inbox would block Hanne.*
- **D4 — Existing Stripe subscriptions.** Count the live ones first. With only a handful, use a one-off migration script: create the Alunta customer, attach the card (D1a, only with our current Stripe account connected to Alunta, see D1) or ask the school to add a payment method (D1b), then `POST /subscriptions` with `start_date` = the Stripe renewal date and `apply_plan_trial: false`, `apply_plan_intro_offer: false`. After that, cancel the Stripe subscription at period end. Alunta offers free migration help. *Recommendation: script it, but run it by hand per school with Niels watching.*

## Plan

### Phase 0 — Decide (Niels)

1. Create the Alunta test account, API token and webhook secret.
2. Pick the renewal setting in the Alunta account: fixed renewal, or flexible renewal with "Forsøg at gruppere abonnementer" turned on. Either gives base and module subscriptions one renewal date. Then, in the test account, start a base subscription plus a module and check with `GET /customers/{uuid}/upcoming-invoices` that they become one invoice.
3. Decide D1, D3 and D4 (D2 is settled). Write an ADR (`docs/adr/alunta-billing.md`) that supersedes [stripe-checkout-billing](../docs/adr/stripe-checkout-billing.md), and update [adr/INDEX.md](../docs/adr/INDEX.md) and the AGENTS.md billing line in the same PR.

### Phase 1 — Build against the test account (agent)

- `AluntaOptions` + validator (`ApiToken`, `WebhookSecret`, `TestMode`, plan and renewal-interval UUIDs per module and interval), replacing `StripeOptions`.
- `IAluntaClient` + typed `HttpClient` implementation: only the calls we use (checkout session, portal link, interval, cancel, resume, create/cancel module subscription, subscription bundle for several modules at once). The interface is allowed because tests replace an external system, as with `IEmailSender`. Use the `Idempotency-Key` header on subscription creation.
- `AluntaWebhookController` (anonymous, `api/v1/webhooks/alunta`): reads the raw body, checks `Signature`, rejects a `test_mode` mismatch, hands the parsed event to `SubscriptionService.HandleAluntaEventAsync` and returns 200 fast.
  - **Dedupe**: a `ProcessedWebhookEvent` table (`Provider`, `EventId`, `ProcessedAt`, unique on `Provider` + `EventId`). `EventId` is `data.event_id` where the event has one, else `webhook_uuid` (`subscription.payment_failed`). `HandleAluntaEventAsync` claims the event by inserting that row in the same transaction as the subscription change; a unique-key violation means already processed, so it returns without applying. Not a last-event-id column: deliveries can arrive out of order and replays resend old events, which a single id can't recognise.
  - **Payment ordering**: dedupe doesn't stop a delayed `payment_failed` (on retry since ~44 h) from overriding a newer `payment_succeeded` and flipping a paid school to `PastDue`. Store the time of the last applied payment event on `Subscription` and skip older ones. Check which timestamp the payload carries.
  - **School resolution**: never from echoed `metadata` or `external_customer_id` alone. When creating a checkout session, store its Alunta session UUID on the school's `Subscription` (`AluntaCheckoutSessionId`). `checkout.completed` finds the `Subscription` by that UUID and stores `AluntaCustomerId` / `AluntaSubscriptionId`. Every later event finds it by those ids (indexed, like the Stripe ids). An event matching no stored id is logged and acknowledged, not applied. `Subscription` and `SubscriptionModuleItem` have no tenant query filter, so this needs no `ITenantContext`. If a handler later touches tenant-scoped data (e.g. notifying admins), set the tenant from the resolved `SchoolId` first.
- `SubscriptionService`: add `IAluntaClient` next to the Stripe calls, keeping public method names, so `BillingController` barely changes. Event mapping is in the table above. **Provider routing**: a subscription is Alunta-backed when `AluntaSubscriptionId` is set, Stripe-backed when `StripeSubscriptionId` is set (the two are never both set: D4 clears the Stripe id when it moves a school). Portal, interval, cancel/resume and module changes go to that provider. New checkouts go to Alunta once Phase 2 is live (config switch), to Stripe before that. The Stripe branch is removed in Phase 3.
- Migration (`/add-migration`): `AluntaCheckoutSessionId`, `AluntaCustomerId`, `AluntaSubscriptionId` and the last payment event time on `Subscription`, `AluntaSubscriptionId` on `SubscriptionModuleItem`, and the `ProcessedWebhookEvent` table. Keep the Stripe columns until Phase 3.
- [BillingPage.tsx](../web/src/pages/BillingPage.tsx): payment-method wording (kort, faktura, EAN, MobilePay, Betalingsservice), and interval change "fra næste periode". Run `/codegen` after the API change.
- Privacy policy ([PrivacyPolicyPage.tsx](../web/src/pages/PrivacyPolicyPage.tsx)): replace the Stripe paragraph with Alunta + the chosen gateway. Also update the comment in [dataProcessing.ts](../web/src/content/dataProcessing.ts), the landing page and backoffice mentions, and `SchoolExportService` / `SchoolDeletionService` (delete the Alunta customer, or keep it for bookkeeping law: invoices must be kept 5 years).
- Local dev tunnel in the AppHost, below. Build it first: every other Phase 1 item is tested through it.
- Replace [STRIPE_LOCAL.md](../docs/STRIPE_LOCAL.md) with `ALUNTA_LOCAL.md`: test account setup, the dev tunnel and its one-time webhook registration, `POST /webhooks/test` for synthetic events, webhook log replay.

#### Local dev: HTTPS dev tunnel

Alunta's test account must deliver webhooks to the API running under `aspire run`. Alunta only calls public HTTPS URLs, so the AppHost exposes the API through a [Dev Tunnel](https://learn.microsoft.com/azure/developer/dev-tunnels/) and replaces the `stripe-listen` container in [Program.cs](../infrastructure/aspire/Skoleoverblikket.AppHost/Program.cs).

- **Aspire integration**: add `Aspire.Hosting.DevTunnels` (same version as the other Aspire packages, 13.4.3) and `builder.AddDevTunnel("alunta-tunnel", tunnelId).WithReference(api.GetEndpoint("http")).WithAnonymousAccess()`, inside `if (isRun)`. Use anonymous access because Alunta can't log in to the tunnel. Forward to the API's `http` endpoint (port 5000). The tunnel terminates TLS, so local dev certificates don't matter.
- **Stable URL**: a persistent tunnel with a fixed `tunnelId` per developer (Aspire parameter `alunta-tunnel-id` in user secrets) keeps the public URL the same across runs. The webhook in the Alunta test account is then registered once: `https://<tunnel-host>/api/v1/webhooks/alunta`. Pick the West Europe cluster (`euw`).
- **Opt-in**: the tunnel starts only when the Alunta test parameters (`alunta-tunnel-id`, API token, webhook secret) are set. A fresh clone, an agent session or CI never opens a public tunnel. Integration tests post signed payloads to the endpoint directly and need no tunnel.
- **Prerequisite**: `devtunnel` CLI installed and `devtunnel user login` once (Microsoft or GitHub account). Add the CLI to [scripts/setup.ps1](../scripts/setup.ps1) and document the login in `ALUNTA_LOCAL.md`. If not logged in, the resource fails in the Aspire dashboard with the login hint; the rest of the stack still runs.
- **Dashboard**: show the tunnel's public webhook URL on the tunnel resource (`WithUrlForEndpoint`), so it can be copied into the Alunta test account.
- **Exposure**: only the API port goes through the tunnel, not Keycloak, Postgres, Mailpit or the frontend. The webhook endpoint checks the signature and every other endpoint still needs a JWT. Checkout `success_url` / `back_url` stay `http://localhost:5173/...`, since the browser follows those, not Alunta.
- **Vendor note**: Dev Tunnels is a Microsoft service. Only test-account traffic with fake schools goes through it, never personal data, so the EU-first rule isn't affected.

**Acceptance**: with `aspire run` going, (1) `POST /webhooks/test` from the Alunta test account (or "Send test" in its UI) shows 2xx in Alunta's webhook log and a log line in the API, and (2) a checkout in the test account turns the local school `Active` with no manual step, as `stripe listen` does today.

### Phase 2 — Switch new schools (Niels + agent)

Live account, live gateway, Dokploy env vars (`Alunta__*`, see [DEPLOYMENT.md](../docs/DEPLOYMENT.md)), webhook endpoint in Alunta. New checkouts go to Alunta. Existing Stripe subscriptions keep working through the provider routing in `SubscriptionService` (Phase 1), so the Stripe calls and webhook controller stay until Phase 3.

### Phase 3 — Move existing schools and remove Stripe

Run the D4 script per school and confirm each school's next invoice in Alunta. Then delete the Stripe code, packages, options, env vars and columns (new migration), and cancel the Stripe account after the last Stripe invoice period.

## Testing

Per [TESTING.md](../docs/TESTING.md): integration tests through HTTP endpoints, with a fake `IAluntaClient` and webhook payloads signed with a test secret, posted to the webhook endpoint.

- Checkout → `checkout.completed` → school is `Active`, writes allowed.
- Bad signature → 401, nothing changes. `test_mode` mismatch → ignored.
- Same event twice → applied once. An older, already processed event replayed after a newer one → ignored.
- Event whose ids match no stored subscription → 200, nothing changes.
- Stripe-backed subscription: portal, interval and cancel still call Stripe, not Alunta.
- `subscription.payment_failed` → `PastDue`. `subscription.payment_succeeded` → `Active`. A `payment_failed` older than the last applied `payment_succeeded` → ignored. Two `payment_failed` retry attempts with different `webhook_uuid` → both processed.
- `subscription.ended` → `Canceled`, `CanceledAt` set. Cancel then resume → no `CanceledAt`.
- Module subscription started/ended → module on/off (`ModuleAccessFilter`).
- Interval switch calls Alunta and shows the effective date.

Playwright: the billing page renders and the "Betal" button redirects (stub the redirect target). No e2e against Alunta itself. Before go-live: one manual run through the test account for card, invoice and EAN.

## Answers from Alunta

Email from Oliver Lindebod (CEO), 2026-10-27:

1. **Webhooks**: `checkout.completed`, `subscription.payment_succeeded`, `subscription.payment_failed` and `subscription.ended` all exist. `subscription.created` and `subscription.started` also exist if we want the start itself. Dedupe keys are in the Idempotency row above.
2. **Modules**: one subscription per module on the same customer is the right model, with one consolidated invoice and a shared renewal date. Details in D2 and Phase 0 step 2.
3. **Contract party**: Langsom ApS, CVR 33643284 (binavne Alunta ApS, Boligforeningsweb ApS).

Still open: the terms (termination, data export, price-change notice), and whether `webhook_uuid` stays the same when Alunta redelivers the same webhook (it must, or a redelivered `payment_failed` is applied twice; harmless for status, but check before relying on it).

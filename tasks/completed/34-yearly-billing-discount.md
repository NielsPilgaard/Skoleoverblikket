# Task 34: Yearly Billing Discount

## Context

All billing today is monthly-only (`StripeOptions.BasePriceId`, `ModulePriceIds` — one Stripe Price per module, `Mode = "subscription"`, no interval concept anywhere). Add a discounted yearly option so schools who commit upfront pay less than 12x monthly. Standard SaaS pattern: yearly price ≈ 10 months' worth (2 months free).

Discount mechanism: **separate yearly Stripe Price IDs**, not coupons. Coupons on a monthly price don't change billing interval — a real annual plan needs its own Price with `interval: year`.

**Intro pricing**: launch yearly at a low intro base price (~10.000 kr/år) to win first paying customers; modules remain separate add-on Prices billed on the same interval (base + add-on model, not an all-inclusive bundle — see `ModulePriceIds`). Raise later without touching existing customers — Stripe subscriptions pin the Price ID they checked out with, so a later config change (swap `BasePriceIdYearly`/`ModulePriceIds` to a new, higher Price) only affects *new* checkouts. Existing subs keep billing at their original Price forever (until they themselves change plan via Portal). No grandfather field or extra logic needed — this falls out of the Price-ID-per-sub model for free. Just:
- Create the yearly Prices in Stripe at the intro amount now.
- Show an "Intropris" badge next to the yearly option on the billing page. (Revised, no intropris)
- When raising later: create new Stripe Prices, update `BasePriceIdYearly`/`ModulePriceIds` config, deploy. Do not edit/archive the old Price while any sub still references it.

---

## Stripe dashboard setup (manual, before code)

For base price and each module price, create a second Price with `interval=year`, same product, discounted vs. 12x monthly. Record new Price IDs for config.

Enable **Customer Portal price switching**: Stripe Dashboard → Settings → Billing → Customer portal → Products → allow customers to switch between the monthly/yearly Price of the same product. This lets existing monthly subscribers upgrade to yearly via the existing `CreateBillingPortalSessionAsync` flow — no new portal code needed.

> **Superseded by task 42.** Portal price switching stays disabled — task 42 found it silently leaves module items on the old interval for this base+module-items subscription model. Interval changes go through `SwitchIntervalAsync` / `POST /api/v1/billing/interval` instead, which updates base + all module items in one call with explicit proration. `ModulePriceIds` is nested by module then interval (`ModulePriceIds[module][interval]`), not the flat `$"{module}:{interval}"` key shown below.

---

## .NET changes

### `StripeOptions.cs`

Replace single price-per-module with interval-keyed pairs:

```csharp
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string BasePriceIdMonthly { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string BasePriceIdYearly { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string WebhookSecret { get; init; } = string.Empty;

    // key: $"{ModuleName}:{Monthly|Yearly}"
    public Dictionary<string, string> ModulePriceIds { get; init; } = new();
}
```

Update `appsettings.Development.json`, `appsettings.Testing.json`, and deployment secrets with new keys.

### New enum

`Models/BillingInterval.cs`:

```csharp
public enum BillingInterval
{
    Monthly,
    Yearly,
}
```

### `Models/Subscription.cs`

Add `public BillingInterval Interval { get; set; }` — persists which cadence the school is on, read back for module price lookups (a module added later must match the subscription's existing interval, not re-ask Stripe). Needs a migration (`/add-migration`). Default existing rows to `Monthly`.

### `Services/SubscriptionService.cs`

- `CreateCheckoutSessionAsync(schoolId, interval, successUrl, cancelUrl, ct)` — new `BillingInterval interval` param, selects `BasePriceIdMonthly`/`BasePriceIdYearly`. Persist `sub.Interval = interval` at checkout creation (confirmed again in `HandleCheckoutCompletedAsync` from the Stripe session, don't trust client-only state).
- `AddModuleAsync` — price lookup becomes `ModulePriceIds[$"{module}:{sub.Interval}"]`, so modules added after the fact bill on the same cadence as the base subscription.
- `HandleSubscriptionChangedAsync` — when a portal-driven monthly→yearly switch fires `customer.subscription.updated`, detect the new interval from `stripeSub.Items` (Price recurring interval) and update `sub.Interval` accordingly. Without this, an existing subscriber who switches via Portal keeps a stale `Interval` and future `AddModuleAsync` calls buy the wrong-cadence Price.

### `Controllers/BillingController.cs`

- `CreateCheckout` — accept `BillingInterval interval` in request body (`record CheckoutRequest(BillingInterval Interval)`), pass through.
- `SubscriptionDto` — add `BillingInterval Interval` so frontend can render current cadence and price correctly post-checkout.

### Webhook

No new event types needed — portal-driven price switches already arrive as `customer.subscription.updated`, already handled by `HandleSubscriptionChangedAsync` (extend per above).

---

## Frontend changes

### `web/src/pages/*` (abonnement/billing page — locate via existing `/abonnement` route)

- Add monthly/yearly toggle above the checkout CTA. Show both prices with yearly's effective monthly cost + "spar X kr/år" (Danish: "save X kr/year") badge — simplicity-first, Hanne must see the saving without doing math.
- Yearly option also gets an "Intropris" badge (see intro pricing note above) — small, no countdown/urgency pressure.
- Pass selected `interval` into the checkout API call.
- Show current `Interval` from `SubscriptionDto` on the active-subscription view; link to Billing Portal for switching (existing `CreatePortal` flow — no new UI needed there since Portal handles the switch natively).

### Codegen

After controller/DTO changes, run `/codegen` to regenerate OpenAPI spec + typed client (`web/src/api/generated/*`) — never hand-edit those files.

---

## Testing

- `BillingTests.cs` (API integration, tUnit + Testcontainers): checkout session created with correct Price ID per interval; `AddModuleAsync` picks module price matching subscription's stored `Interval`; webhook-driven interval switch updates `sub.Interval`.
- Playwright: extend or add to existing billing e2e — toggle changes displayed price, checkout call carries chosen interval (mock/stub Stripe as already done for other billing e2e, don't hit real Stripe).

## Out of scope

- Proration UI/messaging for monthly→yearly switches — Stripe Portal handles proration automatically, no custom logic needed. **Superseded by task 42**: Portal switching stays disabled; `SwitchIntervalAsync` explicitly sets `ProrationBehavior = "create_prorations"` itself rather than relying on Portal defaults.
- Multi-year or other intervals — only monthly/yearly.
- Retroactive discounts for existing yearly-ineligible contracts — none exist yet, all current subs are monthly. No current subs actually.

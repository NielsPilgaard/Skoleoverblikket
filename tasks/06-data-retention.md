# Todo

## Automated data deletion after subscription cancellation

### Context

The privacy policy (Privatlivspolitik) states that school data is retained for 180 days after subscription cancellation, then permanently deleted. This is currently not implemented — deletion is manual.

This task implements the automated cleanup so the stated policy is actually enforced.

### What to build

#### 1. Track cancellation date

When a Stripe subscription is cancelled (webhook event: `customer.subscription.deleted`), record the cancellation timestamp on the tenant record.

Add a nullable column to the `Tenants` table:

```
SubscriptionCancelledAt DateTime? (UTC)
```

Generate a new EF Core migration for this column. Do not modify existing migrations.

The existing Stripe webhook handler should set this field when the subscription moves to a cancelled state.

#### 2. Background job: delete expired tenants

> Pattern to reuse: `AbsenceRetentionJob` (student absence retention, task 42) is the first
> per-tenant background job. It lists schools with a commented `IgnoreQueryFilters()` and runs
> each tenant's work in its own DI scope pinned with `HttpTenantContext.UseBackgroundTenant`, so
> all deletes still go through the tenant query filter. Absence data is already deleted after
> the previous school year, independent of cancellation.

Add a background service (`IHostedService` or Hangfire recurring job — match whatever background job pattern is already in use in the API) that:

- Runs once daily (e.g. 02:00 UTC)
- Queries for tenants where `SubscriptionCancelledAt` is not null and `SubscriptionCancelledAt <= now - 180 days`
- For each matching tenant, performs a hard delete in this order:
  1. Delete all uploaded files from OVHcloud storage for that tenant
  2. Delete all tenant data from the database (cascades via EF Core, or explicit ordered deletes — whichever is already the pattern)
  3. Delete the tenant row itself
- Logs each deletion (tenant ID, deletion timestamp) at `Information` level
- If file storage deletion fails, log the error and skip database deletion for that tenant (do not leave orphaned DB rows with missing files — fail safe)

#### 3. Self-serve data export before deletion

Before a tenant's data is deleted, they should be able to export it. Check whether an export feature already exists (`/eksporter` route exists in the frontend). If a full export (all schedules, staff, classes as a ZIP or PDF bundle) is not yet available, add a task note — do not implement it in this task.

#### 4. Notify before deletion

Send a warning email to the school's admin account 7 days before the 180-day window expires (i.e. at day 173 post-cancellation). Email should:

- Be in Danish
- State clearly that data will be deleted in 7 days
- Include a link to log in and export data
- Include kontakt@skoleoverblikket.dk for questions

Use whatever transactional email provider is already in use. If none exists, add a task note — do not introduce a new provider without explicit instruction.

### Constraints

- New EF Core migration required — do not modify existing ones
- Tenant scoping must be maintained throughout — never delete across tenant boundaries in a single query
- All deletion must be permanent (hard delete) — no soft-delete tombstones
- The 180-day period is measured from `SubscriptionCancelledAt`, not from the last login or any other signal
- Write an integration test that verifies: a tenant with `SubscriptionCancelledAt = 181 days ago` is deleted, and a tenant with `SubscriptionCancelledAt = 179 days ago` is not

### Out of scope

- Allowing schools to manually trigger early deletion (a future self-serve feature)
- Anonymisation instead of deletion
- Any changes to the billing flow itself

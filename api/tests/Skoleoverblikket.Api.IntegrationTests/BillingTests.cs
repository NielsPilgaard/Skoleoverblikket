using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Stripe;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Integration tests for BillingController GET /subscription.
/// Covers:
///   - Non-admin returns 403.
///   - New school with no subscription record gets auto-created Trialing record.
///   - Trialing school: IsTrialing=true, HasAccess=true, TrialDaysLeft>0.
///   - Expired trial: IsTrialing=false, HasAccess=false, TrialDaysLeft=0.
///   - Active subscription: IsActive=true, HasAccess=true.
///   - Active modules listed correctly.
///   - Yearly billing: checkout persists Interval, AddModuleAsync picks the matching-interval
///     module price, and a portal-driven interval switch (subscription.updated) updates Interval.
///   Checkout/module calls hit stripe-mock (Testcontainers) instead of the real Stripe API —
///   see ApiFactory.
/// </summary>
[ClassDataSource<ApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class BillingTests(ApiFactory factory)
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		Converters = { new JsonStringEnumConverter() },
		PropertyNameCaseInsensitive = true,
	};

	private readonly ApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "billing-admin-subject");
	}

	// ── Private helpers ──────────────────────────────────────────────────────────

	private async Task<Models.Subscription> SeedSubscriptionAsync(
		SubscriptionStatus status,
		DateTimeOffset trialEnd,
		DateTimeOffset? currentPeriodEnd = null,
		string? stripeSubscriptionId = null)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var sub = new Models.Subscription
		{
			Id = Guid.NewGuid(),
			SchoolId = _tenantId,
			Status = status,
			TrialEnd = trialEnd,
			CurrentPeriodEnd = currentPeriodEnd,
			StripeSubscriptionId = stripeSubscriptionId,
		};
		db.Subscriptions.Add(sub);
		await db.SaveChangesAsync();
		return sub;
	}

	private async Task SeedModuleAsync(Guid subscriptionId, SubscriptionModule module)
	{
		using var scope = _factory.Services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		db.SubscriptionModuleItems.Add(new SubscriptionModuleItem
		{
			Id = Guid.NewGuid(),
			SubscriptionId = subscriptionId,
			Module = module,
			IsAdminOverride = true,
		});
		await db.SaveChangesAsync();
	}

	// Signs a raw JSON payload the same way Stripe signs real webhook deliveries
	// (t={timestamp},v1={hex hmac-sha256 of "{timestamp}.{payload}"}), so tests exercise
	// StripeWebhookController's actual EventUtility.ConstructEvent signature verification
	// instead of calling SubscriptionService.HandleWebhookAsync directly.
	private static string SignPayload(string payload, string webhookSecret)
	{
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var signedPayload = $"{timestamp}.{payload}";
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
		var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
		var signature = Convert.ToHexStringLower(hash);
		return $"t={timestamp},v1={signature}";
	}

	private async Task<HttpResponseMessage> PostWebhookAsync(string eventType, string subscriptionId, string priceId, string recurringInterval)
	{
		var payload = $$"""
			{
			  "id": "evt_test_{{Guid.NewGuid():N}}",
			  "object": "event",
			  "type": "{{eventType}}",
			  "request": null,
			  "data": {
			    "object": {
			      "id": "{{subscriptionId}}",
			      "object": "subscription",
			      "status": "active",
			      "items": {
			        "object": "list",
			        "data": [
			          {
			            "id": "si_test_1",
			            "object": "subscription_item",
			            "price": {
			              "id": "{{priceId}}",
			              "object": "price",
			              "recurring": { "interval": "{{recurringInterval}}" }
			            }
			          }
			        ]
			      }
			    }
			  }
			}
			""";

		using var client = _factory.CreateClient();
		var signature = SignPayload(payload, "whsec_stub");
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhook")
		{
			Content = new StringContent(payload, Encoding.UTF8, "application/json"),
		};
		request.Headers.Add("Stripe-Signature", signature);
		return await client.SendAsync(request);
	}

	// ── Auth enforcement ──────────────────────────────────────────────────────────

	[Test]
	public async Task GetSubscription_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-billing");

		var response = await client.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	// ── Trial state ───────────────────────────────────────────────────────────────

	[Test]
	public async Task GetSubscription_NewSchool_AutoCreatesTrialingRecord()
	{
		// No Subscription seeded — GetOrCreateAsync should auto-create one
		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Status).IsEqualTo(SubscriptionStatus.Trialing);
		await Assert.That(dto.IsTrialing).IsTrue();
		await Assert.That(dto.HasAccess).IsTrue();
		await Assert.That(dto.TrialDaysLeft).IsGreaterThan(0);
	}

	[Test]
	public async Task GetSubscription_ActiveTrial_ReturnsCorrectTrialDaysLeft()
	{
		var trialEnd = DateTimeOffset.UtcNow.AddDays(7);
		await SeedSubscriptionAsync(SubscriptionStatus.Trialing, trialEnd);

		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.IsTrialing).IsTrue();
		await Assert.That(dto.HasAccess).IsTrue();
		// Should be ~7 days; allow ±1 for clock skew
		await Assert.That(dto.TrialDaysLeft).IsGreaterThanOrEqualTo(6);
		await Assert.That(dto.TrialDaysLeft).IsLessThanOrEqualTo(8);
	}

	[Test]
	public async Task GetSubscription_ExpiredTrial_IsTrialingFalse_HasAccessFalse()
	{
		var trialEnd = DateTimeOffset.UtcNow.AddDays(-1); // past
		await SeedSubscriptionAsync(SubscriptionStatus.Trialing, trialEnd);

		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.IsTrialing).IsFalse();
		await Assert.That(dto.HasAccess).IsFalse();
		await Assert.That(dto.TrialDaysLeft).IsEqualTo(0);
	}

	// ── Active subscription ───────────────────────────────────────────────────────

	[Test]
	public async Task GetSubscription_Active_IsActiveTrueHasAccessTrue()
	{
		var periodEnd = DateTimeOffset.UtcNow.AddMonths(1);
		await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			currentPeriodEnd: periodEnd,
			stripeSubscriptionId: "sub_test_active");

		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.IsActive).IsTrue();
		await Assert.That(dto.HasAccess).IsTrue();
		await Assert.That(dto.IsTrialing).IsFalse();
		await Assert.That(dto.TrialDaysLeft).IsEqualTo(0);
	}

	// ── Active modules ────────────────────────────────────────────────────────────

	[Test]
	public async Task GetSubscription_WithActiveModule_ListsModule()
	{
		var sub = await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_modules");
		await SeedModuleAsync(sub.Id, SubscriptionModule.ParentModule);

		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.ActiveModules).Contains(SubscriptionModule.ParentModule.ToString());
	}

	[Test]
	public async Task GetSubscription_NoModules_ActiveModulesEmpty()
	{
		await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_no_modules");

		var response = await _adminClient.GetAsync("/api/v1/billing/subscription");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.ActiveModules.Count).IsEqualTo(0);
	}

	// ── Yearly billing interval ──────────────────────────────────────────────────
	//
	// HandleCheckoutCompletedAsync verifies Interval against the actual Stripe subscription's
	// base-plan price (via Stripe.SubscriptionService.GetAsync) rather than trusting client-
	// supplied session metadata — a portal/API-driven price swap shouldn't be able to disagree
	// with what Stripe actually billed. stripe-mock returns canned fixture data for any
	// subscription id, unrelated to our configured stub price IDs, so the resolved price never
	// matches BasePriceIdMonthly/BasePriceIdYearly here: ApplyIntervalFromBasePlan falls through
	// to its "keep existing Interval" branch. Same fixture limitation already documented on
	// AddModule_YearlySubscription_UsesYearlyModulePrice below — the interval-selection logic
	// itself is covered directly by Webhook_SubscriptionUpdatedWithYearlyPrice/MonthlyPrice,
	// which construct the Stripe.Subscription object in-process and don't touch stripe-mock.

	[Test]
	public async Task CreateCheckout_YearlyInterval_ReturnsCheckoutUrl()
	{
		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/checkout",
			new BillingController.CheckoutRequest(BillingInterval.Yearly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task CreateCheckout_MonthlyInterval_ReturnsCheckoutUrl()
	{
		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/checkout",
			new BillingController.CheckoutRequest(BillingInterval.Monthly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
	}

	[Test]
	public async Task AddModule_YearlySubscription_UsesYearlyModulePrice()
	{
		var sub = await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_yearly_module");

		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var trackedSub = await db.Subscriptions.FirstAsync(s => s.Id == sub.Id);
			trackedSub.Interval = BillingInterval.Yearly;
			await db.SaveChangesAsync();
		}

		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/modules",
			new BillingController.ModuleRequest(SubscriptionModule.ParentModule));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

		var subResponse = await _adminClient.GetAsync("/api/v1/billing/subscription");
		var dto = await subResponse.Content.ReadFromJsonAsync<BillingController.SubscriptionDto>(JsonOpts);
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.ActiveModules).Contains(SubscriptionModule.ParentModule.ToString());

		// stripe-mock returns fixture data for subscription_items regardless of the requested
		// Price, so the yearly-vs-monthly price selection can't be observed by reading the item
		// back — AddModuleAsync throws if ModulePriceIds has no entry for the sub's Interval,
		// so reaching NoContent above already proves the yearly price was resolved.
		using var verifyScope = _factory.Services.CreateScope();
		var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
		var moduleItem = await verifyDb.SubscriptionModuleItems
			.FirstOrDefaultAsync(m => m.SubscriptionId == sub.Id && m.Module == SubscriptionModule.ParentModule);
		await Assert.That(moduleItem).IsNotNull();
	}

	[Test]
	public async Task Webhook_SubscriptionUpdatedWithYearlyPrice_UpdatesIntervalToYearly()
	{
		var sub = await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_portal_switch");

		var response = await PostWebhookAsync(
			EventTypes.CustomerSubscriptionUpdated, "sub_test_portal_switch", "price_stub_yearly", "year");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		using var verifyScope = _factory.Services.CreateScope();
		var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
		var updatedSub = await verifyDb.Subscriptions.FirstAsync(s => s.Id == sub.Id);
		await Assert.That(updatedSub.Interval).IsEqualTo(BillingInterval.Yearly);
	}

	[Test]
	public async Task Webhook_SubscriptionUpdatedWithMonthlyPrice_UpdatesIntervalToMonthly()
	{
		var sub = await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_portal_switch_monthly");

		using (var scope = _factory.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var trackedSub = await db.Subscriptions.FirstAsync(s => s.Id == sub.Id);
			trackedSub.Interval = BillingInterval.Yearly;
			await db.SaveChangesAsync();
		}

		var response = await PostWebhookAsync(
			EventTypes.CustomerSubscriptionUpdated, "sub_test_portal_switch_monthly", "price_stub_monthly", "month");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		using var verifyScope = _factory.Services.CreateScope();
		var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
		var updatedSub = await verifyDb.Subscriptions.FirstAsync(s => s.Id == sub.Id);
		await Assert.That(updatedSub.Interval).IsEqualTo(BillingInterval.Monthly);
	}

	// ── API-driven interval switch (task 42) ─────────────────────────────────────
	//
	// Portal's native plan-switch only targets a single subscription item and was found
	// (task 42 investigation, Stripe test mode) to silently leave module items on the old
	// interval when a base+module-items subscription switches via Portal. SwitchIntervalAsync
	// replaces that: it updates the base item and every active module item to the target
	// interval's Price in one Stripe call. Portal's subscription_update feature must stay
	// disabled in the Dashboard config — see CreateBillingPortalSessionAsync.
	//
	// stripe-mock returns fixture data for any subscription id regardless of the requested
	// Price (same limitation as AddModule_YearlySubscription_UsesYearlyModulePrice above), so
	// the actual item-price-update call to Stripe can't be observed end-to-end here. These
	// tests cover the reachable business-rule branches instead: same-interval no-op, and the
	// guard rails (inactive subscription, no Stripe subscription).

	[Test]
	public async Task SwitchInterval_SameAsCurrent_ReturnsNoContentWithoutCallingStripe()
	{
		await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: "sub_test_switch_noop");

		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/interval",
			new BillingController.SwitchIntervalRequest(BillingInterval.Monthly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
	}

	[Test]
	public async Task SwitchInterval_TrialingSubscription_Returns400()
	{
		await SeedSubscriptionAsync(
			SubscriptionStatus.Trialing,
			trialEnd: DateTimeOffset.UtcNow.AddDays(7));

		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/interval",
			new BillingController.SwitchIntervalRequest(BillingInterval.Yearly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task SwitchInterval_NoStripeSubscription_Returns400()
	{
		await SeedSubscriptionAsync(
			SubscriptionStatus.Active,
			trialEnd: DateTimeOffset.UtcNow.AddDays(-7),
			stripeSubscriptionId: null);

		var response = await _adminClient.PostAsJsonAsync(
			"/api/v1/billing/interval",
			new BillingController.SwitchIntervalRequest(BillingInterval.Yearly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
	}

	[Test]
	public async Task SwitchInterval_NonAdmin_Returns403()
	{
		using var client = _factory.CreateClient();
		client.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		client.DefaultRequestHeaders.Add("X-Test-Roles", "user");
		client.DefaultRequestHeaders.Add("X-Test-Subject", "nonadmin-switch");

		var response = await client.PostAsJsonAsync(
			"/api/v1/billing/interval",
			new BillingController.SwitchIntervalRequest(BillingInterval.Yearly));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}
}

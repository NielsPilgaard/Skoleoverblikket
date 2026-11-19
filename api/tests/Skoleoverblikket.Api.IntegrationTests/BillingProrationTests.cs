using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Controllers;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.IntegrationTests.Infrastructure;
using Skoleoverblikket.Api.Models;
using Stripe;
using Stripe.TestHelpers;
using CustomerService = Stripe.CustomerService;

namespace Skoleoverblikket.Api.IntegrationTests;

/// <summary>
/// Verifies SwitchIntervalAsync's ProrationBehavior="create_prorations" semantics against the
/// real Stripe test-mode API using a Stripe test clock — stripe-mock (used by the rest of the
/// suite, see BillingTests) is a stateless fixture server and can't simulate proration or invoice
/// timing.
///
/// Stripe does NOT allow BillingCycleAnchor="unchanged" when an item's recurring interval itself
/// changes (confirmed here: the API rejects it with "Changing plan intervals. There's no way to
/// leave billing cycle unchanged."), so switching interval unavoidably resets CurrentPeriodEnd to
/// "now + new interval" and invoices the new interval's price immediately — see the comment on
/// SwitchIntervalAsync. create_prorations credits the unused time on the old plan against that
/// immediate invoice; this test switches partway through the monthly period and asserts the
/// invoiced amount sits strictly between 0 and the full yearly price, proving a credit was applied.
///
/// Only runs with STRIPE_LIVE_TEST=1 set (see RealStripeApiFactory) — needs network access to
/// Stripe and a real test-mode secret key, so it's excluded from the default `dotnet test` run.
/// </summary>
[ClassDataSource<RealStripeApiFactory>(Shared = SharedType.PerTestSession)]
public sealed class BillingProrationTests(RealStripeApiFactory factory)
{
	private readonly RealStripeApiFactory _factory = factory;
	private readonly Guid _tenantId = Guid.NewGuid();
	private HttpClient _adminClient = null!;

	[Before(Test)]
	public async Task SetUp()
	{
		await TestDataBuilder.CreateSchoolAsync(_factory.Services, _tenantId);
		_adminClient = _factory.CreateClient();
		_adminClient.DefaultRequestHeaders.Add("X-Test-TenantId", _tenantId.ToString());
		_adminClient.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
		_adminClient.DefaultRequestHeaders.Add("X-Test-Subject", "billing-proration-admin");
	}

	[Test]
	[SkipUnlessStripeLive]
	public async Task SwitchInterval_MonthlyToYearly_ResetsRenewalDateAndCreditsUnusedTime()
	{
		using var scope = _factory.Services.CreateScope();
		var stripeOptions = scope.ServiceProvider.GetRequiredService<IOptions<StripeOptions>>().Value;
		var testClockService = scope.ServiceProvider.GetRequiredService<TestClockService>();
		var customerService = scope.ServiceProvider.GetRequiredService<CustomerService>();
		var stripeSubscriptionService = scope.ServiceProvider.GetRequiredService<Stripe.SubscriptionService>();
		var invoiceService = scope.ServiceProvider.GetRequiredService<InvoiceService>();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		var testClock = await testClockService.CreateAsync(new TestClockCreateOptions
		{
			FrozenTime = DateTime.UtcNow,
			Name = $"billing-proration-test-{_tenantId}",
		});

		try
		{
			var customer = await customerService.CreateAsync(new CustomerCreateOptions
			{
				Email = "proration-test@example.com",
				TestClock = testClock.Id,
				PaymentMethod = "pm_card_visa",
				InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = "pm_card_visa" },
			});

			var stripeSub = await stripeSubscriptionService.CreateAsync(new SubscriptionCreateOptions
			{
				Customer = customer.Id,
				Items = [new SubscriptionItemOptions { Price = stripeOptions.BasePriceIdMonthly, Quantity = 1 }],
			});

			var originalPeriodStart = stripeSub.CurrentPeriodStart;
			var originalPeriodEnd = stripeSub.CurrentPeriodEnd;
			var midCycle = originalPeriodStart + TimeSpan.FromTicks((originalPeriodEnd - originalPeriodStart).Ticks / 2);

			// Advance to the midpoint of the monthly period so the switch has real unused time to
			// credit — switching at the very start of the period (elapsed ~= 0) would make the
			// prorated credit indistinguishable from "no charge at all", the same false-pass the
			// pre-fix version of this test had.
			testClock = await testClockService.AdvanceAsync(testClock.Id, new TestClockAdvanceOptions { FrozenTime = midCycle });
			await WaitForClockReadyAsync(testClockService, testClock.Id);

			var sub = new Models.Subscription
			{
				Id = Guid.NewGuid(),
				SchoolId = _tenantId,
				Status = SubscriptionStatus.Active,
				TrialEnd = DateTimeOffset.UtcNow.AddDays(-7),
				CurrentPeriodEnd = originalPeriodEnd,
				StripeCustomerId = customer.Id,
				StripeSubscriptionId = stripeSub.Id,
				Interval = BillingInterval.Monthly,
			};
			db.Subscriptions.Add(sub);
			await db.SaveChangesAsync();

			var switchedAt = testClock.FrozenTime;

			var response = await _adminClient.PostAsJsonAsync(
				"/api/v1/billing/interval",
				new BillingController.SwitchIntervalRequest(BillingInterval.Yearly));

			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

			// Item swaps to the yearly price immediately. Stripe has no way to change an item's
			// recurring interval without also resetting the billing cycle anchor to now — see the
			// comment on SwitchIntervalAsync — so CurrentPeriodEnd moves to roughly "now + 1 year"
			// (Stripe's exact anchor-date math isn't a contract we depend on, hence the tolerance),
			// it does NOT stay at the original monthly renewal date.
			var updatedStripeSub = await stripeSubscriptionService.GetAsync(stripeSub.Id);
			var yearlyItem = updatedStripeSub.Items.Data.Single();
			await Assert.That(yearlyItem.Price.Id).IsEqualTo(stripeOptions.BasePriceIdYearly);
			await Assert.That(updatedStripeSub.CurrentPeriodEnd).IsGreaterThan(switchedAt.AddDays(300));
			await Assert.That(updatedStripeSub.CurrentPeriodEnd).IsLessThan(switchedAt.AddDays(430));
			await Assert.That(updatedStripeSub.CurrentPeriodEnd).IsNotEqualTo(originalPeriodEnd);

			// create_prorations bills the proration immediately as its own invoice at the moment of
			// the switch (not as part of the next renewal preview, which by then already reflects
			// the new yearly price for the *next* cycle) — so the credit shows up on the invoice
			// list, not via InvoiceService.UpcomingAsync. It should be less than the full yearly
			// price but still positive, proving a real credit was applied for the unused time on
			// the old monthly plan rather than either "full price" (the pre-fix bug) or "nothing
			// charged".
			var invoices = await invoiceService.ListAsync(
				new InvoiceListOptions { Customer = customer.Id, Subscription = stripeSub.Id, Limit = 2 });
			var prorationInvoice = invoices.Data.OrderByDescending(i => i.Created).First();
			var yearlyPrice = await scope.ServiceProvider.GetRequiredService<PriceService>().GetAsync(stripeOptions.BasePriceIdYearly);
			await Assert.That(prorationInvoice.Total).IsGreaterThan(0);
			await Assert.That(prorationInvoice.Total).IsLessThan(yearlyPrice.UnitAmount ?? 0);
		}
		finally
		{
			// Deleting the test clock deletes every object created under it (customer, subscription, invoices).
			await testClockService.DeleteAsync(testClock.Id, new TestClockDeleteOptions());
		}
	}

	private static async Task WaitForClockReadyAsync(TestClockService service, string clockId)
	{
		for (var i = 0; i < 30; i++)
		{
			var clock = await service.GetAsync(clockId);
			if (clock.Status == "ready")
			{
				return;
			}

			await Task.Delay(TimeSpan.FromSeconds(2));
		}

		throw new TimeoutException($"Test clock {clockId} did not finish advancing within 60s.");
	}
}

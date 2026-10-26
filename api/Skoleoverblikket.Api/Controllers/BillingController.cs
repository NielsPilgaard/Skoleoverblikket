using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize(Roles = Roles.Admin)]
public sealed class BillingController(
	SubscriptionService subscriptionService,
	ITenantContext tenantContext,
	IOptions<ApplicationOptions> appOptions,
	ILogger<BillingController> logger) : ControllerBase
{
	public record SubscriptionDto(
		SubscriptionStatus Status,
		BillingInterval Interval,
		DateTimeOffset TrialEnd,
		DateTimeOffset? CurrentPeriodEnd,
		bool IsTrialing,
		bool IsActive,
		bool HasAccess,
		int TrialDaysLeft,
		IReadOnlyList<string> ActiveModules,
		bool HasFreeAccess,
		DateOnly? FreeAccessLastDay);

	public record CheckoutResponse(string Url);

	public record CheckoutRequest(BillingInterval Interval);

	[HttpGet("subscription")]
	public async Task<ActionResult<SubscriptionDto>> GetSubscription(CancellationToken cancellationToken)
	{
		var sub = await subscriptionService.GetOrCreateAsync(tenantContext.TenantId, cancellationToken);
		var modules = await subscriptionService.GetActiveModulesAsync(tenantContext.TenantId, cancellationToken);
		return Ok(ToDto(sub, modules));
	}

	[HttpPost("modules")]
	public async Task<IActionResult> AddModule([FromBody] ModuleRequest request, CancellationToken cancellationToken)
	{
		try
		{
			await subscriptionService.AddModuleAsync(tenantContext.TenantId, request.Module, cancellationToken);
			return NoContent();
		}
		catch (InvalidOperationException ex)
		{
			return Problem(title: "Modul kunne ikke tilføjes", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
		}
		catch (SubscriptionConfigurationException ex)
		{
			logger.LogError(ex, "AddModule configuration error: {Message}", ex.Message);
			return Problem(
				title: "Modul er ikke korrekt konfigureret",
				detail: "Kunne ikke tilføje modul på grund af en serverfejl. Kontakt support.",
				statusCode: StatusCodes.Status500InternalServerError);
		}
		catch (Stripe.StripeException ex)
		{
			logger.LogError(ex, "AddModule Stripe error: {Code} {Message}", ex.StripeError?.Code, ex.Message);
			return Problem(
				title: "Betalingsgateway fejl",
				detail: "Kunne ikke tilføje modul. Prøv igen eller kontakt support.",
				statusCode: StatusCodes.Status502BadGateway,
				extensions: new Dictionary<string, object?> { ["stripeCode"] = ex.StripeError?.Code });
		}
	}

	[HttpDelete("modules/{module}")]
	public async Task<IActionResult> RemoveModule(SubscriptionModule module, CancellationToken cancellationToken)
	{
		try
		{
			await subscriptionService.RemoveModuleAsync(tenantContext.TenantId, module, cancellationToken);
			return NoContent();
		}
		catch (InvalidOperationException ex)
		{
			return Problem(title: "Modul kunne ikke fjernes", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
		}
		catch (Stripe.StripeException ex)
		{
			return Problem(
				title: "Betalingsgateway fejl",
				detail: "Kunne ikke fjerne modul. Prøv igen eller kontakt support.",
				statusCode: StatusCodes.Status502BadGateway,
				extensions: new Dictionary<string, object?> { ["stripeCode"] = ex.StripeError?.Code });
		}
	}

	public record ModuleRequest(SubscriptionModule Module);

	[HttpPost("checkout")]
	public async Task<ActionResult<CheckoutResponse>> CreateCheckout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(request.Interval))
		{
			return Problem(title: "Ugyldigt betalingsinterval", detail: "Interval skal være Monthly eller Yearly.", statusCode: StatusCodes.Status400BadRequest);
		}

		var baseUrl = appOptions.Value.BaseUrl;
		var successUrl = $"{baseUrl}/abonnement?success=true";
		var cancelUrl = $"{baseUrl}/abonnement";

		try
		{
			var url = await subscriptionService.CreateCheckoutSessionAsync(
				tenantContext.TenantId, request.Interval, successUrl, cancelUrl, cancellationToken);
			return Ok(new CheckoutResponse(url));
		}
		catch (Stripe.StripeException ex)
		{
			return Problem(
				title: "Betalingsgateway fejl",
				detail: "Kunne ikke oprette betalingssession. Prøv igen eller kontakt support.",
				statusCode: StatusCodes.Status502BadGateway,
				extensions: new Dictionary<string, object?> { ["stripeCode"] = ex.StripeError?.Code });
		}
	}

	public record SwitchIntervalRequest(BillingInterval Interval);

	[HttpPost("interval")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> SwitchInterval([FromBody] SwitchIntervalRequest request, CancellationToken cancellationToken)
	{
		if (!Enum.IsDefined(request.Interval))
		{
			return Problem(title: "Ugyldigt betalingsinterval", detail: "Interval skal være Monthly eller Yearly.", statusCode: StatusCodes.Status400BadRequest);
		}

		try
		{
			await subscriptionService.SwitchIntervalAsync(tenantContext.TenantId, request.Interval, cancellationToken);
			return NoContent();
		}
		catch (InvalidOperationException ex)
		{
			return Problem(title: "Kunne ikke skifte betalingsinterval", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
		}
		catch (SubscriptionConfigurationException ex)
		{
			logger.LogError(ex, "SwitchInterval configuration error: {Message}", ex.Message);
			return Problem(
				title: "Abonnement er ikke korrekt konfigureret",
				detail: "Kunne ikke skifte betalingsinterval på grund af en serverfejl. Kontakt support.",
				statusCode: StatusCodes.Status500InternalServerError);
		}
		catch (Stripe.StripeException ex)
		{
			logger.LogError(ex, "SwitchInterval Stripe error: {Code} {Message}", ex.StripeError?.Code, ex.Message);
			return Problem(
				title: "Betalingsgateway fejl",
				detail: "Kunne ikke skifte betalingsinterval. Prøv igen eller kontakt support.",
				statusCode: StatusCodes.Status502BadGateway,
				extensions: new Dictionary<string, object?> { ["stripeCode"] = ex.StripeError?.Code });
		}
	}

	[HttpPost("portal")]
	public async Task<ActionResult<CheckoutResponse>> CreatePortal(CancellationToken cancellationToken)
	{
		var returnUrl = $"{appOptions.Value.BaseUrl}/abonnement";

		try
		{
			var url = await subscriptionService.CreateBillingPortalSessionAsync(
				tenantContext.TenantId, returnUrl, cancellationToken);
			return Ok(new CheckoutResponse(url));
		}
		catch (Stripe.StripeException ex)
		{
			return Problem(
				title: "Betalingsgateway fejl",
				detail: "Kunne ikke åbne betalingsportal. Prøv igen eller kontakt support.",
				statusCode: StatusCodes.Status502BadGateway,
				extensions: new Dictionary<string, object?> { ["stripeCode"] = ex.StripeError?.Code });
		}
	}

	private static SubscriptionDto ToDto(Subscription sub, IReadOnlyList<string> activeModules)
	{
		var now = DateTimeOffset.UtcNow;
		var isTrialing = SubscriptionService.IsTrialing(sub, now);
		var hasFreeAccess = SubscriptionService.HasFreeAccess(sub, now);
		var trialDaysLeft = isTrialing ? Math.Max(0, (int)Math.Ceiling((sub.TrialEnd - now).TotalDays)) : 0;

		return new SubscriptionDto(
			sub.Status,
			sub.Interval,
			sub.TrialEnd,
			sub.CurrentPeriodEnd,
			isTrialing,
			sub.Status == SubscriptionStatus.Active,
			SubscriptionService.HasAccess(sub, now),
			trialDaysLeft,
			activeModules,
			hasFreeAccess,
			hasFreeAccess ? sub.FreeAccessLastDay : null);
	}
}

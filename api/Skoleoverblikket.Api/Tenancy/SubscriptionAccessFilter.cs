using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Tenancy;

/// <summary>
/// Blocks mutating requests (POST/PUT/PATCH/DELETE) when the tenant's subscription
/// has expired (trial ended, canceled, unpaid). Read-only access is always allowed.
/// Billing and Stripe webhook routes are exempt so schools can always resubscribe, and the
/// databehandleraftale can always be accepted.
/// </summary>
public sealed class SubscriptionAccessFilter(AppDbContext db, ITenantContext tenantContext) : IAsyncActionFilter
{
	private static readonly HashSet<string> MutatingMethods =
		new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

	private static readonly string[] ExemptPrefixes =
	[
		"/api/v1/billing",
		"/api/v1/stripe",
		"/api/v1/superadmin",
		"/api/v1/admin",
		"/api/v1/data-processing-agreement",
	];

	public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		var method = context.HttpContext.Request.Method;
		if (!MutatingMethods.Contains(method))
		{
			await next();
			return;
		}

		var path = context.HttpContext.Request.Path.Value ?? string.Empty;
		foreach (var prefix in ExemptPrefixes)
		{
			if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				await next();
				return;
			}
		}

		Guid schoolId;
		try
		{
			schoolId = tenantContext.TenantId;
		}
		catch (MissingTenantClaimException)
		{
			// No tenant claim — let auth handle it.
			await next();
			return;
		}

		var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.SchoolId == schoolId);
		if (sub is not null && !HasAccess(sub))
		{
			context.Result = new ObjectResult(new ProblemDetails
			{
				Title = "Abonnement udløbet",
				Detail = "Skolens prøveperiode eller abonnement er udløbet. Forny abonnementet under Abonnement for at fortsætte med at redigere.",
				Status = StatusCodes.Status403Forbidden,
			})
			{
				StatusCode = StatusCodes.Status403Forbidden,
			};
			return;
		}

		await next();
	}

	private static bool HasAccess(Subscription sub)
	{
		var now = DateTimeOffset.UtcNow;
		return sub.Status == SubscriptionStatus.Active
			|| (sub.Status == SubscriptionStatus.Trialing && sub.TrialEnd > now);
	}
}

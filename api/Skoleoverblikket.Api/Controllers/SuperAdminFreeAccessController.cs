using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Superadmin gives a school free base access without Stripe, e.g. when its trial has expired.
/// </summary>
[ApiController]
[Route("api/v1/admin/tenants/{schoolId:guid}/free-access")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminFreeAccessController(SubscriptionService subscriptions) : ControllerBase
{
	/// <summary>Null <see cref="GrantFreeAccessRequest.LastDay"/> means no end date.</summary>
	public record GrantFreeAccessRequest(DateOnly? LastDay);

	[HttpPut]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> GrantFreeAccess(Guid schoolId, GrantFreeAccessRequest request, CancellationToken cancellationToken) =>
		await subscriptions.GrantFreeAccessAsync(schoolId, request.LastDay, cancellationToken) switch
		{
			FreeAccessResult.Granted => NoContent(),
			FreeAccessResult.SchoolNotFound => NotFound(),
			FreeAccessResult.LastDayInPast => Problem(
				title: "Ugyldig dato",
				detail: "Sidste dag kan ikke ligge før i dag.",
				statusCode: StatusCodes.Status400BadRequest),
			FreeAccessResult.PaidSubscriptionActive => Problem(
				title: "Skolen betaler allerede",
				detail: "Skolen har et aktivt Stripe-abonnement. Gratis adgang kan kun gives uden et betalt abonnement.",
				statusCode: StatusCodes.Status409Conflict),
			var other => throw new InvalidOperationException($"Unhandled result {other}"),
		};

	[HttpDelete]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> RevokeFreeAccess(Guid schoolId, CancellationToken cancellationToken) =>
		await subscriptions.RevokeFreeAccessAsync(schoolId, cancellationToken) ? NoContent() : NotFound();
}

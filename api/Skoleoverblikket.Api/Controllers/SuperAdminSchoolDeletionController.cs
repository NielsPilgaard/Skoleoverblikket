using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using Result = Skoleoverblikket.Api.Services.SchoolDeletionService.DeleteNowResult;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Superadmin deletes a school and all its data now, without the 90-day retention wait.
/// Cannot be undone; the guards are in <see cref="SchoolDeletionService.DeleteNowAsync"/>.
/// </summary>
[ApiController]
[Route("api/v1/admin/tenants/{schoolId:guid}/deletion")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminSchoolDeletionController(SchoolDeletionService deletion) : ControllerBase
{
	/// <summary>The school's exact name, typed by the superadmin as confirmation.</summary>
	public record DeleteSchoolRequest(string ConfirmSchoolName);

	[HttpPost]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> DeleteSchool(Guid schoolId, DeleteSchoolRequest request, CancellationToken cancellationToken)
	{
		if (User.GetKeycloakSubject() is not { } subject)
		{
			return Forbid();
		}

		return await deletion.DeleteNowAsync(schoolId, request.ConfirmSchoolName, subject, cancellationToken) switch
		{
			Result.Deleted => NoContent(),
			Result.SchoolNotFound => NotFound(),
			Result.NameMismatch => Problem(
				title: "Navnet passer ikke",
				detail: "Skriv skolens navn præcis, som det står, for at bekræfte sletningen.",
				statusCode: StatusCodes.Status400BadRequest),
			Result.CallerBelongsToSchool => Problem(
				title: "Du har et login på skolen",
				detail: "Du kan ikke slette en skole, du selv er medarbejder, forælder eller bestyrelsesmedlem på.",
				statusCode: StatusCodes.Status409Conflict),
			Result.StripeSubscriptionLive => Problem(
				title: "Skolen har et abonnement i Stripe",
				detail: "Opsig abonnementet i Stripe først, ellers bliver skolen ved med at blive opkrævet.",
				statusCode: StatusCodes.Status409Conflict),
			Result.FreeAccessActive => Problem(
				title: "Skolen har gratis adgang",
				detail: "Fjern skolens gratis adgang først.",
				statusCode: StatusCodes.Status409Conflict),
			Result.StorageFailed or Result.AccountsFailed => Problem(
				title: "Sletningen blev ikke gennemført",
				detail: "Skolens filer eller logins kunne ikke slettes. Databasen er urørt. Prøv igen.",
				statusCode: StatusCodes.Status502BadGateway),
			var other => throw new InvalidOperationException($"Unhandled result {other}"),
		};
	}
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.Services.DataProcessingAgreementService;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/data-processing-agreement")]
[Authorize(Roles = Roles.Admin)]
public sealed class DataProcessingAgreementController(DataProcessingAgreementService agreements) : ControllerBase
{
	/// <summary>Whether the school has accepted the current databehandleraftale, and by whom.</summary>
	[HttpGet]
	public async Task<ActionResult<DataProcessingAgreementStatusDto>> GetStatus(CancellationToken cancellationToken) =>
		Ok(await agreements.GetStatusAsync(cancellationToken));

	/// <summary>Accepts the databehandleraftale on behalf of the school.</summary>
	[HttpPost("acceptance")]
	public async Task<IActionResult> Accept(AcceptDataProcessingAgreementRequest request, CancellationToken cancellationToken) =>
		await agreements.AcceptAsync(User.GetKeycloakSubject() ?? string.Empty, request.Version, cancellationToken) switch
		{
			AcceptOutcome.Accepted => NoContent(),
			AcceptOutcome.WrongVersion => Problem(
				title: "Databehandleraftalen er blevet opdateret",
				detail: "Genindlæs siden og læs den nye version, før du accepterer.",
				statusCode: StatusCodes.Status409Conflict),
			_ => Problem(
				title: "Kun skolens medarbejdere kan acceptere",
				detail: "Databehandleraftalen skal accepteres af en administrator fra skolen.",
				statusCode: StatusCodes.Status403Forbidden),
		};
}

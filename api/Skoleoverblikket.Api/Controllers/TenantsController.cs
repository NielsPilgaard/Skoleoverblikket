using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.Services.SchoolSignupService;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
public sealed class TenantsController(SchoolSignupService signup) : ControllerBase
{
	private const string TryAgain = "Der skete en fejl hos os. Prøv igen om lidt.";

	/// <summary>
	/// Create a new tenant (school) and its first admin user.
	/// Returns a JWT so the frontend can initialise Keycloak immediately — no separate login redirect needed.
	/// Called anonymously during school signup.
	/// </summary>
	[HttpPost]
	[AllowAnonymous]
	[EnableRateLimiting("signup")]
	public async Task<ActionResult<TenantCreatedDto>> Create([FromBody] CreateTenantRequest req, CancellationToken cancellationToken)
	{
		var result = await signup.CreateAsync(req, cancellationToken);
		return result.Failure switch
		{
			null => Ok(result.Created),
			Failure.EmailTaken => Problem(
				title: "E-mailen er allerede i brug",
				detail: "Der findes allerede en bruger med den e-mail. En bruger kan kun høre til én skole, så brug en anden e-mail til den nye skole.",
				statusCode: StatusCodes.Status409Conflict),
			Failure.AccountFailed => Problem(title: "Kunne ikke oprette brugerkonto", detail: TryAgain, statusCode: 502),
			Failure.SaveFailed => Problem(title: "Kunne ikke oprette skole", detail: TryAgain, statusCode: 502),
			_ => Problem(title: "Skole oprettet, men login fejlede", detail: "Skolen er oprettet. Log ind med din e-mail og adgangskode.", statusCode: 502),
		};
	}
}

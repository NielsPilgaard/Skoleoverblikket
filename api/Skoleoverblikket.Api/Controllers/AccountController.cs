using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using static Skoleoverblikket.Api.Services.AccountService;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Min konto: the caller's own login, whatever their role. Changing the password or two-factor happens on
/// Keycloak's pages (application-initiated actions); these endpoints only read status and end sessions.
/// </summary>
[ApiController]
[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(AccountService accounts) : ControllerBase
{
	[HttpGet("security")]
	public async Task<ActionResult<AccountSecurityDto>> GetSecurity(CancellationToken cancellationToken) =>
		User.GetKeycloakSubject() is { } subject
			? Ok(await accounts.GetSecurityAsync(subject, cancellationToken))
			: Unauthorized();

	[HttpPost("logout-everywhere")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> LogoutEverywhere(CancellationToken cancellationToken)
	{
		if (User.GetKeycloakSubject() is not { } subject)
		{
			return Unauthorized();
		}

		await accounts.LogoutEverywhereAsync(subject, cancellationToken);
		return NoContent();
	}

	[HttpPost("security/cleanup-recovery-codes")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> CleanupRecoveryCodes(CancellationToken cancellationToken)
	{
		if (User.GetKeycloakSubject() is not { } subject)
		{
			return Unauthorized();
		}

		await accounts.CleanupRecoveryCodesAsync(subject, cancellationToken);
		return NoContent();
	}

	/// <summary>Maps a two-factor reset to its response, for the staff, parent, board and back office endpoints.</summary>
	internal static IActionResult ResetResponse(ControllerBase controller, TwoFactorResetResult result) => result switch
	{
		TwoFactorResetResult.Reset => controller.NoContent(),
		TwoFactorResetResult.NotFound => controller.NotFound(),
		TwoFactorResetResult.Self => controller.Problem(
			title: "Du kan ikke nulstille din egen tofaktor",
			detail: "Brug en gendannelseskode, eller bed en anden administrator om at nulstille den.",
			statusCode: StatusCodes.Status403Forbidden),
		TwoFactorResetResult.SharedLogin => controller.Problem(
			title: "Kontoen bruges også andre steder",
			detail: "Personen bruger samme login på en anden skole. Kontakt Skoleoverblikkets support for at nulstille tofaktor.",
			statusCode: StatusCodes.Status409Conflict),
		TwoFactorResetResult.NoTwoFactor => controller.Problem(
			title: "Tofaktor er ikke slået til",
			detail: "Personen logger allerede ind med kun adgangskode.",
			statusCode: StatusCodes.Status409Conflict),
		_ => throw new InvalidOperationException($"Unhandled result {result}"),
	};
}

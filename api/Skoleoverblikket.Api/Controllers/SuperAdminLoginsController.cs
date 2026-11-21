using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>The operator turns off two-factor for any login, also one shared between schools.</summary>
[ApiController]
[Route("api/v1/admin/logins")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class SuperAdminLoginsController(AccountService accounts) : ControllerBase
{
	[HttpPost("reset-two-factor")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	public async Task<IActionResult> ResetTwoFactor(AccountService.ResetTwoFactorRequest request, CancellationToken cancellationToken) =>
		AccountController.ResetResponse(this,
			await accounts.ResetTwoFactorByEmailAsync(request.Email, User.GetKeycloakSubject() ?? "", cancellationToken));
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/classes/{classId:guid}/permissions")]
[Authorize]
public sealed class ClassPermissionsController(ClassPermissionService classPermissions) : ControllerBase
{
	[HttpGet]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<List<ClassPermissionDto>>> GetAll(Guid classId, CancellationToken cancellationToken) =>
		await classPermissions.GetForClassAsync(classId, cancellationToken) is { } permissions
			? Ok(permissions)
			: NotFound();

	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<ClassPermissionDto>> Grant(Guid classId, [FromBody] GrantPermissionRequest req, CancellationToken cancellationToken)
	{
		var (result, permission) = await classPermissions.GrantAsync(classId, req.StaffId, cancellationToken);
		return result switch
		{
			GrantPermissionResult.Granted => CreatedAtAction(nameof(GetAll), new { classId }, permission),
			GrantPermissionResult.ClassNotFound => Problem(
				title: "Klassen findes ikke",
				detail: "Klassen findes ikke længere. Genindlæs siden.",
				statusCode: 404),
			GrantPermissionResult.StaffNotFound => Problem(
				title: "Medarbejderen findes ikke",
				detail: "Medarbejderen findes ikke længere. Genindlæs siden.",
				statusCode: 404),
			GrantPermissionResult.AlreadyGranted => Problem(
				title: "Adgang allerede tildelt",
				detail: "Denne medarbejder har allerede adgang til klassen.",
				statusCode: 409),
			_ => throw new InvalidOperationException($"Unmapped {nameof(GrantPermissionResult)}: {result}"),
		};
	}

	[HttpDelete("{staffId:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Revoke(Guid classId, Guid staffId, CancellationToken cancellationToken) =>
		await classPermissions.RevokeAsync(classId, staffId, cancellationToken) ? NoContent() : NotFound();
}

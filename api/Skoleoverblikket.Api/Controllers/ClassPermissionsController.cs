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
	public record ClassPermissionDto(Guid StaffId, string StaffName, DateTimeOffset GrantedAt);
	public record GrantPermissionRequest(Guid StaffId);

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
			ClassPermissionService.GrantResult.Granted => CreatedAtAction(nameof(GetAll), new { classId }, permission),
			ClassPermissionService.GrantResult.AlreadyGranted => Problem(
				title: "Adgang allerede tildelt",
				detail: "Denne medarbejder har allerede adgang til klassen.",
				statusCode: 409),
			_ => NotFound(),
		};
	}

	[HttpDelete("{staffId:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Revoke(Guid classId, Guid staffId, CancellationToken cancellationToken) =>
		await classPermissions.RevokeAsync(classId, staffId, cancellationToken) ? NoContent() : NotFound();
}

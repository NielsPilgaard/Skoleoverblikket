using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/staff")]
[Authorize(Roles = $"{Roles.Staff},{Roles.Board}")]
public sealed class StaffController(AppDbContext db, StaffService staffService, IAuthorizationService authz) : ControllerBase
{
	public record StaffDto(Guid Id, string Name, string? Email, string? Phone, StaffRole Role, bool IsAdmin, string? KeycloakSubject);
	public record UpsertStaffRequest(string Name, string? Email, string? Phone, StaffRole Role, bool IsAdmin = false);
	public record PatchAdminPermissionRequest(bool IsAdmin);

	[HttpGet]
	public async Task<ActionResult<List<StaffDto>>> GetAll(CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Board))
		{
			var authResult = await authz.AuthorizeAsync(User, null, Policies.CanAccessTeacherData);
			if (!authResult.Succeeded)
			{
				return Forbid();
			}
		}

		var staff = await db.Staff
			.AsNoTracking()
			.OrderBy(s => s.Name)
			.Select(s => new StaffDto(s.Id, s.Name, s.Email, s.Phone, s.Role, s.IsAdmin, s.KeycloakSubject))
			.ToListAsync(cancellationToken);
		return Ok(staff);
	}

	[HttpGet("me")]
	public async Task<ActionResult<StaffDto>> GetMe(CancellationToken cancellationToken)
	{
		var subject = User.GetKeycloakSubject();

		if (string.IsNullOrWhiteSpace(subject))
		{
			return Unauthorized();
		}

		var staff = await db.Staff
			.AsNoTracking()
			.Where(s => s.KeycloakSubject == subject)
			.Select(s => new StaffDto(s.Id, s.Name, s.Email, s.Phone, s.Role, s.IsAdmin, s.KeycloakSubject))
			.FirstOrDefaultAsync(cancellationToken);

		return staff is null
			? NotFound()
			: Ok(staff);
	}

	[HttpGet("{id:guid}")]
	public async Task<ActionResult<StaffDto>> GetById(Guid id, CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Board))
		{
			var authResult = await authz.AuthorizeAsync(User, null, Policies.CanAccessTeacherData);
			if (!authResult.Succeeded)
			{
				return Forbid();
			}
		}

		var staff = await db.Staff
							.AsNoTracking()
							.Where(s => s.Id == id)
							.Select(s => new StaffDto(s.Id, s.Name, s.Email, s.Phone, s.Role, s.IsAdmin, s.KeycloakSubject))
							.FirstOrDefaultAsync(cancellationToken);

		return staff is null
				   ? NotFound()
				   : Ok(staff);
	}

	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<StaffDto>> Create([FromBody] UpsertStaffRequest req, CancellationToken cancellationToken) =>
		await staffService.CreateAsync(req, cancellationToken) is { } created
			? CreatedAtAction(nameof(GetById), new { id = created.Id }, created)
			: EmailTaken();

	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<StaffDto>> Update(Guid id, [FromBody] UpsertStaffRequest req, CancellationToken cancellationToken) =>
		ToResult(await staffService.UpdateAsync(id, req, User.GetKeycloakSubject(), cancellationToken));

	[HttpPatch("{id:guid}/admin-permission")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<StaffDto>> PatchAdminPermission(Guid id, [FromBody] PatchAdminPermissionRequest req, CancellationToken cancellationToken) =>
		ToResult(await staffService.SetAdminAsync(id, req.IsAdmin, User.GetKeycloakSubject(), cancellationToken));

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await staffService.DeleteAsync(id, User.GetKeycloakSubject(), cancellationToken) switch
		{
			StaffService.DeleteResult.Deleted => NoContent(),
			StaffService.DeleteResult.Self => Problem(
				detail: "Du kan ikke slette din egen konto.",
				statusCode: StatusCodes.Status403Forbidden),
			StaffService.DeleteResult.HasLessons => Problem(
				detail: "Medarbejderen er tildelt en eller flere lektioner og kan ikke slettes. Fjern medarbejderen fra alle lektioner først.",
				statusCode: StatusCodes.Status409Conflict),
			_ => NotFound(),
		};

	private ActionResult<StaffDto> ToResult((StaffService.ChangeResult Result, StaffDto? Staff) change) => change.Result switch
	{
		StaffService.ChangeResult.Done => Ok(change.Staff),
		StaffService.ChangeResult.EmailTaken => EmailTaken(),
		StaffService.ChangeResult.NotAccepted => Problem(
			title: "Invitation ikke accepteret",
			detail: "Medarbejderen skal acceptere invitationen, før administratoradgang kan ændres.",
			statusCode: 409),
		StaffService.ChangeResult.CannotRemoveOwnAdmin => Problem(
			title: "Ikke tilladt",
			detail: "Du kan ikke fjerne din egen administratoradgang.",
			statusCode: 409),
		StaffService.ChangeResult.LastAdmin => Problem(
			title: "Ikke tilladt",
			detail: "Der skal altid være mindst én administrator. Tildel administratoradgang til en anden medarbejder først.",
			statusCode: 409),
		StaffService.ChangeResult.KeycloakFailed => Problem(
			title: "Keycloak-synkronisering fejlede",
			detail: "Administratoradgangen kunne ikke fjernes. Prøv igen.",
			statusCode: 502),
		_ => NotFound(),
	};

	private ActionResult EmailTaken() => ValidationProblem(new ValidationProblemDetails
	{
		Errors = { ["email"] = ["En medarbejder med denne e-mailadresse findes allerede."] }
	});
}

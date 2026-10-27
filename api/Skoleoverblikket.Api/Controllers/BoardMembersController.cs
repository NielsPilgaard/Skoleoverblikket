using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/board-members")]
[Authorize]
[RequiresModule(SubscriptionModule.BoardModule)]
public sealed class BoardMembersController(BoardMemberInvitationService boardMembers) : ControllerBase
{
	public record BoardMemberDto(
		Guid Id,
		string Name,
		string Email,
		bool CanAccessTeacherData,
		bool HasAccount,
		DateTimeOffset CreatedAt);

	public record InviteBoardMemberRequest(
		[Required, StringLength(200, MinimumLength = 1)] string Name,
		[Required, StringLength(500), EmailAddress] string Email);

	public record ToggleTeacherDataRequest(bool CanAccessTeacherData);

	[HttpGet]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<List<BoardMemberDto>>> GetAll(CancellationToken cancellationToken) =>
		Ok(await boardMembers.GetAllAsync(cancellationToken));

	[HttpGet("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> GetById(Guid id, CancellationToken cancellationToken) =>
		await boardMembers.GetByIdAsync(id, cancellationToken) is { } member ? Ok(member) : NotFound();

	[HttpGet("me")]
	[Authorize(Roles = Roles.Board)]
	public async Task<ActionResult<BoardMemberDto>> GetMe(CancellationToken cancellationToken)
	{
		var sub = User.FindFirstValue("sub");
		if (string.IsNullOrEmpty(sub))
		{
			return Unauthorized();
		}

		return await boardMembers.GetBySubjectAsync(sub, cancellationToken) is { } member ? Ok(member) : NotFound();
	}

	[HttpPost("invite")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> Invite(
		[FromBody] InviteBoardMemberRequest req,
		CancellationToken cancellationToken) =>
		await boardMembers.InviteAsync(req.Name, req.Email, cancellationToken) is { } member
			? CreatedAtAction(nameof(GetById), new { id = member.Id }, ToDto(member))
			: Problem(
			title: "E-mailadressen bruges på en anden skole",
			detail: "Denne e-mail bruges allerede på en anden skole. Brug en anden e-mailadresse.",
			statusCode: StatusCodes.Status409Conflict);

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await boardMembers.DeleteAsync(id, cancellationToken) switch
		{
			BoardMemberInvitationService.DeleteResult.Deleted => NoContent(),
			BoardMemberInvitationService.DeleteResult.LoginDeleteFailed => Problem(
				title: "Keycloak-brugeren kunne ikke slettes. Prøv igen.",
				statusCode: StatusCodes.Status502BadGateway),
			_ => NotFound(),
		};

	[HttpPatch("{id:guid}/teacher-data-access")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> ToggleTeacherDataAccess(
		Guid id,
		[FromBody] ToggleTeacherDataRequest req,
		CancellationToken cancellationToken) =>
		await boardMembers.SetTeacherDataAccessAsync(id, req.CanAccessTeacherData, cancellationToken) is { } member
			? Ok(member)
			: NotFound();

	private static BoardMemberDto ToDto(BoardMember m) =>
		new(m.Id, m.Name, m.Email, m.CanAccessTeacherData, m.KeycloakSubject is not null, m.CreatedAt);
}

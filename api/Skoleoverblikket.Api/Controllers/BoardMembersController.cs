using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/board-members")]
[Authorize]
[RequiresModule(SubscriptionModule.BoardModule)]
public sealed class BoardMembersController(
	AppDbContext db,
	ITenantContext tenant,
	BoardMemberInvitationService invitationService,
	KeycloakAdminService keycloakAdmin) : ControllerBase
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
	public async Task<ActionResult<List<BoardMemberDto>>> GetAll(CancellationToken cancellationToken)
	{
		var members = await db.BoardMembers
			.AsNoTracking()
			.OrderBy(m => m.Name)
			.ToListAsync(cancellationToken);

		return Ok(members.Select(ToDto).ToList());
	}

	[HttpGet("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> GetById(Guid id, CancellationToken cancellationToken)
	{
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
		if (member is null)
		{
			return NotFound();
		}

		return Ok(ToDto(member));
	}

	[HttpGet("me")]
	[Authorize(Roles = Roles.Board)]
	public async Task<ActionResult<BoardMemberDto>> GetMe(CancellationToken cancellationToken)
	{
		var sub = User.FindFirstValue("sub");
		if (string.IsNullOrEmpty(sub))
		{
			return Unauthorized();
		}

		var member = await db.BoardMembers
			.AsNoTracking()
			.IgnoreQueryFilters()
			.FirstOrDefaultAsync(m => m.KeycloakSubject == sub, cancellationToken);

		if (member is null)
		{
			return NotFound();
		}

		return Ok(ToDto(member));
	}

	[HttpPost("invite")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> Invite(
		[FromBody] InviteBoardMemberRequest req,
		CancellationToken cancellationToken)
	{
		var normalizedEmail = req.Email.Trim().ToLowerInvariant();

		var existing = await db.BoardMembers
			.FirstOrDefaultAsync(m => m.Email == normalizedEmail, cancellationToken);

		BoardMember member;
		if (existing is not null)
		{
			member = existing;
		}
		else
		{
			member = new BoardMember
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				Name = req.Name.Trim(),
				Email = normalizedEmail,
			};
			db.BoardMembers.Add(member);
			try
			{
				await db.SaveChangesAsync(cancellationToken);
			}
			catch (DbUpdateException)
			{
				// Race condition: another request created the same member
				member = await db.BoardMembers
					.FirstAsync(m => m.Email == normalizedEmail, cancellationToken);
			}
		}

		await invitationService.CreateAndSendAsync(member, cancellationToken);

		return CreatedAtAction(nameof(GetById), new { id = member.Id }, ToDto(member));
	}

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
		if (member is null)
		{
			return NotFound();
		}

		if (!string.IsNullOrEmpty(member.KeycloakSubject))
		{
			try
			{
				await keycloakAdmin.DeleteStaffUserAsync(member.KeycloakSubject, cancellationToken);
			}
			catch (KeycloakException ex)
			{
				return Problem(
					detail: ex.Message,
					title: "Keycloak-brugeren kunne ikke slettes. Prøv igen.",
					statusCode: StatusCodes.Status502BadGateway);
			}
		}

		db.BoardMembers.Remove(member);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}

	[HttpPatch("{id:guid}/teacher-data-access")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<BoardMemberDto>> ToggleTeacherDataAccess(
		Guid id,
		[FromBody] ToggleTeacherDataRequest req,
		CancellationToken cancellationToken)
	{
		var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
		if (member is null)
		{
			return NotFound();
		}

		member.CanAccessTeacherData = req.CanAccessTeacherData;
		await db.SaveChangesAsync(cancellationToken);
		return Ok(ToDto(member));
	}

	private static BoardMemberDto ToDto(BoardMember m) =>
		new(m.Id, m.Name, m.Email, m.CanAccessTeacherData, m.KeycloakSubject is not null, m.CreatedAt);
}

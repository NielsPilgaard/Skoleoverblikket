using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/board-invitations")]
public sealed class BoardInvitationsController(
	AppDbContext db,
	BoardMemberInvitationService invitationService) : ControllerBase
{
	[HttpGet("preview")]
	[AllowAnonymous]
	public async Task<ActionResult> Preview([FromQuery] string token, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(token))
		{
			return BadRequest(new ProblemDetails { Title = "Token mangler" });
		}

		var invitation = await invitationService.FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return Problem(title: "Ugyldig eller udløbet invitation", statusCode: 404);
		}

		var school = await db.Schools
			.IgnoreQueryFilters()
			.Where(s => s.Id == invitation.TenantId)
			.Select(s => s.Name)
			.FirstOrDefaultAsync(cancellationToken);

		return Ok(new
		{
			boardMemberName = invitation.BoardMember.Name,
			email = invitation.Email,
			schoolName = school ?? "Skoleoverblikket",
			expiresAt = invitation.ExpiresAt,
		});
	}

	[HttpPost("{token}/accept")]
	[AllowAnonymous]
	public async Task<ActionResult> Accept(string token, CancellationToken cancellationToken)
	{
		return await invitationService.AcceptAsync(token, cancellationToken) switch
		{
			InvitationAcceptResult.Accepted => NoContent(),
			InvitationAcceptResult.ModuleInactive => Problem(
				title: "Modulet er ikke aktivt",
				detail: "Skolen har ikke længere Bestyrelsesmodulet. Kontakt skolens kontor.",
				statusCode: 403),
			_ => Problem(title: "Ugyldig eller udløbet invitation", statusCode: 400),
		};
	}
}

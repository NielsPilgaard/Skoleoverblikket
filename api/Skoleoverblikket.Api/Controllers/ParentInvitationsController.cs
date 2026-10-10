using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/parent-invitations")]
public sealed class ParentInvitationsController(
	AppDbContext db,
	ParentInvitationService invitationService) : ControllerBase
{
	[HttpGet("preview")]
	[AllowAnonymous]
	public async Task<ActionResult> Preview([FromQuery] string token, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(token))
		{
			return BadRequest(new ProblemDetails { Title = "Token mangler", Status = 400 });
		}

		var invitation = await invitationService.FindValidAsync(token, cancellationToken);
		if (invitation is null)
		{
			return Problem(title: "Ugyldig eller udløbet invitation", statusCode: 404);
		}

		var school = await db.Schools
							 .IgnoreQueryFilters()
							 .Where(s => s.Id == invitation.TenantId)
							 .Select(s => new { s.Name })
							 .FirstOrDefaultAsync(cancellationToken);

		return Ok(new
		{
			parentName = invitation.Parent.Name,
			email = invitation.Email,
			schoolName = school?.Name ?? "Skoleoverblikket",
			expiresAt = invitation.ExpiresAt,
		});
	}

	[HttpPost("accept")]
	[Authorize]
	public async Task<ActionResult> Accept([FromQuery] string token, CancellationToken cancellationToken)
	{
		var keycloakSubject = User.GetKeycloakSubject();
		if (string.IsNullOrEmpty(keycloakSubject))
		{
			return Unauthorized(new ProblemDetails { Title = "Ikke autentificeret", Status = 401 });
		}

		return await invitationService.AcceptAsync(token, keycloakSubject, cancellationToken) switch
		{
			InvitationAcceptResult.Accepted => NoContent(),
			InvitationAcceptResult.ModuleInactive => Problem(
				title: "Modulet er ikke aktivt",
				detail: "Skolen har ikke længere Forældremodulet. Kontakt skolens kontor.",
				statusCode: 403),
			_ => Problem(title: "Ugyldig eller udløbet invitation", statusCode: 404),
		};
	}

	[HttpPost("{parentId:guid}/resend")]
	[RequiresModule(SubscriptionModule.ParentModule)]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult> Resend(Guid parentId, CancellationToken cancellationToken)
	{
		var parent = await db.Parents.FirstOrDefaultAsync(p => p.Id == parentId, cancellationToken);
		if (parent is null)
		{
			return NotFound();
		}

		await invitationService.CreateAndSendAsync(parent, cancellationToken);
		return NoContent();
	}
}

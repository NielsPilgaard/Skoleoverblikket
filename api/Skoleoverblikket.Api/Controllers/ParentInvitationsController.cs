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
	public async Task<ActionResult<ParentInvitationService.InvitationPreview>> Preview([FromQuery] string token, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(token))
		{
			return BadRequest(new ProblemDetails { Title = "Token mangler", Status = 400 });
		}

		var preview = await invitationService.PreviewAsync(token, cancellationToken);
		return preview is null ? Problem(title: "Ugyldig eller udløbet invitation", statusCode: 404) : Ok(preview);
	}

	/// <summary>A new invitee chooses their password. Returns tokens for their first session.</summary>
	[HttpPost("password")]
	[AllowAnonymous]
	public async Task<ActionResult<InvitationLoginDto>> SetPassword([FromBody] SetInvitationPasswordRequest req, CancellationToken cancellationToken)
	{
		var (result, login) = await invitationService.SetPasswordAsync(req.Token, req.Password, cancellationToken);
		return result switch
		{
			SetInvitationPasswordResult.Set => Ok(login),
			SetInvitationPasswordResult.PasswordAlreadySet => Problem(
				title: "Du har allerede en adgangskode",
				detail: "Log ind med din eksisterende adgangskode.",
				statusCode: StatusCodes.Status409Conflict),
			_ => Problem(
				title: "Ugyldig eller udløbet invitation",
				detail: "Invitationslinket er ugyldigt eller udløbet.",
				statusCode: StatusCodes.Status404NotFound),
		};
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

		return await invitationService.ResendAsync(parent, cancellationToken)
			? NoContent()
			: Problem(
			title: "E-mailadressen bruges på en anden skole",
			detail: "Denne e-mail bruges allerede på en anden skole. Brug en anden e-mailadresse.",
			statusCode: StatusCodes.Status409Conflict);
	}
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/board-invitations")]
public sealed class BoardInvitationsController(BoardMemberInvitationService invitationService) : ControllerBase
{
	[HttpGet("preview")]
	[AllowAnonymous]
	public async Task<ActionResult<BoardMemberInvitationService.InvitationPreview>> Preview([FromQuery] string token, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(token))
		{
			return BadRequest(new ProblemDetails { Title = "Token mangler" });
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

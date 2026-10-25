using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Links: the school's curated external links (Aula, fravær, buskort, Facebook groups). Everyone reads
/// the links for the role they act as; only admins manage them. Basis, not module-gated.
/// </summary>
[ApiController]
[Route("api/v1/external-links")]
[Authorize]
public sealed class ExternalLinksController(ExternalLinkService links) : ControllerBase
{
	/// <summary>The links for the role the caller acts as, in the school's order.</summary>
	[HttpGet]
	public async Task<ActionResult<List<ExternalLinkDto>>> GetVisible(CancellationToken cancellationToken)
	{
		LinkAudience? audience = User.IsInRole(Roles.Staff) ? LinkAudience.Staff
			: User.IsInRole(Roles.Parent) ? LinkAudience.Parent
			: User.IsInRole(Roles.Board) ? LinkAudience.Board
			: null;
		if (audience is null)
		{
			return Ok(new List<ExternalLinkDto>());
		}

		return Ok(await links.GetVisibleLinksAsync(audience.Value, User.GetKeycloakSubject(), cancellationToken));
	}

	/// <summary>Every link with its audience and classes.</summary>
	[HttpGet("all")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<List<ExternalLinkAdminDto>>> GetAll(CancellationToken cancellationToken) =>
		Ok(await links.GetAllLinksAsync(cancellationToken));

	/// <summary>Adds a link at the end of the list. A URL without a scheme gets https://.</summary>
	[HttpPost]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> Create([FromBody] UpsertExternalLinkRequest req, CancellationToken cancellationToken)
	{
		var (result, id) = await links.CreateLinkAsync(req, cancellationToken);
		return result == ExternalLinkSaveResult.Saved
			? CreatedAtAction(nameof(GetAll), new { }, new { id })
			: SaveFailed(result);
	}

	[HttpPut("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> Update(Guid id, [FromBody] UpsertExternalLinkRequest req, CancellationToken cancellationToken)
	{
		var result = await links.UpdateLinkAsync(id, req, cancellationToken);
		return result == ExternalLinkSaveResult.Saved ? NoContent() : SaveFailed(result);
	}

	[HttpDelete("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
		await links.DeleteLinkAsync(id, cancellationToken) ? NoContent() : NotFound();

	/// <summary>Sets the order of all links. The body lists every link id once, first link first.</summary>
	[HttpPut("order")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> Reorder([FromBody] ReorderExternalLinksRequest req, CancellationToken cancellationToken) =>
		await links.ReorderAsync(req.OrderedIds, cancellationToken)
			? NoContent()
			: Problem(
				title: "Listen er ændret",
				detail: "En anden har ændret linkene imens. Genindlæs siden og prøv igen.",
				statusCode: StatusCodes.Status409Conflict);

	private IActionResult SaveFailed(ExternalLinkSaveResult result) => result switch
	{
		ExternalLinkSaveResult.NotFound => NotFound(),
		ExternalLinkSaveResult.InvalidUrl => Invalid(
			nameof(UpsertExternalLinkRequest.Url), "Skriv en webadresse, der starter med https:// eller http://."),
		ExternalLinkSaveResult.NoAudience => Invalid(
			nameof(UpsertExternalLinkRequest.VisibleToParents), "Vælg mindst én: Forældre, Medarbejdere eller Bestyrelse."),
		_ => Invalid(nameof(UpsertExternalLinkRequest.ClassIds), "En af de valgte klasser findes ikke længere."),
	};

	private ActionResult Invalid(string field, string message) =>
		ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] })
		{
			Detail = message,
		});
}

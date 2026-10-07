using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/exports")]
[Authorize(Roles = Roles.Admin)]
public sealed class SchoolExportController(SchoolExportService exports, ExportLinkTokens links, ITenantContext tenant) : ControllerBase
{
	public sealed record ExportLinkDto(string Url);

	/// <summary>
	/// POST /api/v1/exports/school.zip/link — a one-minute, single-use link to <see cref="DownloadSchoolZip"/>,
	/// so the browser can download the ZIP natively instead of holding all of it in memory.
	/// </summary>
	[HttpPost("school.zip/link")]
	public ActionResult<ExportLinkDto> CreateSchoolZipLink() =>
		User.GetKeycloakSubject() is { } subject
			? Ok(new ExportLinkDto($"/api/v1/exports/school.zip/download?token={Uri.EscapeDataString(links.Create(tenant.TenantId, subject))}"))
			: Forbid();

	/// <summary>GET /api/v1/exports/school.zip/download?token=… — <see cref="GetSchoolZip"/> for a link from <see cref="CreateSchoolZipLink"/>.</summary>
	/// <param name="token">The link's token. Checked and redeemed by <see cref="ExportLinkAuthHandler"/>; declared here so it is in the OpenAPI spec.</param>
	/// <param name="cancellationToken">Request cancellation.</param>
	[HttpGet("school.zip/download")]
	[Authorize(AuthenticationSchemes = ExportLinkAuthHandler.SchemeName)]
	[Produces("application/zip")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public Task<IResult> DownloadSchoolZip([FromQuery, Required] string token, CancellationToken cancellationToken) =>
		GetSchoolZip(cancellationToken);

	/// <summary>
	/// GET /api/v1/exports/school.zip — all the school's data: a CSV per table plus every uploaded file.
	/// Streamed as it is built, so an error after the first bytes ends the download instead of returning ProblemDetails.
	/// Read-only, so it keeps working after the subscription is canceled, until the data is deleted.
	/// </summary>
	[HttpGet("school.zip")]
	[Produces("application/zip")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public async Task<IResult> GetSchoolZip(CancellationToken cancellationToken)
	{
		var fileName = SchoolExportService.ZipFileName(await exports.GetSchoolNameAsync(cancellationToken), DateTimeOffset.UtcNow);
		return Results.Stream(
			body => exports.WriteZipAsync(body, HttpContext.RequestAborted),
			contentType: "application/zip",
			fileDownloadName: fileName);
	}
}

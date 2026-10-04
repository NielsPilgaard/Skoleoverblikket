using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/exports")]
[Authorize(Roles = Roles.Admin)]
public sealed class SchoolExportController(SchoolExportService exports) : ControllerBase
{
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

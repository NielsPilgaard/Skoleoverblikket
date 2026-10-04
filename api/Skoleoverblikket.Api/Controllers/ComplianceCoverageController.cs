using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/compliance-coverage")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Board}")]
public sealed class ComplianceCoverageController(ComplianceCoverageService complianceCoverage) : ControllerBase
{
	[HttpGet("coverage")]
	public async Task<ActionResult<CoverageResponseDto>> GetCoverage(CancellationToken cancellationToken) =>
		Ok(await complianceCoverage.GetCoverageAsync(cancellationToken));

	[HttpPost("snapshots")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<CoverageSnapshotSummaryDto>> CreateSnapshot([FromBody] CreateCoverageSnapshotRequest req, CancellationToken cancellationToken)
	{
		var snapshot = await complianceCoverage.CreateSnapshotAsync(User.GetKeycloakSubject() ?? string.Empty, req.Reason, cancellationToken);
		return snapshot is null
			? Forbid()
			: CreatedAtAction(nameof(GetSnapshot), new { id = snapshot.Id }, snapshot);
	}

	[HttpGet("snapshots")]
	public async Task<ActionResult<List<CoverageSnapshotSummaryDto>>> GetSnapshots(CancellationToken cancellationToken) =>
		Ok(await complianceCoverage.GetSnapshotsAsync(cancellationToken));

	[HttpGet("snapshots/{id:guid}")]
	public async Task<ActionResult<CoverageSnapshotDetailDto>> GetSnapshot(Guid id, CancellationToken cancellationToken)
	{
		var (result, snapshot, dataVersion) = await complianceCoverage.GetSnapshotAsync(id, cancellationToken);
		return result switch
		{
			CoverageSnapshotLookup.Found => Ok(snapshot),
			// Not a server error: a 500 here would be indistinguishable from a transient failure to
			// the frontend, hiding a compliance record right when someone needs it.
			CoverageSnapshotLookup.UnsupportedVersion => Problem(
				title: "Snapshot-version understøttes ikke",
				detail: $"Denne version blev gemt i et format, der ikke længere understøttes (version {dataVersion}).",
				statusCode: StatusCodes.Status409Conflict),
			_ => NotFound(),
		};
	}

	[HttpDelete("snapshots/{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> DeleteSnapshot(Guid id, CancellationToken cancellationToken) =>
		await complianceCoverage.DeleteSnapshotAsync(id, cancellationToken) ? NoContent() : NotFound();
}

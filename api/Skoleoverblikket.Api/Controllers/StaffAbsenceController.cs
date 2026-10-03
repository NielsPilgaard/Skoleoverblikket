using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Staff fravær: a teacher or pædagog reports themselves absent (or the office does it for them),
/// and the office covers each affected lektion with a vikar.
/// </summary>
[ApiController]
[Route("api/v1/staff-absences")]
[Authorize]
public sealed class StaffAbsenceController(StaffAbsenceService staffAbsences) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Report([FromBody] ReportStaffAbsenceRequest req, CancellationToken cancellationToken)
	{
		if (IsParentOrBoard())
		{
			return Forbid();
		}

		var (result, id) = await staffAbsences.ReportAsync(
			User.GetKeycloakSubject(), User.IsInRole(Roles.Admin), req, cancellationToken);

		return result switch
		{
			StaffAbsenceReportResult.Created => CreatedAtAction(nameof(GetMine), new { }, new { id }),
			StaffAbsenceReportResult.StaffNotFound => NotFound(),
			StaffAbsenceReportResult.InvalidDates => Problem("Ugyldige datoer. Fravær kan højst dække 90 dage.", statusCode: 400),
			_ => Forbid(),
		};
	}

	[HttpGet("mine")]
	public async Task<ActionResult<IReadOnlyList<StaffAbsenceDto>>> GetMine(CancellationToken cancellationToken)
	{
		if (IsParentOrBoard())
		{
			return Forbid();
		}

		return await staffAbsences.GetMineAsync(User.GetKeycloakSubject(), cancellationToken);
	}

	/// <summary>Staff fravær overlapping the range. Defaults to the last week through the next 30 days.</summary>
	[HttpGet]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<IReadOnlyList<StaffAbsenceDto>>> GetAll(
		[FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
	{
		var today = SchoolDayCalendar.Today();
		var start = from ?? today.AddDays(-7);
		var end = to ?? today.AddDays(30);
		if (end < start || end.DayNumber - start.DayNumber > 400)
		{
			return Problem("Ugyldig periode", statusCode: 400);
		}

		return await staffAbsences.GetAllAsync(start, end, cancellationToken);
	}

	/// <summary>One absence with each affected lektion, its current vikar and ranked candidates.</summary>
	[HttpGet("{id:guid}")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<StaffAbsenceDetailDto>> GetDetail(Guid id, CancellationToken cancellationToken)
	{
		var detail = await staffAbsences.GetDetailAsync(id, cancellationToken);
		return detail is null ? NotFound() : detail;
	}

	/// <summary>Assigns (or clears, with a null StaffId) the vikar for one affected lektion.</summary>
	[HttpPut("{id:guid}/substitute")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<IActionResult> AssignSubstitute(
		Guid id, [FromBody] AssignAbsenceSubstituteRequest req, CancellationToken cancellationToken) =>
		await staffAbsences.AssignSubstituteAsync(id, req, cancellationToken) switch
		{
			AbsenceSubstituteResult.Assigned => NoContent(),
			AbsenceSubstituteResult.NotFound => NotFound(),
			AbsenceSubstituteResult.NotAffected => Problem("Lektionen er ikke berørt af dette fravær", statusCode: 400),
			AbsenceSubstituteResult.CandidateNotFound => Problem("Vikaren findes ikke", statusCode: 400),
			_ => Problem("Vikaren er lige blevet optaget i samme tidsrum. Vælg en anden.", statusCode: 409),
		};

	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
	{
		if (IsParentOrBoard())
		{
			return Forbid();
		}

		return await staffAbsences.DeleteAsync(id, User.GetKeycloakSubject(), User.IsInRole(Roles.Admin), cancellationToken) switch
		{
			StaffAbsenceDeleteResult.Deleted => NoContent(),
			StaffAbsenceDeleteResult.NotFound => NotFound(),
			_ => Forbid(),
		};
	}

	private bool IsParentOrBoard() => User.IsInRole(Roles.Parent) || User.IsInRole(Roles.Board);
}

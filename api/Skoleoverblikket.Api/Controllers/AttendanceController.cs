using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

/// <summary>
/// Daily fremmøde: a teacher notes who is absent at the start of the day (and at the end of the
/// day for 7.–10. klasse). Writes go to the fravær register through <see cref="AbsenceService"/>.
/// </summary>
[ApiController]
[Route("api/v1/attendance")]
[Authorize]
public sealed class AttendanceController(AbsenceService absence, IAuthorizationService authorization) : ControllerBase
{
	[HttpGet("classes/{classId:guid}")]
	public async Task<ActionResult<ClassAttendanceDto>> GetClassAttendance(
		Guid classId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
	{
		if (!(await authorization.AuthorizeAsync(User, classId, new EditClassRequirement())).Succeeded)
		{
			return Forbid();
		}

		var attendance = await absence.GetClassAttendanceAsync(classId, date ?? SchoolDayCalendar.Today(), cancellationToken);
		return attendance is null ? NotFound() : attendance;
	}

	[HttpPut("classes/{classId:guid}")]
	public async Task<IActionResult> SaveClassAttendance(
		Guid classId, [FromQuery] DateOnly? date, [FromBody] SaveAttendanceRequest req, CancellationToken cancellationToken)
	{
		if (!(await authorization.AuthorizeAsync(User, classId, new EditClassRequirement())).Succeeded)
		{
			return Forbid();
		}

		var result = await absence.SaveClassAttendanceAsync(
			classId, date ?? SchoolDayCalendar.Today(), req, User.GetKeycloakSubject(), cancellationToken);

		return result switch
		{
			SaveAttendanceResult.Saved => NoContent(),
			SaveAttendanceResult.ClassNotFound => NotFound(),
			SaveAttendanceResult.NoStaffRecord => Forbid(),
			SaveAttendanceResult.NotASchoolDay => Problem("Der er ikke skole den dag", statusCode: 400),
			SaveAttendanceResult.FutureDate => Problem("Fremmøde kan ikke noteres for en dag, der ikke er kommet endnu", statusCode: 400),
			SaveAttendanceResult.QuarterClosed => Problem("Kvartalet er afsluttet, så fremmødet kan ikke længere rettes", statusCode: 400),
			SaveAttendanceResult.EndOfDayNotRequired => Problem("Fremmøde ved skoledagens slutning noteres kun for 7.–10. klasse", statusCode: 400),
			SaveAttendanceResult.InvalidCategory => Problem("Vælg sygdom eller ulovligt fravær", statusCode: 400),
			SaveAttendanceResult.StudentNotInClass => Problem("En af eleverne går ikke i klassen", statusCode: 400),
			_ => Problem(statusCode: 500),
		};
	}

	/// <summary>Which klasser have had fremmøde noted on a day — the office's overview.</summary>
	[HttpGet("overview")]
	[Authorize(Roles = Roles.Admin)]
	public async Task<ActionResult<AttendanceOverviewDto>> GetOverview(
		[FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
		await absence.GetOverviewAsync(date ?? SchoolDayCalendar.Today(), cancellationToken);

	/// <summary>The caller's klasser that still need fremmøde today.</summary>
	[HttpGet("mine/pending")]
	public async Task<ActionResult<IReadOnlyList<PendingAttendanceDto>>> GetMyPending(CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Parent) || User.IsInRole(Roles.Board))
		{
			return Forbid();
		}

		return await absence.GetMyPendingAsync(User.GetKeycloakSubject(), DateTimeOffset.UtcNow, cancellationToken);
	}
}

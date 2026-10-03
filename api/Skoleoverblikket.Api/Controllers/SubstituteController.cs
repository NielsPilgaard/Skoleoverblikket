using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Services;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Authorize]
public sealed class SubstituteController(
	SubstituteService substitutes,
	IAuthorizationService authorization) : ControllerBase
{
	/// <summary>
	/// Returns all staff split into "free" and "busy" for the given slot. Busy = fraværende that day,
	/// teaching an overlapping lektion, or already vikar in an overlapping lektion that week.
	/// </summary>
	[HttpGet("api/v1/staff/available")]
	public async Task<ActionResult<StaffAvailabilityDto>> GetAvailable(
		[FromQuery] int isoYear,
		[FromQuery] int isoWeek,
		[FromQuery] int weekday,
		[FromQuery] Guid timeSlotId,
		CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Parent) || User.IsInRole(Roles.Board))
		{
			return Forbid();
		}

		if (!IsoWeekValidation.IsValid(isoYear, isoWeek))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		if (weekday is < 1 or > 5)
		{
			return Problem("weekday skal være 1–5 (mandag–fredag)", statusCode: 400);
		}

		var availability = await substitutes.GetAvailabilityAsync(isoYear, isoWeek, (DayOfWeek)weekday, timeSlotId, cancellationToken);
		return availability is null ? NotFound() : availability;
	}

	/// <summary>Assigns (or clears) a substitute teacher/aide on an existing week plan slot.</summary>
	[HttpPut("api/v1/week-plans/{weekPlanId:guid}/slots/{slotId:guid}/substitute")]
	public async Task<ActionResult<SubstituteAssignmentDto>> AssignSubstitute(
		Guid weekPlanId,
		Guid slotId,
		[FromBody] AssignSubstituteRequest req,
		CancellationToken cancellationToken)
	{
		var classId = await substitutes.GetWeekPlanClassIdAsync(weekPlanId, cancellationToken);
		if (classId is null)
		{
			return NotFound();
		}

		if (!(await authorization.AuthorizeAsync(User, classId.Value, new EditClassRequirement())).Succeeded)
		{
			return Forbid();
		}

		var (result, assignment) = await substitutes.SetWeekPlanSlotSubstituteAsync(weekPlanId, slotId, req, cancellationToken);
		return result switch
		{
			SetSubstituteResult.Saved => assignment!,
			SetSubstituteResult.NotFound => NotFound(),
			SetSubstituteResult.SamePersonBothRoles => Problem("Samme person kan ikke tildeles som både lærer og pædagog", statusCode: 400),
			SetSubstituteResult.StaffBusy => Problem("Vikaren er optaget i samme tidsrum. Vælg en anden.", statusCode: 409),
			_ => Problem("Vikaren findes ikke", statusCode: 400),
		};
	}

	/// <summary>The caller's vikar lektioner. Defaults to today through the next 14 days.</summary>
	[HttpGet("api/v1/substitutions/mine")]
	public async Task<ActionResult<IReadOnlyList<MySubstitutionDto>>> GetMine(
		[FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
	{
		if (User.IsInRole(Roles.Parent) || User.IsInRole(Roles.Board))
		{
			return Forbid();
		}

		var start = from ?? SchoolDayCalendar.Today();
		var end = to ?? start.AddDays(14);
		if (end < start || end.DayNumber - start.DayNumber > 120)
		{
			return Problem("Ugyldig periode", statusCode: 400);
		}

		return await substitutes.GetMySubstitutionsAsync(User.GetKeycloakSubject(), start, end, cancellationToken);
	}
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Authorize]
public sealed class SubstituteController(AppDbContext db) : ControllerBase
{
	public record AvailableStaffDto(Guid Id, string Name, StaffRole Role);

	public record BusyStaffDto(
		Guid Id,
		string Name,
		StaffRole Role,
		string ConflictDescription);

	public record StaffAvailabilityDto(
		IReadOnlyList<AvailableStaffDto> Available,
		IReadOnlyList<BusyStaffDto> Busy);

	public record AssignSubstituteRequest(Guid? SubstituteTeacherId, Guid? SubstituteAideId);

	/// <summary>
	/// Returns all staff split into "free" and "busy" for the given slot.
	/// Busy = assigned as teacher or aide on any SchemaSlot at that weekday+timeSlot that week,
	///        OR already assigned as substitute on a WeekPlanSlot at that time.
	/// </summary>
	[HttpGet("api/v1/staff/available")]
	public async Task<ActionResult<StaffAvailabilityDto>> GetAvailable(
		[FromQuery] int isoYear,
		[FromQuery] int isoWeek,
		[FromQuery] int weekday,
		[FromQuery] Guid timeSlotId,
		CancellationToken cancellationToken)
	{
		if (!IsoWeekValidation.IsValid(isoYear, isoWeek))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		if (weekday is < 1 or > 5)
		{
			return Problem("weekday skal være 1–5 (mandag–fredag)", statusCode: 400);
		}

		var requestedDay = (DayOfWeek)weekday;

		var timeSlot = await db.TimeSlots.AsNoTracking().FirstOrDefaultAsync(t => t.Id == timeSlotId, cancellationToken);
		if (timeSlot is null)
		{
			return NotFound();
		}

		var allStaff = await db.Staff.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);

		// Find all SchemaSlots on this weekday whose time overlaps the requested time slot
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var overlappingSchemaSlots = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Weekday == requestedDay &&
						s.Schema.StartDate <= today && s.Schema.EndDate >= today)
			.Include(s => s.TimeSlot)
			.Include(s => s.Course)
			.Include(s => s.Schema).ThenInclude(sc => sc.Class)
			.ToListAsync(cancellationToken);

		overlappingSchemaSlots = overlappingSchemaSlots
			.Where(s => Overlaps(s.TimeSlot.StartTime, s.TimeSlot.EndTime, timeSlot.StartTime, timeSlot.EndTime))
			.ToList();

		// Find substitute assignments for this week that overlap the requested time
		var weekPlanSlotIds = await db.WeekPlanSlots
			.AsNoTracking()
			.Where(wps =>
				wps.WeekPlan.IsoYear == isoYear && wps.WeekPlan.IsoWeek == isoWeek &&
				(wps.SubstituteTeacherId != null || wps.SubstituteAideId != null))
			.Include(wps => wps.SchemaSlot).ThenInclude(ss => ss.TimeSlot)
			.Include(wps => wps.SchemaSlot).ThenInclude(ss => ss.Course)
			.Include(wps => wps.SchemaSlot).ThenInclude(ss => ss.Schema).ThenInclude(sc => sc.Class)
			.ToListAsync(cancellationToken);

		var overlappingSubstitutes = weekPlanSlotIds
			.Where(wps => wps.SchemaSlot.Weekday == requestedDay &&
						  Overlaps(wps.SchemaSlot.TimeSlot.StartTime, wps.SchemaSlot.TimeSlot.EndTime, timeSlot.StartTime, timeSlot.EndTime))
			.ToList();

		// Build conflict map: staffId → description
		var conflicts = new Dictionary<Guid, string>();

		foreach (var ss in overlappingSchemaSlots)
		{
			var courseName = ss.Course?.Name ?? "";
			var className = ss.Schema?.Class?.Name ?? "";
			var desc = $"Optaget: {className} – {courseName}";

			if (!conflicts.ContainsKey(ss.TeacherId))
			{
				conflicts[ss.TeacherId] = desc;
			}

			if (ss.AideId.HasValue && !conflicts.ContainsKey(ss.AideId.Value))
			{
				conflicts[ss.AideId.Value] = desc;
			}
		}

		foreach (var wps in overlappingSubstitutes)
		{
			var courseName = wps.SchemaSlot.Course?.Name ?? "";
			var className = wps.SchemaSlot.Schema?.Class?.Name ?? "";
			var desc = $"Optaget: {className} – {courseName}";

			if (wps.SubstituteTeacherId.HasValue && !conflicts.ContainsKey(wps.SubstituteTeacherId.Value))
			{
				conflicts[wps.SubstituteTeacherId.Value] = desc;
			}

			if (wps.SubstituteAideId.HasValue && !conflicts.ContainsKey(wps.SubstituteAideId.Value))
			{
				conflicts[wps.SubstituteAideId.Value] = desc;
			}
		}

		var available = new List<AvailableStaffDto>();
		var busy = new List<BusyStaffDto>();

		foreach (var s in allStaff)
		{
			if (conflicts.TryGetValue(s.Id, out var conflictDesc))
			{
				busy.Add(new BusyStaffDto(s.Id, s.Name, s.Role, conflictDesc));
			}
			else
			{
				available.Add(new AvailableStaffDto(s.Id, s.Name, s.Role));
			}
		}

		return Ok(new StaffAvailabilityDto(available, busy));
	}

	/// <summary>
	/// Assigns (or clears) a substitute teacher/aide on a WeekPlanSlot.
	/// Creates the WeekPlanSlot lazily if it doesn't exist yet.
	/// </summary>
	[HttpPut("api/v1/week-plans/{weekPlanId:guid}/slots/{slotId:guid}/substitute")]
	public async Task<ActionResult> AssignSubstitute(
		Guid weekPlanId,
		Guid slotId,
		[FromBody] AssignSubstituteRequest req,
		CancellationToken cancellationToken)
	{
		if (req.SubstituteTeacherId.HasValue && req.SubstituteTeacherId == req.SubstituteAideId)
		{
			return Problem("Samme person kan ikke tildeles som både lærer og pædagog", statusCode: 400);
		}

		var weekPlan = await db.WeekPlans.FirstOrDefaultAsync(w => w.Id == weekPlanId, cancellationToken);
		if (weekPlan is null)
		{
			return NotFound();
		}

		if (req.SubstituteTeacherId.HasValue)
		{
			var exists = await db.Staff.AnyAsync(s => s.Id == req.SubstituteTeacherId.Value, cancellationToken);
			if (!exists)
			{
				return Problem("SubstituteTeacherId tilhører ikke denne lejer", statusCode: 400);
			}
		}

		if (req.SubstituteAideId.HasValue)
		{
			var exists = await db.Staff.AnyAsync(s => s.Id == req.SubstituteAideId.Value, cancellationToken);
			if (!exists)
			{
				return Problem("SubstituteAideId tilhører ikke denne lejer", statusCode: 400);
			}
		}

		var slot = await db.WeekPlanSlots
			.Include(s => s.SubstituteTeacher)
			.Include(s => s.SubstituteAide)
			.FirstOrDefaultAsync(s => s.Id == slotId && s.WeekPlanId == weekPlanId, cancellationToken);

		if (slot is null)
		{
			return NotFound();
		}

		slot.SubstituteTeacherId = req.SubstituteTeacherId;
		slot.SubstituteAideId = req.SubstituteAideId;
		slot.UpdatedAt = DateTimeOffset.UtcNow;

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new
		{
			slot.Id,
			slot.SubstituteTeacherId,
			SubstituteTeacherName = slot.SubstituteTeacher?.Name,
			slot.SubstituteAideId,
			SubstituteAideName = slot.SubstituteAide?.Name,
		});
	}

	private static bool Overlaps(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd) =>
		aStart < bEnd && bStart < aEnd;
}

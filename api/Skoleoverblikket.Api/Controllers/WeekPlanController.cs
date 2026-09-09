using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/classes/{classId:guid}/ugeplan")]
[Authorize]
public sealed class WeekPlanController(AppDbContext db, ITenantContext tenant, IAuthorizationService authz) : ControllerBase
{
	public record WeekPlanSlotFileDto(Guid Id, Guid SchoolFileId, string FileName, string Url);

	public record WeekPlanSlotDto(
		Guid Id,
		Guid SchemaSlotId,
		DayOfWeek Weekday,
		Guid TimeSlotId,
		string TimeSlotLabel,
		TimeOnly StartTime,
		TimeOnly EndTime,
		Guid CourseId,
		string CourseName,
		Guid? OriginalCourseId,
		string? OriginalCourseName,
		string? Beskrivelse,
		string? Lektier,
		IReadOnlyList<WeekPlanSlotFileDto> Files,
		Guid? SubstituteTeacherId,
		string? SubstituteTeacherName,
		Guid? SubstituteAideId,
		string? SubstituteAideName,
		Guid WeekPlanId);

	public record HolidayDayDto(DayOfWeek Weekday, string Title);

	public record BreakTimeSlotDto(Guid TimeSlotId, string TimeSlotLabel, TimeOnly StartTime, TimeOnly EndTime);

	public record WeekPlanDto(
		Guid Id,
		Guid ClassId,
		int IsoYear,
		int IsoWeek,
		DateOnly WeekStartDate,
		DateOnly WeekEndDate,
		bool IsHolidayWeek,
		string? HolidayTitle,
		IReadOnlyList<HolidayDayDto> HolidayDays,
		IReadOnlyList<BreakTimeSlotDto> BreakSlots,
		IReadOnlyList<WeekPlanSlotDto> Slots,
		string? Generelt);

	public record UpsertWeekPlanSlotRequest(
		Guid SchemaSlotId,
		string? Beskrivelse,
		string? Lektier,
		Guid? FagSwapCourseId);

	public record AddFileToSlotRequest(Guid SchoolFileId);

	public record UpdateGenereltRequest([property: StringLength(8000)] string? Generelt);

	public record GenereltDto(string? Generelt);

	[HttpGet]
	public async Task<ActionResult<WeekPlanDto>> GetWeekPlan(
		Guid classId,
		[FromQuery] int? isoYear,
		[FromQuery] int? isoWeek,
		[FromQuery] Guid? schemaId,
		CancellationToken cancellationToken)
	{
		if (isoYear is null || isoWeek is null)
		{
			return Problem("isoYear og isoWeek er påkrævet", statusCode: 400);
		}

		if (!IsoWeekValidation.IsValid(isoYear.Value, isoWeek.Value))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var klass = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
		if (klass is null)
		{
			return NotFound();
		}

		var weekStart = DateOnly.FromDateTime(ISOWeek.ToDateTime(isoYear.Value, isoWeek.Value, DayOfWeek.Monday));
		var weekEnd = weekStart.AddDays(4);

		var holidays = await db.CalendarEntries
			.AsNoTracking()
			.Where(e =>
				(e.Type == CalendarEntryType.Ferie || e.Type == CalendarEntryType.Lukkedag) &&
				e.StartDate <= weekEnd && e.EndDate >= weekStart)
			.OrderBy(e => e.StartDate)
			.ToListAsync(cancellationToken);

		// Only treat the entire week as a holiday if the full Mon–Fri span is covered.
		var isHolidayWeek = holidays.Count > 0 && IsFullWeekCovered(holidays, weekStart, weekEnd);
		var holidayTitle = holidays.FirstOrDefault()?.Title;

		var holidayDays = new List<HolidayDayDto>();
		for (var d = 0; d < 5; d++)
		{
			var date = weekStart.AddDays(d);
			var covering = holidays.FirstOrDefault(h => h.StartDate <= date && h.EndDate >= date);
			if (covering is not null)
			{
				holidayDays.Add(new HolidayDayDto(date.DayOfWeek, covering.Title));
			}
		}

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var activeSchema = schemaId.HasValue
			? await db.Schemas.AsNoTracking().FirstOrDefaultAsync(s => s.ClassId == classId && s.Id == schemaId.Value, cancellationToken)
			: await db.Schemas.AsNoTracking().FirstOrDefaultAsync(s => s.ClassId == classId && s.StartDate <= today && s.EndDate >= today, cancellationToken);

		if (activeSchema is null)
		{
			var weekPlanNoSchema = await db.WeekPlans
				.AsNoTracking()
				.FirstOrDefaultAsync(w => w.ClassId == classId && w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);

			return Ok(new WeekPlanDto(
				weekPlanNoSchema?.Id ?? Guid.Empty, classId, isoYear.Value, isoWeek.Value,
				weekStart, weekEnd, isHolidayWeek, holidayTitle, holidayDays, [], [], weekPlanNoSchema?.Generelt));
		}

		var schemaSlots = await db.SchemaSlots
			.AsNoTracking()
			.Include(s => s.TimeSlot)
			.Include(s => s.Course)
			.Where(s => s.SchemaId == activeSchema.Id)
			.ToListAsync(cancellationToken);

		var weekPlan = await db.WeekPlans
			.Include(w => w.Slots)
				.ThenInclude(s => s.Files)
					.ThenInclude(f => f.SchoolFile)
			.Include(w => w.Slots)
				.ThenInclude(s => s.FagSwapCourse)
			.Include(w => w.Slots)
				.ThenInclude(s => s.SubstituteTeacher)
			.Include(w => w.Slots)
				.ThenInclude(s => s.SubstituteAide)
			.FirstOrDefaultAsync(w => w.ClassId == classId && w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);

		var breakSlots = schemaSlots
			.Where(ss => ss.TimeSlot.IsBreak)
			.GroupBy(ss => ss.TimeSlotId)
			.Select(g =>
			{
				var ts = g.First().TimeSlot;
				return new BreakTimeSlotDto(ts.Id, ts.Label ?? ts.SortOrder.ToString(), ts.StartTime, ts.EndTime);
			})
			.OrderBy(b => b.StartTime)
			.ToList();

		var holidayWeekdays = holidayDays.Select(h => h.Weekday).ToHashSet();

		var slotDtos = schemaSlots
			.Where(ss => !ss.TimeSlot.IsBreak && !holidayWeekdays.Contains(ss.Weekday))
			.Select(ss =>
		{
			var wps = weekPlan?.Slots.FirstOrDefault(s => s.SchemaSlotId == ss.Id);
			var effectiveCourse = wps?.FagSwapCourse ?? ss.Course;
			var timeSlotLabel = ss.TimeSlot.Label ?? ss.TimeSlot.SortOrder.ToString();

			return new WeekPlanSlotDto(
				Id: wps?.Id ?? Guid.Empty,
				SchemaSlotId: ss.Id,
				Weekday: ss.Weekday,
				TimeSlotId: ss.TimeSlotId,
				TimeSlotLabel: timeSlotLabel,
				StartTime: ss.TimeSlot.StartTime,
				EndTime: ss.TimeSlot.EndTime,
				CourseId: effectiveCourse.Id,
				CourseName: effectiveCourse.Name,
				OriginalCourseId: wps?.FagSwapCourseId.HasValue == true ? ss.CourseId : null,
				OriginalCourseName: wps?.FagSwapCourseId.HasValue == true ? ss.Course.Name : null,
				Beskrivelse: wps?.Beskrivelse,
				Lektier: wps?.Lektier,
				Files: (wps?.Files ?? [])
					.Select(f => new WeekPlanSlotFileDto(f.Id, f.SchoolFileId, f.SchoolFile.FileName, f.SchoolFile.Url))
					.ToList(),
				SubstituteTeacherId: wps?.SubstituteTeacherId,
				SubstituteTeacherName: wps?.SubstituteTeacher?.Name,
				SubstituteAideId: wps?.SubstituteAideId,
				SubstituteAideName: wps?.SubstituteAide?.Name,
				WeekPlanId: weekPlan?.Id ?? Guid.Empty
			);
		}).ToList();

		return Ok(new WeekPlanDto(
			Id: weekPlan?.Id ?? Guid.Empty,
			ClassId: classId,
			IsoYear: isoYear.Value,
			IsoWeek: isoWeek.Value,
			WeekStartDate: weekStart,
			WeekEndDate: weekEnd,
			IsHolidayWeek: isHolidayWeek,
			HolidayTitle: holidayTitle,
			HolidayDays: holidayDays,
			BreakSlots: breakSlots,
			Slots: slotDtos,
			Generelt: weekPlan?.Generelt));
	}

	[HttpPut("slots")]
	public async Task<ActionResult<WeekPlanSlotDto>> UpsertSlot(
		Guid classId,
		[FromQuery] int? isoYear,
		[FromQuery] int? isoWeek,
		[FromQuery] Guid? schemaId,
		[FromBody] UpsertWeekPlanSlotRequest req,
		CancellationToken cancellationToken)
	{
		if (isoYear is null || isoWeek is null)
		{
			return Problem("isoYear og isoWeek er påkrævet", statusCode: 400);
		}

		if (!IsoWeekValidation.IsValid(isoYear.Value, isoWeek.Value))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
		if (klass is null)
		{
			return NotFound();
		}

		var today2 = DateOnly.FromDateTime(DateTime.UtcNow);
		var activeSchema = schemaId.HasValue
			? await db.Schemas.FirstOrDefaultAsync(s => s.ClassId == classId && s.Id == schemaId.Value, cancellationToken)
			: await db.Schemas
				.FirstOrDefaultAsync(s => s.ClassId == classId && s.StartDate <= today2 && s.EndDate >= today2, cancellationToken);

		var schemaSlot = activeSchema is null
			? null
			: await db.SchemaSlots
				.Include(s => s.TimeSlot)
				.Include(s => s.Course)
				.FirstOrDefaultAsync(s => s.SchemaId == activeSchema.Id && s.Id == req.SchemaSlotId, cancellationToken);

		if (schemaSlot is null)
		{
			return Problem("SchemaSlotId tilhører ikke det aktive skema for denne klasse", statusCode: 400);
		}

		var authResult = await authz.AuthorizeAsync(User, (classId, req.SchemaSlotId), Policies.EditWeekPlan);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		if (req.FagSwapCourseId.HasValue)
		{
			var courseExists = await db.Courses.AnyAsync(c => c.Id == req.FagSwapCourseId.Value, cancellationToken);
			if (!courseExists)
			{
				return Problem("FagSwapCourseId findes ikke under denne lejer", statusCode: 400);
			}
		}

		var weekPlan = await db.WeekPlans
			.FirstOrDefaultAsync(w => w.ClassId == classId && w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);

		if (weekPlan is null)
		{
			weekPlan = new WeekPlan
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				ClassId = classId,
				IsoYear = isoYear.Value,
				IsoWeek = isoWeek.Value,
			};
			db.WeekPlans.Add(weekPlan);
			await db.SaveChangesAsync(cancellationToken);
		}

		var slot = await db.WeekPlanSlots
			.Include(s => s.Files).ThenInclude(f => f.SchoolFile)
			.Include(s => s.FagSwapCourse)
			.FirstOrDefaultAsync(s => s.WeekPlanId == weekPlan.Id && s.SchemaSlotId == req.SchemaSlotId, cancellationToken);

		if (slot is null)
		{
			slot = new WeekPlanSlot
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				WeekPlanId = weekPlan.Id,
				SchemaSlotId = req.SchemaSlotId,
			};
			db.WeekPlanSlots.Add(slot);
		}

		slot.Beskrivelse = req.Beskrivelse;
		slot.Lektier = req.Lektier;
		slot.FagSwapCourseId = req.FagSwapCourseId;
		slot.UpdatedAt = DateTimeOffset.UtcNow;

		await db.SaveChangesAsync(cancellationToken);

		// Reload to get navigation props
		await db.Entry(slot).Reference(s => s.FagSwapCourse).LoadAsync(cancellationToken);
		await db.Entry(slot).Reference(s => s.SubstituteTeacher).LoadAsync(cancellationToken);
		await db.Entry(slot).Reference(s => s.SubstituteAide).LoadAsync(cancellationToken);

		var effectiveCourse = slot.FagSwapCourse ?? schemaSlot.Course;
		var timeSlotLabel = schemaSlot.TimeSlot.Label ?? schemaSlot.TimeSlot.SortOrder.ToString();

		return Ok(new WeekPlanSlotDto(
			Id: slot.Id,
			SchemaSlotId: schemaSlot.Id,
			Weekday: schemaSlot.Weekday,
			TimeSlotId: schemaSlot.TimeSlotId,
			TimeSlotLabel: timeSlotLabel,
			StartTime: schemaSlot.TimeSlot.StartTime,
			EndTime: schemaSlot.TimeSlot.EndTime,
			CourseId: effectiveCourse.Id,
			CourseName: effectiveCourse.Name,
			OriginalCourseId: slot.FagSwapCourseId.HasValue ? schemaSlot.CourseId : null,
			OriginalCourseName: slot.FagSwapCourseId.HasValue ? schemaSlot.Course.Name : null,
			Beskrivelse: slot.Beskrivelse,
			Lektier: slot.Lektier,
			Files: slot.Files
				.Select(f => new WeekPlanSlotFileDto(f.Id, f.SchoolFileId, f.SchoolFile.FileName, f.SchoolFile.Url))
				.ToList(),
			SubstituteTeacherId: slot.SubstituteTeacherId,
			SubstituteTeacherName: slot.SubstituteTeacher?.Name,
			SubstituteAideId: slot.SubstituteAideId,
			SubstituteAideName: slot.SubstituteAide?.Name,
			WeekPlanId: weekPlan.Id
		));
	}

	[HttpPut("generelt")]
	public async Task<ActionResult<GenereltDto>> UpdateGenerelt(
		Guid classId,
		[FromQuery] int? isoYear,
		[FromQuery] int? isoWeek,
		[FromBody] UpdateGenereltRequest req,
		CancellationToken cancellationToken)
	{
		if (isoYear is null || isoWeek is null)
		{
			return Problem("isoYear og isoWeek er påkrævet", statusCode: 400);
		}

		if (!IsoWeekValidation.IsValid(isoYear.Value, isoWeek.Value))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);
		if (klass is null)
		{
			return NotFound();
		}

		var authResult = await authz.AuthorizeAsync(User, (classId, Guid.Empty), Policies.EditWeekPlan);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		var weekPlan = await db.WeekPlans
			.FirstOrDefaultAsync(w => w.ClassId == classId && w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);

		if (weekPlan is null)
		{
			weekPlan = new WeekPlan
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				ClassId = classId,
				IsoYear = isoYear.Value,
				IsoWeek = isoWeek.Value,
			};
			db.WeekPlans.Add(weekPlan);

			try
			{
				await db.SaveChangesAsync(cancellationToken);
			}
			catch (DbUpdateException)
			{
				// A concurrent request created the same class/week WeekPlan; reload and reuse it.
				db.ChangeTracker.Clear();
				weekPlan = await db.WeekPlans
					.FirstAsync(w => w.ClassId == classId && w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);
			}
		}

		weekPlan.Generelt = req.Generelt;
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new GenereltDto(weekPlan.Generelt));
	}

	[HttpPost("slots/{slotId:guid}/files")]
	public async Task<ActionResult<WeekPlanSlotFileDto>> AddFile(
		Guid classId,
		Guid slotId,
		[FromBody] AddFileToSlotRequest req,
		CancellationToken cancellationToken)
	{
		var slot = await db.WeekPlanSlots
			.Include(s => s.WeekPlan)
			.FirstOrDefaultAsync(s => s.Id == slotId && s.WeekPlan.ClassId == classId, cancellationToken);

		if (slot is null)
		{
			return NotFound();
		}

		var authResult = await authz.AuthorizeAsync(User, (classId, slot.SchemaSlotId), Policies.EditWeekPlan);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		var fileExists = await db.SchoolFiles.AnyAsync(f => f.Id == req.SchoolFileId, cancellationToken);
		if (!fileExists)
		{
			return Problem("SchoolFileId findes ikke under denne lejer", statusCode: 400);
		}

		var link = new WeekPlanSlotFile
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			WeekPlanSlotId = slotId,
			SchoolFileId = req.SchoolFileId,
		};
		db.WeekPlanSlotFiles.Add(link);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
											ex.InnerException?.Message.Contains("duplicate") == true)
		{
			return Problem("Filen er allerede tilknyttet denne lektion", statusCode: 409);
		}

		var schoolFile = await db.SchoolFiles.AsNoTracking().FirstAsync(f => f.Id == req.SchoolFileId, cancellationToken);
		return CreatedAtAction(nameof(AddFile), new { classId, slotId },
			new WeekPlanSlotFileDto(link.Id, link.SchoolFileId, schoolFile.FileName, schoolFile.Url));
	}

	private static bool IsFullWeekCovered(IEnumerable<CalendarEntry> holidays, DateOnly weekStart, DateOnly weekEnd)
	{
		// Build a set of covered days Mon–Fri
		var coveredDays = new HashSet<DateOnly>();
		foreach (var h in holidays)
		{
			for (var d = h.StartDate; d <= h.EndDate; d = d.AddDays(1))
			{
				if (d >= weekStart && d <= weekEnd)
				{
					coveredDays.Add(d);
				}
			}
		}

		for (var d = weekStart; d <= weekEnd; d = d.AddDays(1))
		{
			if (!coveredDays.Contains(d))
			{
				return false;
			}
		}

		return true;
	}

	[HttpDelete("slots/{slotId:guid}/files/{fileId:guid}")]
	public async Task<ActionResult> RemoveFile(Guid classId, Guid slotId, Guid fileId, CancellationToken cancellationToken)
	{
		var link = await db.WeekPlanSlotFiles
			.Include(f => f.WeekPlanSlot).ThenInclude(s => s.WeekPlan)
			.FirstOrDefaultAsync(f => f.Id == fileId && f.WeekPlanSlotId == slotId && f.WeekPlanSlot.WeekPlan.ClassId == classId, cancellationToken);

		if (link is null)
		{
			return NotFound();
		}

		var authResult = await authz.AuthorizeAsync(User, (classId, link.WeekPlanSlot.SchemaSlotId), Policies.EditWeekPlan);
		if (!authResult.Succeeded)
		{
			return Forbid();
		}

		db.WeekPlanSlotFiles.Remove(link);
		await db.SaveChangesAsync(cancellationToken);
		return NoContent();
	}
}

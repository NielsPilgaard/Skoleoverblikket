using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Auth;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Controllers;

[ApiController]
[Route("api/v1/sfo/week-plan")]
[Authorize(Roles = Roles.Admin)]
public sealed class SfoWeekPlanController(AppDbContext db, ITenantContext tenant) : ControllerBase
{
	public record SfoWeekPlanShiftDto(
		Guid Id,
		Guid SfoShiftId,
		int DayOfWeek,
		string StartTime,
		string EndTime,
		string? Label,
		IReadOnlyList<SfoStaffRefDto> Staff,
		string? Beskrivelse);

	public record SfoStaffRefDto(Guid Id, string Name);

	public record SfoWeekPlanDto(
		Guid Id,
		int IsoYear,
		int IsoWeek,
		IReadOnlyList<SfoWeekPlanShiftDto> Shifts,
		string? Generelt);

	public record UpsertSfoWeekPlanShiftRequest(
		[Required] int IsoYear,
		[Required] int IsoWeek,
		[Required] Guid SfoShiftId,
		[StringLength(4000)] string? Beskrivelse);

	public record UpdateSfoNotesRequest(
		[Required] int IsoYear,
		[Required] int IsoWeek,
		[property: StringLength(8000)] string? Generelt);

	public record NotesDto(string? Generelt);

	[HttpGet]
	public async Task<ActionResult<SfoWeekPlanDto>> Get(
		[FromQuery] int? isoYear,
		[FromQuery] int? isoWeek,
		CancellationToken cancellationToken)
	{
		if (isoYear is null || isoWeek is null)
		{

			return Problem("årstal eller ugenummer er påkrævet", statusCode: 400);
		}

		if (!IsoWeekValidation.IsValid(isoYear.Value, isoWeek.Value))
		{

			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var shifts = await db.SfoShifts
			.AsNoTracking()
			.Include(s => s.StaffAssignments).ThenInclude(ss => ss.Staff)
			.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
			.ToListAsync(cancellationToken);

		var weekPlan = await db.SfoWeekPlans
			.AsNoTracking()
			.Include(w => w.Shifts)
			.FirstOrDefaultAsync(w => w.IsoYear == isoYear.Value && w.IsoWeek == isoWeek.Value, cancellationToken);

		var weekPlanId = weekPlan?.Id ?? Guid.Empty;

		var shiftDtos = shifts.Select(shift =>
		{
			var weekShift = weekPlan?.Shifts.FirstOrDefault(ws => ws.SfoShiftId == shift.Id);
			return new SfoWeekPlanShiftDto(
				weekShift?.Id ?? Guid.Empty,
				shift.Id,
				shift.DayOfWeek,
				shift.StartTime.ToString("HH:mm"),
				shift.EndTime.ToString("HH:mm"),
				shift.Label,
				shift.StaffAssignments.Select(sa => new SfoStaffRefDto(sa.StaffId, sa.Staff.Name)).ToList(),
				weekShift?.Beskrivelse);
		}).ToList();

		return Ok(new SfoWeekPlanDto(weekPlanId, isoYear.Value, isoWeek.Value, shiftDtos, weekPlan?.Generelt));
	}

	[HttpPut("generelt")]
	public async Task<ActionResult<NotesDto>> UpdateGenerelt(
		[FromBody] UpdateSfoNotesRequest request,
		CancellationToken cancellationToken)
	{
		if (!IsoWeekValidation.IsValid(request.IsoYear, request.IsoWeek))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var weekPlanId = await GetOrCreateWeekPlanId(request.IsoYear, request.IsoWeek, cancellationToken);
		if (weekPlanId is null)
		{
			return Problem("Kunne ikke oprette ugeplan", statusCode: 500);
		}

		var weekPlan = await db.SfoWeekPlans.FirstAsync(w => w.Id == weekPlanId.Value, cancellationToken);
		weekPlan.Generelt = request.Generelt;
		await db.SaveChangesAsync(cancellationToken);

		return Ok(new NotesDto(weekPlan.Generelt));
	}

	[HttpPut("shifts")]
	public async Task<ActionResult<SfoWeekPlanShiftDto>> UpsertShift(
		[FromBody] UpsertSfoWeekPlanShiftRequest request,
		CancellationToken cancellationToken)
	{
		if (!IsoWeekValidation.IsValid(request.IsoYear, request.IsoWeek))
		{
			return Problem("Ugyldigt årstal eller ugenummer", statusCode: 400);
		}

		var shift = await db.SfoShifts
			.AsNoTracking()
			.Include(s => s.StaffAssignments).ThenInclude(ss => ss.Staff)
			.FirstOrDefaultAsync(s => s.Id == request.SfoShiftId, cancellationToken);

		if (shift is null)
		{
			return NotFound();
		}

		var weekPlanId = await GetOrCreateWeekPlanId(request.IsoYear, request.IsoWeek, cancellationToken);
		if (weekPlanId is null)
		{
			return Problem("Kunne ikke oprette ugeplan", statusCode: 500);
		}

		var weekShift = await db.SfoWeekPlanShifts
			.FirstOrDefaultAsync(ws => ws.SfoWeekPlanId == weekPlanId.Value && ws.SfoShiftId == request.SfoShiftId, cancellationToken);

		if (weekShift is not null)
		{
			weekShift.Beskrivelse = request.Beskrivelse;
			weekShift.UpdatedAt = DateTimeOffset.UtcNow;
		}
		else
		{
			weekShift = new SfoWeekPlanShift
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				SfoWeekPlanId = weekPlanId.Value,
				SfoShiftId = request.SfoShiftId,
				Beskrivelse = request.Beskrivelse,
				UpdatedAt = DateTimeOffset.UtcNow,
			};
			db.SfoWeekPlanShifts.Add(weekShift);
		}

		await db.SaveChangesAsync(cancellationToken);

		return Ok(new SfoWeekPlanShiftDto(
			weekShift.Id,
			shift.Id,
			shift.DayOfWeek,
			shift.StartTime.ToString("HH:mm"),
			shift.EndTime.ToString("HH:mm"),
			shift.Label,
			shift.StaffAssignments.Select(sa => new SfoStaffRefDto(sa.StaffId, sa.Staff.Name)).ToList(),
			weekShift.Beskrivelse));
	}

	private async Task<Guid?> GetOrCreateWeekPlanId(int isoYear, int isoWeek, CancellationToken cancellationToken)
	{
		var id = await db.SfoWeekPlans
			.Where(w => w.IsoYear == isoYear && w.IsoWeek == isoWeek)
			.Select(w => (Guid?)w.Id)
			.FirstOrDefaultAsync(cancellationToken);

		if (id is not null)
		{
			return id;
		}

		db.SfoWeekPlans.Add(new SfoWeekPlan
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			IsoYear = isoYear,
			IsoWeek = isoWeek,
		});

		try { await db.SaveChangesAsync(cancellationToken); }
		catch (DbUpdateException) { db.ChangeTracker.Clear(); }

		return await db.SfoWeekPlans
			.Where(w => w.IsoYear == isoYear && w.IsoWeek == isoWeek)
			.Select(w => (Guid?)w.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}
}

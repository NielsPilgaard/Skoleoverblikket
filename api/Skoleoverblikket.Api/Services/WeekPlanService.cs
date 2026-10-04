using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;
using Skoleoverblikket.Api.Tenancy;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// The one place <see cref="WeekPlan"/> and <see cref="WeekPlanSlot"/> rows are created. Both are
/// created lazily — the first edit, note or vikar assignment for a klasse's week brings them into
/// existence — so several features need "get or create".
/// </summary>
public sealed class WeekPlanService(AppDbContext db, ITenantContext tenant)
{
	/// <summary>
	/// Returns the tracked week plan for the klasse and ISO week, creating and saving it if missing.
	/// A concurrent request creating the same row is tolerated: the unique index rejects the
	/// duplicate and the winner's row is reused.
	/// </summary>
	public async Task<WeekPlan> GetOrCreateWeekPlanAsync(
		Guid classId, int isoYear, int isoWeek, CancellationToken cancellationToken)
	{
		var weekPlan = await db.WeekPlans
			.FirstOrDefaultAsync(w => w.ClassId == classId && w.IsoYear == isoYear && w.IsoWeek == isoWeek, cancellationToken);

		if (weekPlan is not null)
		{
			return weekPlan;
		}

		weekPlan = new WeekPlan
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			ClassId = classId,
			IsoYear = isoYear,
			IsoWeek = isoWeek,
		};
		db.WeekPlans.Add(weekPlan);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
			return weekPlan;
		}
		catch (DbUpdateException)
		{
			db.Entry(weekPlan).State = EntityState.Detached;
			return await db.WeekPlans
				.FirstAsync(w => w.ClassId == classId && w.IsoYear == isoYear && w.IsoWeek == isoWeek, cancellationToken);
		}
	}

	/// <summary>
	/// Returns the tracked slot for the lektion in that week, adding (not saving) a new one if missing.
	/// The caller sets its fields and saves.
	/// </summary>
	public async Task<WeekPlanSlot> GetOrAddSlotAsync(
		WeekPlan weekPlan, Guid schemaSlotId, CancellationToken cancellationToken)
	{
		var slot = await db.WeekPlanSlots
			.FirstOrDefaultAsync(s => s.WeekPlanId == weekPlan.Id && s.SchemaSlotId == schemaSlotId, cancellationToken);

		if (slot is null)
		{
			slot = new WeekPlanSlot
			{
				Id = Guid.NewGuid(),
				TenantId = tenant.TenantId,
				WeekPlanId = weekPlan.Id,
				SchemaSlotId = schemaSlotId,
			};
			db.WeekPlanSlots.Add(slot);
		}

		return slot;
	}
}

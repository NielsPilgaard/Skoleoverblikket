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

	/// <summary>
	/// Saves the klasse's "Generelt for ugen". An unchanged text keeps its author and time, so
	/// re-saving doesn't tell parents the note is new. The author is the staff member's name, since
	/// that's who parents know, or the login's own name for someone who isn't staff.
	/// </summary>
	public async Task<WeekPlan> UpdateNotesAsync(
		Guid classId, int isoYear, int isoWeek, string? notes, string? subject, string? loginName, CancellationToken cancellationToken)
	{
		var weekPlan = await GetOrCreateWeekPlanAsync(classId, isoYear, isoWeek, cancellationToken);
		if (weekPlan.Notes == notes)
		{
			return weekPlan;
		}

		var staffName = subject is null
			? null
			: await db.Staff.AsNoTracking()
				.Where(s => s.KeycloakSubject == subject)
				.Select(s => s.Name)
				.FirstOrDefaultAsync(cancellationToken);

		weekPlan.Notes = notes;
		weekPlan.NotesUpdatedBy = notes is null ? null : staffName ?? loginName;
		weekPlan.NotesUpdatedAt = notes is null ? null : DateTimeOffset.UtcNow;
		await db.SaveChangesAsync(cancellationToken);
		return weekPlan;
	}

	public enum AddSlotFileOutcome
	{
		Added,
		FileNotFound,
		AlreadyAttached,
	}

	public record AddSlotFileResult(AddSlotFileOutcome Outcome, WeekPlanSlotFile? Link, SchoolFile? File);

	/// <summary>Attaches a school file to a lektion, remembering who did it for "Senest brugt af dig".</summary>
	public async Task<AddSlotFileResult> AddFileToSlotAsync(
		Guid slotId, Guid schoolFileId, string? attachedBySubject, CancellationToken cancellationToken)
	{
		var file = await db.SchoolFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == schoolFileId, cancellationToken);
		if (file is null)
		{
			return new AddSlotFileResult(AddSlotFileOutcome.FileNotFound, null, null);
		}

		var link = new WeekPlanSlotFile
		{
			Id = Guid.NewGuid(),
			TenantId = tenant.TenantId,
			WeekPlanSlotId = slotId,
			SchoolFileId = schoolFileId,
			AttachedBySubject = attachedBySubject,
		};
		db.WeekPlanSlotFiles.Add(link);

		try
		{
			await db.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
											ex.InnerException?.Message.Contains("duplicate") == true)
		{
			return new AddSlotFileResult(AddSlotFileOutcome.AlreadyAttached, null, null);
		}

		return new AddSlotFileResult(AddSlotFileOutcome.Added, link, file);
	}

	public record RecentFileDto(
		Guid Id,
		string FileName,
		string ContentType,
		long SizeBytes,
		string? CourseName,
		DateTimeOffset LastUsedAt);

	/// <summary>The files this user most recently attached to a lektion, newest first, each file once.</summary>
	public async Task<List<RecentFileDto>> GetRecentlyAttachedFilesAsync(
		string subject, int take, CancellationToken cancellationToken)
	{
		var recent = await db.WeekPlanSlotFiles
			.AsNoTracking()
			.Where(f => f.AttachedBySubject == subject)
			.GroupBy(f => f.SchoolFileId)
			.Select(g => new { SchoolFileId = g.Key, LastUsedAt = g.Max(f => f.AttachedAt) })
			.OrderByDescending(g => g.LastUsedAt)
			.Take(take)
			.ToListAsync(cancellationToken);

		var ids = recent.Select(r => r.SchoolFileId).ToList();
		var files = await db.SchoolFiles
			.AsNoTracking()
			.Where(f => ids.Contains(f.Id))
			.Select(f => new { f.Id, f.FileName, f.ContentType, f.SizeBytes, CourseName = f.Course != null ? f.Course.Name : null })
			.ToDictionaryAsync(f => f.Id, cancellationToken);

		return recent
			.Where(r => files.ContainsKey(r.SchoolFileId))
			.Select(r =>
			{
				var f = files[r.SchoolFileId];
				return new RecentFileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.CourseName, r.LastUsedAt);
			})
			.ToList();
	}
}

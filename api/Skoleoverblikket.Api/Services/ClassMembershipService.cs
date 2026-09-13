using Microsoft.EntityFrameworkCore;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Models;

namespace Skoleoverblikket.Api.Services;

/// <summary>
/// Resolves who belongs to a klasse. No staff↔klasse junction table exists — the association is
/// derived live from <see cref="SchemaSlot"/>.TeacherId/AideId on the klasse's schemas, which is
/// the same rule the schema grid itself uses. Substitutes are deliberately excluded: a vikar is a
/// day-specific <see cref="WeekPlanSlot"/> assignment, too transient for persistent membership.
///
/// Every query here runs through the DbContext global query filter, so results are tenant-scoped.
/// </summary>
public sealed class ClassMembershipService(AppDbContext db)
{
	/// <summary>
	/// Distinct staff ids teaching or assisting in <paramref name="classId"/>, taken from the
	/// klasse's currently active schema(s) — a schema counts as active when today falls within
	/// [StartDate, EndDate], treating a null bound as open-ended.
	/// </summary>
	public async Task<List<Guid>> GetStaffIdsAsync(Guid classId, CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);

		var slots = await db.SchemaSlots
			.AsNoTracking()
			.Where(s => s.Schema.ClassId == classId
					 && (s.Schema.StartDate == null || s.Schema.StartDate <= today)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= today))
			.Select(s => new { s.TeacherId, s.AideId })
			.ToListAsync(cancellationToken);

		return slots
			.SelectMany(s => s.AideId is null ? [s.TeacherId] : new[] { s.TeacherId, s.AideId.Value })
			.Distinct()
			.ToList();
	}

	/// <summary>Class ids <paramref name="staffId"/> is on the roster for.</summary>
	public async Task<List<Guid>> GetClassIdsForStaffAsync(Guid staffId, CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);

		return await db.SchemaSlots
			.AsNoTracking()
			.Where(s => (s.TeacherId == staffId || s.AideId == staffId)
					 && (s.Schema.StartDate == null || s.Schema.StartDate <= today)
					 && (s.Schema.EndDate == null || s.Schema.EndDate >= today))
			.Select(s => s.Schema.ClassId)
			.Distinct()
			.ToListAsync(cancellationToken);
	}

	/// <summary>
	/// Distinct parent ids with at least one student enrolled in <paramref name="classId"/>.
	/// Distinct matters: a parent with two children in the same klasse is one member, not two.
	/// </summary>
	public Task<List<Guid>> GetParentIdsAsync(Guid classId, CancellationToken cancellationToken) =>
		db.Parents
			.AsNoTracking()
			.Where(p => p.Students.Any(s => s.ClassId == classId))
			.Select(p => p.Id)
			.Distinct()
			.ToListAsync(cancellationToken);

	/// <summary>Class ids <paramref name="parentId"/> has a child enrolled in.</summary>
	public Task<List<Guid>> GetClassIdsForParentAsync(Guid parentId, CancellationToken cancellationToken) =>
		db.Students
			.AsNoTracking()
			.Where(s => s.Parents.Any(p => p.Id == parentId))
			.Select(s => s.ClassId)
			.Distinct()
			.ToListAsync(cancellationToken);
}

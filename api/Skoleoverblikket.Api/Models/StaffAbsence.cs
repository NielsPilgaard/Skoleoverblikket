using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// A staff member's own absence (sick, course, etc.) for a date range. Self-report is final — no
/// approval step. Vikar cover for the affected lektioner is stored on <see cref="WeekPlanSlot"/>
/// (SubstituteTeacherId / SubstituteAideId), the same place the ugeplan already shows it.
/// </summary>
public sealed class StaffAbsence : ITenantScoped, IEntityTypeConfiguration<StaffAbsence>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid StaffId { get; set; }
	public Staff Staff { get; set; } = null!;

	/// <summary>Who filed it: the staff member themself, or an admin on their behalf. Null for an admin without a staff row.</summary>
	public Guid? ReportedByStaffId { get; set; }
	public Staff? ReportedByStaff { get; set; }

	public DateOnly Date { get; set; }
	public DateOnly? EndDate { get; set; }

	/// <summary>Partial day: the window the staff member is out. Both null = the whole day. Only set on single-day absences.</summary>
	public TimeOnly? StartTime { get; set; }
	public TimeOnly? EndTime { get; set; }

	[StringLength(500)]
	public string? Reason { get; set; }

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<StaffAbsence> builder)
	{
		builder.HasOne(a => a.Staff).WithMany().HasForeignKey(a => a.StaffId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(a => a.ReportedByStaff).WithMany().HasForeignKey(a => a.ReportedByStaffId).OnDelete(DeleteBehavior.SetNull);
		builder.HasIndex(a => new { a.TenantId, a.Date });
	}
}

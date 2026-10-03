using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Models;

/// <summary>The three legal absence categories from BEK 1063/2019.</summary>
public enum AbsenceCategory
{
	/// <summary>Sygdom / funktionsnedsættelse o.l.</summary>
	Illness,

	/// <summary>Ekstraordinær frihed — granted by the principal on prior application.</summary>
	ExtraordinaryLeave,

	/// <summary>Ulovligt fravær — the only category with follow-up thresholds.</summary>
	Unauthorized,
}

/// <summary>Approval state of a parent's request for ekstraordinær frihed. Null on every other record.</summary>
public enum LeaveStatus { Pending, Approved, Rejected }

/// <summary>
/// One entry in a student's absence register (fravær). Created either by a parent (sick report or
/// leave request) or by staff when noting fremmøde. Exactly one of <see cref="ReportedByParentId"/>
/// and <see cref="RegisteredByStaffId"/> is set at creation (the parent link is cleared if the parent
/// is later deleted; the record stays because it belongs to the student).
/// </summary>
public sealed class AbsenceReport : ITenantScoped, IEntityTypeConfiguration<AbsenceReport>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid StudentId { get; set; }
	public Student Student { get; set; } = null!;
	public Guid? ReportedByParentId { get; set; }
	public Parent? ReportedByParent { get; set; }
	public Guid? RegisteredByStaffId { get; set; }
	public Staff? RegisteredByStaff { get; set; }
	public DateOnly Date { get; set; }
	public DateOnly? EndDate { get; set; }

	[StringLength(500)]
	public string? Reason { get; set; }

	public AbsenceCategory Category { get; set; }

	/// <summary>
	/// Present at the start of the day, gone at the end (7.–10. klasse only). Counts as half a day.
	/// Single-day records only.
	/// </summary>
	public bool HalfDay { get; set; }

	/// <summary>Set only for <see cref="AbsenceCategory.ExtraordinaryLeave"/> requested by a parent.</summary>
	public LeaveStatus? LeaveStatus { get; set; }
	public Guid? DecidedByStaffId { get; set; }
	public Staff? DecidedByStaff { get; set; }
	public DateTimeOffset? DecidedAt { get; set; }

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<AbsenceReport> builder)
	{
		builder.HasOne(a => a.Student)
			.WithMany()
			.HasForeignKey(a => a.StudentId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasOne(a => a.ReportedByParent)
			.WithMany()
			.HasForeignKey(a => a.ReportedByParentId)
			.OnDelete(DeleteBehavior.SetNull);

		builder.HasOne(a => a.RegisteredByStaff)
			.WithMany()
			.HasForeignKey(a => a.RegisteredByStaffId)
			.OnDelete(DeleteBehavior.SetNull);

		builder.HasOne(a => a.DecidedByStaff)
			.WithMany()
			.HasForeignKey(a => a.DecidedByStaffId)
			.OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(a => new { a.TenantId, a.Date });
		builder.HasIndex(a => new { a.TenantId, a.StudentId, a.Date });
		builder.HasIndex(a => new { a.TenantId, a.LeaveStatus });
	}
}

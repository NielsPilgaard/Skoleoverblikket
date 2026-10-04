using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// Follow-up state for one student in one calendar quarter once their ulovligt fravær reaches the
/// 10% flag: when staff were notified, and when someone marked the parents as informed.
/// </summary>
public sealed class AbsenceFollowUp : ITenantScoped, IEntityTypeConfiguration<AbsenceFollowUp>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid StudentId { get; set; }
	public Student Student { get; set; } = null!;

	/// <summary>First day of the calendar quarter (1 Jan, 1 Apr, 1 Jul or 1 Oct).</summary>
	public DateOnly QuarterStart { get; set; }

	public DateTimeOffset? WarningSentAt { get; set; }
	public DateTimeOffset? ParentsInformedAt { get; set; }
	public Guid? ParentsInformedByStaffId { get; set; }
	public Staff? ParentsInformedByStaff { get; set; }

	public void Configure(EntityTypeBuilder<AbsenceFollowUp> builder)
	{
		builder.HasOne(f => f.Student).WithMany().HasForeignKey(f => f.StudentId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(f => f.ParentsInformedByStaff).WithMany().HasForeignKey(f => f.ParentsInformedByStaffId).OnDelete(DeleteBehavior.SetNull);
		builder.HasIndex(f => new { f.StudentId, f.QuarterStart }).IsUnique();
	}
}

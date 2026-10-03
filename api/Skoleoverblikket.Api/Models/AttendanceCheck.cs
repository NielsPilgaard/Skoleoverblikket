using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>When in the school day fremmøde is noted. 7.–10. klasse need both.</summary>
public enum AttendanceCheckpoint { StartOfDay, EndOfDay }

/// <summary>
/// Marks "fremmøde noteret" for one klasse, date and checkpoint. Present students get no row of
/// their own — only absent students get an <see cref="AbsenceReport"/>.
/// </summary>
public sealed class AttendanceCheck : ITenantScoped, IEntityTypeConfiguration<AttendanceCheck>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid ClassId { get; set; }
	public Class Class { get; set; } = null!;
	public DateOnly Date { get; set; }
	public AttendanceCheckpoint Checkpoint { get; set; }
	public Guid? TakenByStaffId { get; set; }
	public Staff? TakenByStaff { get; set; }
	public DateTimeOffset TakenAt { get; set; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<AttendanceCheck> builder)
	{
		builder.HasOne(c => c.Class).WithMany().HasForeignKey(c => c.ClassId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(c => c.TakenByStaff).WithMany().HasForeignKey(c => c.TakenByStaffId).OnDelete(DeleteBehavior.SetNull);
		builder.HasIndex(c => new { c.ClassId, c.Date, c.Checkpoint }).IsUnique();
		builder.HasIndex(c => new { c.TenantId, c.Date });
	}
}

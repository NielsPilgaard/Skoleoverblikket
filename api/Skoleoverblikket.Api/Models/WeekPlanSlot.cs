using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

public sealed class WeekPlanSlot : ITenantScoped, IEntityTypeConfiguration<WeekPlanSlot>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid WeekPlanId { get; set; }
	public WeekPlan WeekPlan { get; set; } = null!;
	public Guid SchemaSlotId { get; set; }
	public SchemaSlot SchemaSlot { get; set; } = null!;

	[StringLength(8000)]
	public string? Description { get; set; }

	[StringLength(8000)]
	public string? Lektier { get; set; }

	/// <summary>Course override for this week. Null = use SchemaSlot.Course.</summary>
	public Guid? FagSwapCourseId { get; set; }
	public Course? FagSwapCourse { get; set; }

	/// <summary>Substitute teacher for this week only. Null = schema-assigned teacher covers.</summary>
	public Guid? SubstituteTeacherId { get; set; }
	public Staff? SubstituteTeacher { get; set; }

	/// <summary>Substitute aide for this week only. Null = schema-assigned aide covers.</summary>
	public Guid? SubstituteAideId { get; set; }
	public Staff? SubstituteAide { get; set; }

	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
	public ICollection<WeekPlanSlotFile> Files { get; set; } = [];

	public void Configure(EntityTypeBuilder<WeekPlanSlot> builder)
	{
		builder.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");
		builder.HasOne(s => s.WeekPlan).WithMany(w => w.Slots).HasForeignKey(s => s.WeekPlanId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(s => s.SchemaSlot).WithMany().HasForeignKey(s => s.SchemaSlotId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(s => s.FagSwapCourse).WithMany().HasForeignKey(s => s.FagSwapCourseId).OnDelete(DeleteBehavior.SetNull);
		builder.HasOne(s => s.SubstituteTeacher).WithMany().HasForeignKey(s => s.SubstituteTeacherId).OnDelete(DeleteBehavior.Restrict);
		builder.HasOne(s => s.SubstituteAide).WithMany().HasForeignKey(s => s.SubstituteAideId).OnDelete(DeleteBehavior.Restrict);
		builder.HasIndex(s => new { s.WeekPlanId, s.SchemaSlotId }).IsUnique();
	}
}

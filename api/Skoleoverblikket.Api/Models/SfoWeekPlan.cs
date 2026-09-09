using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

public sealed class SfoWeekPlan : ITenantScoped, IEntityTypeConfiguration<SfoWeekPlan>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public int IsoYear { get; set; }
	public int IsoWeek { get; set; }

	[StringLength(8000)]
	public string? Generelt { get; set; }

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
	public ICollection<SfoWeekPlanShift> Shifts { get; set; } = [];

	public void Configure(EntityTypeBuilder<SfoWeekPlan> builder)
	{
		builder.Property(w => w.CreatedAt).HasDefaultValueSql("now()");
		builder.HasIndex(w => new { w.TenantId, w.IsoYear, w.IsoWeek }).IsUnique();
	}
}

public sealed class SfoWeekPlanShift : ITenantScoped, IEntityTypeConfiguration<SfoWeekPlanShift>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid SfoWeekPlanId { get; set; }
	public SfoWeekPlan SfoWeekPlan { get; set; } = null!;
	public Guid SfoShiftId { get; set; }
	public SfoShift SfoShift { get; set; } = null!;

	[StringLength(4000)]
	public string? Beskrivelse { get; set; }

	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<SfoWeekPlanShift> builder)
	{
		builder.Property(s => s.UpdatedAt).ValueGeneratedNever();
		builder.HasOne(s => s.SfoWeekPlan).WithMany(w => w.Shifts).HasForeignKey(s => s.SfoWeekPlanId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(s => s.SfoShift).WithMany().HasForeignKey(s => s.SfoShiftId).OnDelete(DeleteBehavior.Cascade);
		builder.HasIndex(s => new { s.SfoWeekPlanId, s.SfoShiftId }).IsUnique();
	}
}

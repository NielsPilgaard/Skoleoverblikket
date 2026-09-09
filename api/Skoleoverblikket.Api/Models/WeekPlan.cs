using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

public sealed class WeekPlan : ITenantScoped, IEntityTypeConfiguration<WeekPlan>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid ClassId { get; set; }
	public Class Class { get; set; } = null!;
	public int IsoYear { get; set; }   // ISO year (distinct from calendar year for weeks 52/53)
	public int IsoWeek { get; set; }   // 1–53

	[StringLength(8000)]
	public string? Generelt { get; set; }

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
	public ICollection<WeekPlanSlot> Slots { get; set; } = [];

	public void Configure(EntityTypeBuilder<WeekPlan> builder)
	{
		builder.Property(w => w.CreatedAt).HasDefaultValueSql("now()");
		builder.HasOne(w => w.Class).WithMany().HasForeignKey(w => w.ClassId).OnDelete(DeleteBehavior.Cascade);
		builder.HasIndex(w => new { w.TenantId, w.ClassId, w.IsoYear, w.IsoWeek }).IsUnique();
	}
}

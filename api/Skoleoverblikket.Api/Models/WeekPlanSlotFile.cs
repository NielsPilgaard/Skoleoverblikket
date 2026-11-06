using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>Join table: attaches an existing SchoolFile to a WeekPlanSlot.</summary>
public sealed class WeekPlanSlotFile : ITenantScoped, IEntityTypeConfiguration<WeekPlanSlotFile>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }
	public Guid WeekPlanSlotId { get; set; }
	public WeekPlanSlot WeekPlanSlot { get; set; } = null!;
	public Guid SchoolFileId { get; set; }
	public SchoolFile SchoolFile { get; set; } = null!;

	/// <summary>Keycloak subject of whoever attached the file. Null on attachments made before this was tracked.</summary>
	[StringLength(200)]
	public string? AttachedBySubject { get; set; }

	public DateTimeOffset AttachedAt { get; init; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<WeekPlanSlotFile> builder)
	{
		builder.HasOne(f => f.WeekPlanSlot).WithMany(s => s.Files).HasForeignKey(f => f.WeekPlanSlotId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(f => f.SchoolFile).WithMany().HasForeignKey(f => f.SchoolFileId).OnDelete(DeleteBehavior.Cascade);
		builder.Property(f => f.AttachedAt).HasDefaultValueSql("now()");
		builder.HasIndex(f => new { f.WeekPlanSlotId, f.SchoolFileId }).IsUnique();
		builder.HasIndex(f => new { f.TenantId, f.AttachedBySubject, f.AttachedAt });
	}
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using System.ComponentModel.DataAnnotations;

namespace Skoleoverblikket.Api.Models;

public sealed class ComplianceCoverageSnapshot : ITenantScoped, IEntityTypeConfiguration<ComplianceCoverageSnapshot>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(20)]
	public string SchoolYear { get; set; } = null!;

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

	public Guid CreatedByStaffId { get; set; }
	public Staff CreatedByStaff { get; set; } = null!;

	[StringLength(500)]
	public string? Reason { get; set; }

	public int DataVersion { get; set; } = 1;

	public string Data { get; set; } = null!;

	public void Configure(EntityTypeBuilder<ComplianceCoverageSnapshot> builder)
	{
		builder.Property(s => s.Data).HasColumnType("jsonb");

		builder.HasOne(s => s.CreatedByStaff)
			.WithMany()
			.HasForeignKey(s => s.CreatedByStaffId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasIndex(s => new { s.TenantId, s.SchoolYear });
		builder.HasIndex(s => new { s.TenantId, s.CreatedAt });
	}
}

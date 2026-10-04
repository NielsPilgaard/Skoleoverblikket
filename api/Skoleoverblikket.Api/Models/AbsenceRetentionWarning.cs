using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// Records that admins were warned that a school year's absence data is about to be deleted.
/// Retention never deletes a school year without one of these rows (see AbsenceRetentionJob).
/// </summary>
public sealed class AbsenceRetentionWarning : ITenantScoped, IEntityTypeConfiguration<AbsenceRetentionWarning>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	/// <summary>Calendar year the school year starts in: 2025 = skoleåret 2025/26.</summary>
	public int SchoolYearStart { get; set; }

	public DateTimeOffset WarnedAt { get; set; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<AbsenceRetentionWarning> builder) =>
		builder.HasIndex(w => new { w.TenantId, w.SchoolYearStart }).IsUnique();
}

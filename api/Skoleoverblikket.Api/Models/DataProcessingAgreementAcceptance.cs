using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// Records that a school admin accepted a version of the databehandleraftale (GDPR Art. 28) on
/// behalf of the school. Name and email are copied, not linked, so the record survives the staff
/// member being deleted.
/// </summary>
public sealed class DataProcessingAgreementAcceptance : ITenantScoped, IEntityTypeConfiguration<DataProcessingAgreementAcceptance>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(20)]
	public required string Version { get; set; }

	[StringLength(200)]
	public required string AcceptedBySubject { get; set; }

	[StringLength(200)]
	public required string AcceptedByName { get; set; }

	[StringLength(500)]
	public string? AcceptedByEmail { get; set; }

	public DateTimeOffset AcceptedAt { get; set; }

	public void Configure(EntityTypeBuilder<DataProcessingAgreementAcceptance> builder) =>
		builder.HasIndex(a => new { a.TenantId, a.Version }).IsUnique();
}

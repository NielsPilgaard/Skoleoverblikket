using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;

namespace Skoleoverblikket.Api.Models;

/// <summary>
/// An external link the school curates for parents, staff or the board (Aula, fravær form, buskort,
/// a class Facebook group). Owned by <c>ExternalLinkService</c>.
/// </summary>
public sealed class ExternalLink : ITenantScoped, IEntityTypeConfiguration<ExternalLink>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(100, MinimumLength = 1)]
	public required string Title { get; set; }

	/// <summary>Absolute http or https URL.</summary>
	[StringLength(2000, MinimumLength = 1)]
	public required string Url { get; set; }

	[StringLength(300)]
	public string? Description { get; set; }

	public bool VisibleToParents { get; set; }
	public bool VisibleToStaff { get; set; }
	public bool VisibleToBoard { get; set; }

	/// <summary>
	/// True when the admin picked classes, so parents only see the link through those classes. Kept
	/// separately from <see cref="Classes"/> so a link whose classes were all deleted stays hidden from
	/// parents instead of widening to the whole school.
	/// </summary>
	public bool RestrictToClasses { get; set; }

	public ICollection<ExternalLinkClass> Classes { get; set; } = [];

	public int SortOrder { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<ExternalLink> builder)
	{
		builder.HasIndex(l => new { l.TenantId, l.SortOrder });

		builder.HasMany(l => l.Classes)
			   .WithOne()
			   .HasForeignKey(c => c.ExternalLinkId)
			   .OnDelete(DeleteBehavior.Cascade);
	}
}

/// <summary>
/// A class whose parents see an <see cref="ExternalLink"/>. An entity of its own rather than a skip
/// navigation so the row carries a TenantId, which the school export needs.
/// </summary>
public sealed class ExternalLinkClass : ITenantScoped, IEntityTypeConfiguration<ExternalLinkClass>
{
	public Guid TenantId { get; set; }
	public Guid ExternalLinkId { get; set; }
	public Guid ClassId { get; set; }
	public Class Class { get; set; } = null!;

	public void Configure(EntityTypeBuilder<ExternalLinkClass> builder)
	{
		builder.ToTable("ExternalLinkClasses");
		builder.HasKey(c => new { c.ExternalLinkId, c.ClassId });
		builder.HasOne(c => c.Class).WithMany().HasForeignKey(c => c.ClassId).OnDelete(DeleteBehavior.Cascade);
	}
}

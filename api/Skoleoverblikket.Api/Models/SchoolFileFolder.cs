using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skoleoverblikket.Api.Data;
using Skoleoverblikket.Api.Storage;

namespace Skoleoverblikket.Api.Models;

/// <summary>Mappe — a folder that organises school files.</summary>
public sealed class SchoolFileFolder : ITenantScoped, IFileFolder, IEntityTypeConfiguration<SchoolFileFolder>
{
	public Guid Id { get; set; }
	public Guid TenantId { get; set; }

	[StringLength(200, MinimumLength = 1)]
	public required string Name { get; set; }

	/// <summary>Parent folder. Null = root level.</summary>
	public Guid? ParentId { get; set; }
	public SchoolFileFolder? Parent { get; set; }

	/// <summary>Optional link to a course (fag). Null = not course-scoped.</summary>
	public Guid? CourseId { get; set; }
	public Course? Course { get; set; }

	public ICollection<SchoolFileFolder> Children { get; set; } = [];
	public ICollection<SchoolFile> Files { get; set; } = [];

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

	public void Configure(EntityTypeBuilder<SchoolFileFolder> builder)
	{
		builder.Property(f => f.CreatedAt).HasDefaultValueSql("now()");
		builder.HasOne(f => f.Parent)
			   .WithMany(f => f.Children)
			   .HasForeignKey(f => f.ParentId)
			   .OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(f => f.Course)
			   .WithMany()
			   .HasForeignKey(f => f.CourseId)
			   .OnDelete(DeleteBehavior.SetNull);
	}
}
